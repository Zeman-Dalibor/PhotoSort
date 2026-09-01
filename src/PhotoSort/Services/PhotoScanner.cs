using PhotoSort.Models;

namespace PhotoSort.Services;

/// <summary>Builds the working list of <see cref="PhotoItem"/> from a root folder.</summary>
public sealed class PhotoScanner
{
    /// <param name="rootPath">Folder chosen by the user.</param>
    /// <param name="includeFilterFolders">Also list photos already sorted into edit/archive/delete.</param>
    public IReadOnlyList<PhotoItem> Scan(string rootPath, bool includeFilterFolders)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Folder '{rootPath}' does not exist.");
        }

        var items = new List<PhotoItem>();
        items.AddRange(ScanDirectory(rootPath, PhotoCategory.None));

        if (includeFilterFolders)
        {
            foreach (var category in CategoryFolders.Filterable)
            {
                var directory = CategoryFolders.ResolveDirectory(rootPath, category);
                if (Directory.Exists(directory))
                {
                    items.AddRange(ScanDirectory(directory, category));
                }
            }
        }

        return items
            .OrderBy(i => i.Category)
            .ThenBy(i => i.DisplayName, NaturalStringComparer.Instance)
            .ToList();
    }

    private static IEnumerable<PhotoItem> ScanDirectory(string directory, PhotoCategory category)
    {
        var groups = new Dictionary<string, List<PhotoVariant>>(StringComparer.OrdinalIgnoreCase);
        var displayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in EnumerateFilesSafely(directory))
        {
            if (!SupportedFormats.IsSupported(Path.GetExtension(path)))
            {
                continue;
            }

            var baseName = Path.GetFileNameWithoutExtension(path);
            var size = TryGetLength(path);
            if (size < 0)
            {
                continue;
            }

            if (!groups.TryGetValue(baseName, out var variants))
            {
                variants = [];
                groups[baseName] = variants;
                displayNames[baseName] = baseName;
            }

            variants.Add(new PhotoVariant(path, size));
        }

        return groups.Select(g => new PhotoItem(displayNames[g.Key], category, g.Value));
    }

    private static IEnumerable<string> EnumerateFilesSafely(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static long TryGetLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return -1;
        }
    }
}
