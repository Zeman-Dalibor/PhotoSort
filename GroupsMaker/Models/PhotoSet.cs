namespace GroupsMaker.Models;

/// <summary>
/// One photograph: every file in a folder that shares a name, for example
/// <c>IMG_0042.JPG</c> + <c>IMG_0042.CR2</c>. Grouping and moving always treat the set as a unit,
/// exactly like <c>PhotoItem</c> in the desktop app.
/// </summary>
/// <param name="Directory">Folder the files currently live in.</param>
/// <param name="Name">File name without extension, in its original casing.</param>
/// <param name="Files">Paths ordered by decoding preference; raster formats come before RAW.</param>
public sealed record PhotoSet(string Directory, string Name, IReadOnlyList<string> Files)
{
    /// <summary>The file the fingerprint is computed from: the cheapest one to decode.</summary>
    public string PreviewFile => Files[0];
}
