using System.Text.Json;
using GroupsMaker.Models;

namespace GroupsMaker.Services;

/// <summary>Reads and writes the undo manifest that lives inside the output folder.</summary>
public sealed class ManifestStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string ResolvePath(string root, string outputFolderName) =>
        Path.Combine(root, outputFolderName, GroupsManifest.FileName);

    public void Save(string root, string outputFolderName, GroupsManifest manifest)
    {
        var path = ResolvePath(root, outputFolderName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, Options));
    }

    /// <summary>Returns <c>null</c> when there is no manifest or it cannot be read.</summary>
    public GroupsManifest? Load(string root, string outputFolderName)
    {
        var path = ResolvePath(root, outputFolderName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GroupsManifest>(File.ReadAllText(path), Options);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
