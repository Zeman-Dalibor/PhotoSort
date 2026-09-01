namespace PhotoSort.Services;

/// <summary>Single source of truth for which file extensions the application handles.</summary>
public static class SupportedFormats
{
    /// <summary>Formats SkiaSharp decodes directly, ordered by display preference.</summary>
    public static readonly IReadOnlyList<string> Raster =
    [
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff", ".gif"
    ];

    /// <summary>RAW formats served through their embedded JPEG preview.</summary>
    public static readonly IReadOnlyList<string> Raw =
    [
        ".cr2", ".nef", ".arw", ".dng", ".orf", ".rw2", ".pef", ".raf", ".srw"
    ];

    private static readonly HashSet<string> RasterSet = new(Raster, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> RawSet = new(Raw, StringComparer.OrdinalIgnoreCase);

    public static bool IsRaster(string extension) => RasterSet.Contains(extension);

    public static bool IsRaw(string extension) => RawSet.Contains(extension);

    public static bool IsSupported(string extension) => IsRaster(extension) || IsRaw(extension);

    /// <summary>
    /// Lower rank wins when picking which variant of a group to display first.
    /// Raster formats come before RAW because they decode without preview extraction.
    /// </summary>
    public static int DisplayRank(string extension)
    {
        for (var i = 0; i < Raster.Count; i++)
        {
            if (string.Equals(Raster[i], extension, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        for (var i = 0; i < Raw.Count; i++)
        {
            if (string.Equals(Raw[i], extension, StringComparison.OrdinalIgnoreCase))
            {
                return 100 + i;
            }
        }

        return int.MaxValue;
    }
}
