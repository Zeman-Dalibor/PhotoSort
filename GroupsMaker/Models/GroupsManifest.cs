namespace GroupsMaker.Models;

/// <summary>
/// Written into the output folder so every move can be undone later. Paths are relative to the
/// input folder, which keeps the manifest valid even after the whole tree is moved or renamed.
/// </summary>
public sealed record GroupsManifest(
    int Version,
    DateTimeOffset CreatedAt,
    string OutputFolder,
    IReadOnlyList<ManifestEntry> Entries)
{
    public const int CurrentVersion = 1;

    public const string FileName = "groups-manifest.json";
}

/// <param name="Group">Name of the group folder the file was moved into.</param>
/// <param name="From">Original path, relative to the input folder.</param>
/// <param name="To">Path after the move, relative to the input folder.</param>
public sealed record ManifestEntry(string Group, string From, string To);
