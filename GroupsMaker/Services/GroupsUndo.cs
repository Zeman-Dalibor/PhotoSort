using GroupsMaker.Models;

namespace GroupsMaker.Services;

/// <summary>Puts every file recorded in the manifest back where the grouping run found it.</summary>
public sealed class GroupsUndo(ManifestStore manifests)
{
    public UndoResult Restore(string root, string outputFolderName, bool dryRun, Action<string> warn)
    {
        var manifest = manifests.Load(root, outputFolderName);
        if (manifest is null)
        {
            throw new FileNotFoundException(
                $"No manifest at '{ManifestStore.ResolvePath(root, outputFolderName)}'. Nothing to undo.");
        }

        var restored = 0;
        var missing = 0;
        var blocked = 0;

        foreach (var entry in manifest.Entries.AsEnumerable().Reverse())
        {
            var current = Path.Combine(root, entry.To);
            var original = Path.Combine(root, entry.From);

            if (!File.Exists(current))
            {
                warn($"Already gone: {entry.To}");
                missing++;
                continue;
            }

            if (File.Exists(original))
            {
                warn($"Occupied, left in place: {entry.From}");
                blocked++;
                continue;
            }

            if (!dryRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                File.Move(current, original);
            }

            restored++;
        }

        if (!dryRun && blocked == 0)
        {
            RemoveEmptyOutput(root, outputFolderName);
        }

        return new UndoResult(restored, missing, blocked);
    }

    /// <summary>Deletes the manifest and any group folder left empty; keeps anything the user added.</summary>
    private static void RemoveEmptyOutput(string root, string outputFolderName)
    {
        var outputRoot = Path.Combine(root, outputFolderName);
        if (!Directory.Exists(outputRoot))
        {
            return;
        }

        var manifestPath = Path.Combine(outputRoot, GroupsManifest.FileName);
        if (File.Exists(manifestPath))
        {
            File.Delete(manifestPath);
        }

        foreach (var folder in Directory.EnumerateDirectories(outputRoot, "*", SearchOption.AllDirectories)
                     .OrderByDescending(f => f.Length))
        {
            TryDeleteIfEmpty(folder);
        }

        TryDeleteIfEmpty(outputRoot);
    }

    private static void TryDeleteIfEmpty(string folder)
    {
        try
        {
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Leaving an empty folder behind is harmless.
        }
    }
}

/// <param name="Restored">Files moved back.</param>
/// <param name="Missing">Files the manifest expected but no longer found.</param>
/// <param name="Blocked">Files left alone because something already sits at the original path.</param>
public sealed record UndoResult(int Restored, int Missing, int Blocked);
