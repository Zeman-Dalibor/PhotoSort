using GroupsMaker.Models;

namespace GroupsMaker.Services;

/// <summary>
/// Creates one folder per group inside the output folder and moves every file of the group there.
/// Nothing is ever deleted or overwritten, and every move is recorded so it can be undone.
/// </summary>
public sealed class GroupWriter
{
    private const int MaxCollisionAttempts = 1000;
    private const int MaxNameLength = 40;

    /// <param name="root">Input folder.</param>
    /// <param name="outputFolderName">Sub-folder of <paramref name="root"/> receiving the groups.</param>
    /// <param name="dryRun">Compute the plan without touching the file system.</param>
    public GroupsManifest Write(
        string root,
        string outputFolderName,
        IReadOnlyList<PhotoGroup> groups,
        bool dryRun)
    {
        var outputRoot = Path.Combine(root, outputFolderName);
        var entries = new List<ManifestEntry>();

        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var folderName = BuildFolderName(i + 1, group);
            var folder = Path.Combine(outputRoot, folderName);

            if (!dryRun)
            {
                Directory.CreateDirectory(folder);
            }

            foreach (var member in group.Members)
            {
                var suffix = FindFreeSuffix(member.Photo, folder);

                foreach (var file in member.Photo.Files)
                {
                    var target = Path.Combine(folder, member.Photo.Name + suffix + Path.GetExtension(file));

                    if (!dryRun)
                    {
                        File.Move(file, target);
                    }

                    entries.Add(new ManifestEntry(
                        folderName,
                        Path.GetRelativePath(root, file),
                        Path.GetRelativePath(root, target)));
                }
            }
        }

        return new GroupsManifest(
            GroupsManifest.CurrentVersion,
            DateTimeOffset.Now,
            outputFolderName,
            entries);
    }

    /// <summary>
    /// <c>007_IMG_0042_5x</c> — the ordinal keeps folders sorted and unique, the name tells the
    /// user what is inside and the count tells them how much there is to pick from.
    /// </summary>
    private static string BuildFolderName(int ordinal, PhotoGroup group)
    {
        var name = Sanitize(group.First.Photo.Name);
        if (name.Length > MaxNameLength)
        {
            name = name[..MaxNameLength];
        }

        return $"{ordinal:D3}_{name}_{group.Count}x";
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. name.Select(c => invalid.Contains(c) ? '_' : c)]).Trim(' ', '.');
        return cleaned.Length == 0 ? "photo" : cleaned;
    }

    /// <summary>
    /// Finds a " (n)" suffix free for every file of the set at once, so a JPG+CR2 pair keeps
    /// sharing one base name after the move.
    /// </summary>
    private static string FindFreeSuffix(PhotoSet photo, string targetDirectory)
    {
        for (var attempt = 0; attempt < MaxCollisionAttempts; attempt++)
        {
            var suffix = attempt == 0 ? string.Empty : $" ({attempt})";
            var free = photo.Files.All(file =>
                !File.Exists(Path.Combine(targetDirectory, photo.Name + suffix + Path.GetExtension(file))));

            if (free)
            {
                return suffix;
            }
        }

        throw new IOException($"Could not find a free file name for '{photo.Name}' in '{targetDirectory}'.");
    }
}
