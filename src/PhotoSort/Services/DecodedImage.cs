using Avalonia.Media.Imaging;

namespace PhotoSort.Services;

/// <summary>Outcome of one decode attempt: either a bitmap or the reason it failed.</summary>
public sealed class DecodedImage : IDisposable
{
    private DecodedImage(Bitmap? bitmap, int sourceWidth, int sourceHeight, string? error)
    {
        Bitmap = bitmap;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        Error = error;
    }

    public Bitmap? Bitmap { get; }

    /// <summary>Pixel size of the original file, not of the downscaled bitmap.</summary>
    public int SourceWidth { get; }

    public int SourceHeight { get; }

    public string? Error { get; }

    public bool IsSuccess => Bitmap is not null;

    public static DecodedImage Success(Bitmap bitmap, int sourceWidth, int sourceHeight) =>
        new(bitmap, sourceWidth, sourceHeight, null);

    public static DecodedImage Failure(string error) => new(null, 0, 0, error);

    public void Dispose() => Bitmap?.Dispose();
}
