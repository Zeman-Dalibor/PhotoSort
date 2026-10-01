using PhotoSort.Services;
using SkiaSharp;

namespace GroupsMaker.Services;

/// <summary>
/// Decodes any supported file into a small bitmap. RAW files go through their embedded JPEG
/// preview, and the codec does the downscaling, so full-resolution pixels never reach memory.
/// </summary>
public sealed class ThumbnailDecoder(TiffPreviewExtractor previewExtractor)
{
    /// <summary>Returns <c>null</c> when the file holds nothing this decoder can read.</summary>
    public SKBitmap? TryDecode(string path, int maxEdge)
    {
        using var data = OpenData(path);
        if (data is null)
        {
            return null;
        }

        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return null;
        }

        var longestEdge = Math.Max(codec.Info.Width, codec.Info.Height);
        var scale = longestEdge <= maxEdge ? 1f : (float)maxEdge / longestEdge;

        // Codecs only honour a few scales (JPEG: 1, 1/2, 1/4, 1/8), so the result is usually
        // larger than asked for. The hash resizes it down to 9x8 anyway.
        var scaled = codec.GetScaledDimensions(scale);
        var info = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);

        if (codec.GetPixels(info, bitmap.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            return null;
        }

        return bitmap;
    }

    private SKData? OpenData(string path)
    {
        if (!SupportedFormats.IsRaw(Path.GetExtension(path)))
        {
            return SKData.Create(path);
        }

        var preview = previewExtractor.TryExtract(path);
        return preview is null ? null : SKData.CreateCopy(preview);
    }
}
