using PhotoSort.Services;

namespace PhotoSort.Models;

/// <summary>One physical file belonging to a photo (for example the JPG or the CR2 of a pair).</summary>
public sealed record PhotoVariant(string FullPath, long SizeBytes)
{
    public string Extension => Path.GetExtension(FullPath);

    public string FileName => Path.GetFileName(FullPath);

    /// <summary>Upper-case extension without the dot, used as the variant's UI label.</summary>
    public string Label => Extension.TrimStart('.').ToUpperInvariant();

    public bool IsRaw => SupportedFormats.IsRaw(Extension);

    public PhotoVariant WithPath(string newPath) => this with { FullPath = newPath };
}
