using PhotoSort.Models;

namespace PhotoSort.Services;

/// <summary>
/// Moves all files of a photo between the root folder and the edit/archive/delete sub-folders.
/// Never deletes anything: "delete" is just another folder.
/// </summary>
public sealed class PhotoFileMover
{
    private const int MaxCollisionAttempts = 1000;

    /// <summary>
    /// Moves every variant of <paramref name="item"/> into the folder for <paramref name="target"/>.
    /// Returns <c>null</c> when the item is already there.
    /// </summary>
    public MoveRecord? Move(PhotoItem item, PhotoCategory target, string rootPath)
    {
        if (item.Category == target)
        {
            return null;
        }

        var targetDirectory = CategoryFolders.ResolveDirectory(rootPath, target);
        Directory.CreateDirectory(targetDirectory);

        var suffix = FindFreeSuffix(item, targetDirectory);
        var changes = new List<PathChange>(item.Variants.Count);

        foreach (var variant in item.Variants)
        {
            var newPath = Path.Combine(targetDirectory, item.DisplayName + suffix + variant.Extension);
            File.Move(variant.FullPath, newPath);
            changes.Add(new PathChange(variant.FullPath, newPath));
        }

        var record = new MoveRecord(item, item.Category, target, changes);
        item.ApplyRelocation(target, changes.Select(c => c.NewPath).ToList());
        return record;
    }

    /// <summary>Puts the files back where <paramref name="record"/> found them.</summary>
    public void Undo(MoveRecord record)
    {
        foreach (var change in record.Paths)
        {
            var directory = Path.GetDirectoryName(change.OldPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Move(change.NewPath, change.OldPath);
        }

        record.Item.ApplyRelocation(record.PreviousCategory, record.Paths.Select(c => c.OldPath).ToList());
    }

    /// <summary>
    /// Finds a " (n)" suffix that is free for every variant at once, so a JPG+CR2 pair keeps
    /// sharing one base name after the move.
    /// </summary>
    private static string FindFreeSuffix(PhotoItem item, string targetDirectory)
    {
        for (var attempt = 0; attempt < MaxCollisionAttempts; attempt++)
        {
            var suffix = attempt == 0 ? string.Empty : $" ({attempt})";
            var free = item.Variants.All(v =>
                !File.Exists(Path.Combine(targetDirectory, item.DisplayName + suffix + v.Extension)));

            if (free)
            {
                return suffix;
            }
        }

        throw new IOException($"Could not find a free file name for '{item.DisplayName}' in '{targetDirectory}'.");
    }
}
