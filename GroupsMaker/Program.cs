using System.Globalization;
using System.Text;
using GroupsMaker.Models;
using GroupsMaker.Services;
using PhotoSort.Services;

namespace GroupsMaker;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;
    private const int ExitUsage = 2;

    private const int ProgressStep = 25;

    private static int Main(string[] args)
    {
        UseUtf8Console();

        if (!Options.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Options.HelpText);
            return ExitUsage;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(Options.HelpText);
            return ExitSuccess;
        }

        try
        {
            return options.Undo ? RunUndo(options) : RunGrouping(options);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Error: {e.Message}");
            return ExitFailure;
        }
    }

    private static int RunGrouping(Options options)
    {
        Console.WriteLine($"Folder: {options.InputFolder}");

        var photos = new PhotoSetScanner().Scan(options.InputFolder, options.OutputFolderName, options.Recursive);
        Console.WriteLine($"Found:  {photos.Count} photos in {photos.Sum(p => p.Files.Count)} files");

        if (photos.Count == 0)
        {
            Console.WriteLine("Nothing to group.");
            return ExitSuccess;
        }

        var fingerprints = Fingerprint(photos, options.Threads);
        ReportFingerprintProblems(fingerprints);

        var grouper = new BurstGrouper(
            options.TimeGap, options.BurstDistance, options.DuplicateDistance, options.MinGroupSize);
        var groups = grouper.Group(fingerprints);

        var grouped = groups.Sum(g => g.Count);
        Console.WriteLine($"Groups: {groups.Count} holding {grouped} photos, {photos.Count - grouped} left alone");

        if (groups.Count == 0)
        {
            Console.WriteLine("No bursts or duplicates found; nothing was moved.");
            return ExitSuccess;
        }

        PrintGroups(groups);

        var manifest = new GroupWriter().Write(
            options.InputFolder, options.OutputFolderName, groups, options.DryRun);

        var outputRoot = Path.Combine(options.InputFolder, options.OutputFolderName);
        Console.WriteLine();

        if (options.DryRun)
        {
            Console.WriteLine($"Dry run: {manifest.Entries.Count} files would move into {outputRoot}");
            return ExitSuccess;
        }

        new ManifestStore().Save(options.InputFolder, options.OutputFolderName, manifest);
        Console.WriteLine($"Moved {manifest.Entries.Count} files into {outputRoot}");
        Console.WriteLine($"Undo with: GroupsMaker \"{options.InputFolder}\" --undo");
        return ExitSuccess;
    }

    private static int RunUndo(Options options)
    {
        Console.WriteLine($"Folder: {options.InputFolder}");

        var undo = new GroupsUndo(new ManifestStore());
        var result = undo.Restore(
            options.InputFolder, options.OutputFolderName, options.DryRun, Console.Error.WriteLine);

        var verb = options.DryRun ? "would be restored" : "restored";
        Console.WriteLine($"{result.Restored} files {verb}, {result.Missing} missing, {result.Blocked} blocked");
        return result.Blocked > 0 ? ExitFailure : ExitSuccess;
    }

    /// <summary>
    /// Fingerprinting is CPU bound and independent per file, so unlike the desktop app this runs
    /// on every core.
    /// </summary>
    private static IReadOnlyList<PhotoFingerprint> Fingerprint(IReadOnlyList<PhotoSet> photos, int threads)
    {
        var fingerprinter = new ImageFingerprinter(new ThumbnailDecoder(new TiffPreviewExtractor()));
        var results = new PhotoFingerprint[photos.Count];
        var done = 0;

        Parallel.For(0, photos.Count, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
        {
            results[i] = fingerprinter.Create(photos[i]);
            ReportProgress(Interlocked.Increment(ref done), photos.Count);
        });

        ClearProgress();
        return results;
    }

    private static void ReportFingerprintProblems(IReadOnlyList<PhotoFingerprint> fingerprints)
    {
        foreach (var failed in fingerprints.Where(f => f.Hash is null))
        {
            Console.Error.WriteLine($"Skipped {failed.Photo.PreviewFile}: {failed.Error}");
        }

        if (fingerprints.Any(f => f.Hash is not null) && fingerprints.All(f => !f.TimeFromExif))
        {
            Console.WriteLine("Note:   no EXIF capture times found, falling back to file timestamps.");
        }
    }

    private static void PrintGroups(IReadOnlyList<PhotoGroup> groups)
    {
        Console.WriteLine();

        foreach (var (group, ordinal) in groups.Select((g, i) => (g, i + 1)))
        {
            var taken = group.First.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            Console.WriteLine(
                $"  {ordinal,3}. {group.First.Photo.Name,-24} {group.Count,3} photos  {taken}  " +
                $"span {FormatSpan(group.Span)}");
        }
    }

    private static string FormatSpan(TimeSpan span) => span.TotalSeconds switch
    {
        < 60 => string.Format(CultureInfo.InvariantCulture, "{0:0.0} s", span.TotalSeconds),
        < 3600 => string.Format(CultureInfo.InvariantCulture, "{0:0.0} min", span.TotalMinutes),
        _ => string.Format(CultureInfo.InvariantCulture, "{0:0.0} h", span.TotalHours)
    };

    private static void ReportProgress(int done, int total)
    {
        if (Console.IsOutputRedirected || (done % ProgressStep != 0 && done != total))
        {
            return;
        }

        Console.Write($"\rHashing: {done}/{total}");
    }

    private static void ClearProgress()
    {
        if (!Console.IsOutputRedirected)
        {
            Console.Write('\r');
            Console.Write(new string(' ', 32));
            Console.Write('\r');
        }
    }

    /// <summary>Without this the Windows console mangles file names with diacritics.</summary>
    private static void UseUtf8Console()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // Redirected to a handle that cannot change encoding; plain ASCII output still works.
        }
    }
}
