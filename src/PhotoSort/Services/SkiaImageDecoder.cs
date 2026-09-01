using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SkiaSharp;

namespace PhotoSort.Services;

/// <summary>
/// Decodes a file into an Avalonia bitmap that is no larger than the requested edge length.
/// Downscaling happens inside the codec, so full-resolution pixels never reach managed memory.
/// </summary>
public sealed class SkiaImageDecoder(TiffPreviewExtractor previewExtractor) : IImageDecoder
{
    private static readonly Vector Dpi = new(96, 96);

    public DecodedImage Decode(string path, int maxEdge)
    {
        try
        {
            using var data = OpenImageData(path, out var isRaw);
            if (data is null)
            {
                return DecodedImage.Failure("No decodable image data found in the file.");
            }

            using var codec = SKCodec.Create(data);
            if (codec is null)
            {
                return DecodedImage.Failure("Unsupported or corrupted image data.");
            }

            var sourceWidth = codec.Info.Width;
            var sourceHeight = codec.Info.Height;

            using var decoded = DecodeScaled(codec, maxEdge);
            if (decoded is null)
            {
                return DecodedImage.Failure("The image could not be decoded.");
            }

            var origin = ResolveOrigin(codec, path, isRaw);
            using var rotated = origin == SKEncodedOrigin.TopLeft ? null : ApplyOrigin(decoded, origin);

            if (IsQuarterTurn(origin))
            {
                (sourceWidth, sourceHeight) = (sourceHeight, sourceWidth);
            }

            return DecodedImage.Success(ToAvaloniaBitmap(rotated ?? decoded), sourceWidth, sourceHeight);
        }
        catch (Exception e)
        {
            return DecodedImage.Failure(e.Message);
        }
    }

    private SKData? OpenImageData(string path, out bool isRaw)
    {
        isRaw = SupportedFormats.IsRaw(Path.GetExtension(path));

        if (!isRaw)
        {
            return SKData.Create(path);
        }

        var preview = previewExtractor.TryExtract(path);
        return preview is null ? null : SKData.CreateCopy(preview);
    }

    private static SKBitmap? DecodeScaled(SKCodec codec, int maxEdge)
    {
        var longestEdge = Math.Max(codec.Info.Width, codec.Info.Height);
        var scale = longestEdge <= maxEdge ? 1f : (float)maxEdge / longestEdge;

        // Codecs only honour a few scales (JPEG: 1, 1/2, 1/4, 1/8), so the result may still be
        // larger than requested and needs a second, exact resize.
        var scaled = codec.GetScaledDimensions(scale);
        var info = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);

        if (codec.GetPixels(info, bitmap.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            return null;
        }

        return ResizeToFit(bitmap, maxEdge);
    }

    private static SKBitmap ResizeToFit(SKBitmap bitmap, int maxEdge)
    {
        var longestEdge = Math.Max(bitmap.Width, bitmap.Height);
        if (longestEdge <= maxEdge)
        {
            return bitmap;
        }

        var ratio = (float)maxEdge / longestEdge;
        var target = new SKImageInfo(
            Math.Max(1, (int)(bitmap.Width * ratio)),
            Math.Max(1, (int)(bitmap.Height * ratio)),
            bitmap.ColorType,
            bitmap.AlphaType);

        var resized = bitmap.Resize(target, SKFilterQuality.High);
        if (resized is null)
        {
            return bitmap;
        }

        bitmap.Dispose();
        return resized;
    }

    /// <summary>
    /// JPEG carries its orientation in the codec. RAW previews usually do not, so the EXIF
    /// orientation of the RAW container is used instead.
    /// </summary>
    private static SKEncodedOrigin ResolveOrigin(SKCodec codec, string path, bool isRaw)
    {
        if (codec.EncodedOrigin != SKEncodedOrigin.TopLeft || !isRaw)
        {
            return codec.EncodedOrigin;
        }

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            foreach (var directory in directories.OfType<ExifIfd0Directory>())
            {
                if (directory.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientation) &&
                    orientation is >= 1 and <= 8)
                {
                    return (SKEncodedOrigin)orientation;
                }
            }
        }
        catch (Exception e) when (e is ImageProcessingException or IOException)
        {
            // Orientation is a nice-to-have; showing the image unrotated beats failing.
        }

        return SKEncodedOrigin.TopLeft;
    }

    private static SKBitmap ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
        {
            return source;
        }

        var swapAxes = IsQuarterTurn(origin);
        var width = swapAxes ? source.Height : source.Width;
        var height = swapAxes ? source.Width : source.Height;

        var rotated = new SKBitmap(new SKImageInfo(width, height, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(rotated);

        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Scale(-1, 1, width / 2f, 0);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180, width / 2f, height / 2f);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Scale(1, -1, 0, height / 2f);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.Translate(width, 0);
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1, 0, source.Height / 2f);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(width, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(0, height);
                canvas.RotateDegrees(-90);
                canvas.Scale(1, -1, 0, source.Height / 2f);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, height);
                canvas.RotateDegrees(-90);
                break;
        }

        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return rotated;
    }

    private static bool IsQuarterTurn(SKEncodedOrigin origin) => origin
        is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
        or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    private static unsafe Bitmap ToAvaloniaBitmap(SKBitmap source)
    {
        var size = new PixelSize(source.Width, source.Height);
        var target = new WriteableBitmap(size, Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);

        using (var locked = target.Lock())
        {
            var pixels = source.GetPixelSpan();
            var sourceStride = source.RowBytes;
            var bytesPerRow = Math.Min(sourceStride, locked.RowBytes);
            var destination = (byte*)locked.Address;

            for (var row = 0; row < size.Height; row++)
            {
                var line = pixels.Slice(row * sourceStride, bytesPerRow);
                line.CopyTo(new Span<byte>(destination + row * locked.RowBytes, bytesPerRow));
            }
        }

        return target;
    }
}
