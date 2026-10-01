using System.Globalization;
using GroupsMaker.Services;

namespace GroupsMaker;

/// <summary>Command line configuration. <see cref="HelpText"/> is the user-facing contract.</summary>
public sealed class Options
{
    public string InputFolder { get; private set; } = string.Empty;

    public string OutputFolderName { get; private set; } = "Groups";

    /// <summary>Longest pause still counted as the same burst.</summary>
    public TimeSpan TimeGap { get; private set; } = TimeSpan.FromSeconds(10);

    /// <summary>Hash distance allowed between two shots of one burst, in bits out of 64.</summary>
    public int BurstDistance { get; private set; } = 10;

    /// <summary>Hash distance below which two shots group regardless of time. Negative disables it.</summary>
    public int DuplicateDistance { get; private set; } = 4;

    public int MinGroupSize { get; private set; } = 2;

    public bool Recursive { get; private set; }

    public bool DryRun { get; private set; }

    public bool Undo { get; private set; }

    public int Threads { get; private set; } = Environment.ProcessorCount;

    public bool ShowHelp { get; private set; }

    public static string HelpText =>
        $"""
         GroupsMaker — groups bursts and duplicates so only the best shot has to be picked.

         Usage:
           GroupsMaker <folder> [options]
           GroupsMaker <folder> --undo

         Every group becomes one sub-folder of <folder>/Groups and the matching photos are moved
         into it. Photos without a partner stay untouched. A JPG+CR2 pair always travels together.

         Options:
           -o, --output <name>     Folder created inside <folder>. Default: Groups
           -t, --time-gap <sec>    Longest pause inside one burst. Default: 10
           -s, --similarity <n>    Hash distance for a burst, 0-{DifferenceHash.Bits}. Lower is stricter. Default: 10
           -d, --duplicate <n>     Hash distance that groups photos regardless of time,
                                   0-{DifferenceHash.Bits}, or -1 to switch the rule off. Default: 4
           -m, --min-size <n>      Smallest group worth a folder. Default: 2
           -r, --recursive         Also scan sub-folders.
           -n, --dry-run           Print the plan, move nothing.
           -j, --threads <n>       Files fingerprinted in parallel. Default: {Environment.ProcessorCount}
           -u, --undo              Move everything back using Groups/{Models.GroupsManifest.FileName}.
           -h, --help              This text.

         Examples:
           GroupsMaker "D:\Photos\2026-05 Iceland"
           GroupsMaker "D:\Photos" --dry-run --time-gap 3 --similarity 6
           GroupsMaker "D:\Photos" --undo
         """;

    public static bool TryParse(string[] args, out Options options, out string? error)
    {
        var result = new Options();
        error = null;
        options = result;

        for (var i = 0; i < args.Length; i++)
        {
            var (name, inlineValue) = SplitArgument(args[i]);

            switch (name)
            {
                case "-h" or "--help" or "/?":
                    result.ShowHelp = true;
                    break;
                case "-r" or "--recursive":
                    result.Recursive = true;
                    break;
                case "-n" or "--dry-run":
                    result.DryRun = true;
                    break;
                case "-u" or "--undo":
                    result.Undo = true;
                    break;
                case "-o" or "--output":
                    if (!TryTakeValue(args, ref i, name, inlineValue, out var output, out error)) return false;
                    result.OutputFolderName = output;
                    break;
                case "-t" or "--time-gap":
                    if (!TryTakeNumber(args, ref i, name, inlineValue, out var seconds, out error)) return false;
                    result.TimeGap = TimeSpan.FromSeconds(seconds);
                    break;
                case "-s" or "--similarity":
                    if (!TryTakeNumber(args, ref i, name, inlineValue, out var similarity, out error)) return false;
                    result.BurstDistance = (int)similarity;
                    break;
                case "-d" or "--duplicate":
                    if (!TryTakeNumber(args, ref i, name, inlineValue, out var duplicate, out error)) return false;
                    result.DuplicateDistance = (int)duplicate;
                    break;
                case "-m" or "--min-size":
                    if (!TryTakeNumber(args, ref i, name, inlineValue, out var minSize, out error)) return false;
                    result.MinGroupSize = (int)minSize;
                    break;
                case "-j" or "--threads":
                    if (!TryTakeNumber(args, ref i, name, inlineValue, out var threads, out error)) return false;
                    result.Threads = (int)threads;
                    break;
                default:
                    if (name.StartsWith('-'))
                    {
                        error = $"Unknown option '{name}'.";
                        return false;
                    }

                    if (result.InputFolder.Length > 0)
                    {
                        error = $"Only one folder can be given, got '{result.InputFolder}' and '{name}'.";
                        return false;
                    }

                    result.InputFolder = name;
                    break;
            }
        }

        return result.ShowHelp || result.Validate(out error);
    }

    private bool Validate(out string? error)
    {
        error = FindProblem();

        if (error is null)
        {
            InputFolder = Path.GetFullPath(InputFolder);
        }

        return error is null;
    }

    private string? FindProblem()
    {
        if (InputFolder.Length == 0)
        {
            return "No input folder given.";
        }

        if (OutputFolderName.Length == 0 || OutputFolderName.Intersect(Path.GetInvalidFileNameChars()).Any())
        {
            return $"'{OutputFolderName}' is not a valid folder name.";
        }

        if (TimeGap < TimeSpan.Zero)
        {
            return "--time-gap cannot be negative.";
        }

        if (BurstDistance < 0 || BurstDistance > DifferenceHash.Bits)
        {
            return $"--similarity must be between 0 and {DifferenceHash.Bits}.";
        }

        if (DuplicateDistance > DifferenceHash.Bits)
        {
            return $"--duplicate must be at most {DifferenceHash.Bits}.";
        }

        if (MinGroupSize < 2)
        {
            return "--min-size must be at least 2.";
        }

        return Threads < 1 ? "--threads must be at least 1." : null;
    }

    /// <summary>Accepts both <c>--time-gap 3</c> and <c>--time-gap=3</c>.</summary>
    private static (string Name, string? Value) SplitArgument(string argument)
    {
        var separator = argument.IndexOf('=');
        return separator > 0
            ? (argument[..separator], argument[(separator + 1)..])
            : (argument, null);
    }

    private static bool TryTakeValue(
        string[] args, ref int index, string name, string? inlineValue, out string value, out string? error)
    {
        if (inlineValue is not null)
        {
            value = inlineValue;
            error = null;
            return true;
        }

        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            error = $"Option '{name}' needs a value.";
            return false;
        }

        value = args[++index];
        error = null;
        return true;
    }

    private static bool TryTakeNumber(
        string[] args, ref int index, string name, string? inlineValue, out double value, out string? error)
    {
        value = 0;

        if (!TryTakeValue(args, ref index, name, inlineValue, out var text, out error))
        {
            return false;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            error = $"Option '{name}' expects a number, got '{text}'.";
            return false;
        }

        return true;
    }
}
