using GroupsMaker.Models;
using PhotoSort.Services;

namespace GroupsMaker.Services;

/// <summary>Turns a folder into the list of photographs to compare, one entry per file name.</summary>
public sealed class PhotoSetScanner
{
    /// <param name="root">Folder chosen by the user.</param>
    /// <param name="outputFolderName">Sub-folder holding the groups; never scanned back in.</param>
    /// <param name="recursive">Also walk sub-folders of <paramref name="root"/>.</param>
    public IReadOnlyList<PhotoSet> Scan(string root, string outputFolderName, bool recursive)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Folder '{root}' does not exist.");
        }

        var excluded = Path.Combine(root, outputFolderName) + Path.DirectorySeparatorChar;
        var sets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System
        };

        foreach (var path in Directory.EnumerateFiles(root, "*", options))
        {
            if (!SupportedFormats.IsSupported(Path.GetExtension(path)) ||
                path.StartsWith(excluded, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = Path.Combine(Path.GetDirectoryName(path) ?? root, Path.GetFileNameWithoutExtension(path));
            if (!sets.TryGetValue(key, out var files))
            {
                files = [];
                sets[key] = files;
                names[key] = Path.GetFileNameWithoutExtension(path);
            }

            files.Add(path);
        }

        return sets
            .Select(pair => new PhotoSet(
                Path.GetDirectoryName(pair.Key) ?? root,
                names[pair.Key],
                [.. pair.Value.OrderBy(f => SupportedFormats.DisplayRank(Path.GetExtension(f)))]))
            .OrderBy(set => set.Directory, StringComparer.OrdinalIgnoreCase)
            .ThenBy(set => set.Name, NaturalStringComparer.Instance)
            .ToList();
    }
}
