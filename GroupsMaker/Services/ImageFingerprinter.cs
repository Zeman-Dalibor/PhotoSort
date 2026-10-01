using System.Globalization;
using GroupsMaker.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

// MetadataExtractor has its own Directory type; this file only ever means the file system one.
using Directory = System.IO.Directory;

namespace GroupsMaker.Services;

/// <summary>Reads capture time and visual hash for one photograph.</summary>
public sealed class ImageFingerprinter(ThumbnailDecoder decoder)
{
    /// <summary>
    /// Pixels fed into the hash. Small enough to decode thousands of files quickly, large enough
    /// that the mipmap reduction to 9x8 still averages real detail.
    /// </summary>
    private const int HashSourceEdge = 128;

    public PhotoFingerprint Create(PhotoSet photo)
    {
        var (capturedAt, fromExif) = ReadCaptureTime(photo);

        try
        {
            using var bitmap = decoder.TryDecode(photo.PreviewFile, HashSourceEdge);
            if (bitmap is null)
            {
                return new PhotoFingerprint(photo, capturedAt, fromExif, null, "No decodable image data.");
            }

            var hash = DifferenceHash.TryCompute(bitmap);
            return new PhotoFingerprint(photo, capturedAt, fromExif, hash,
                hash is null ? "The image could not be resized for hashing." : null);
        }
        catch (Exception e)
        {
            return new PhotoFingerprint(photo, capturedAt, fromExif, null, e.Message);
        }
    }

    /// <summary>
    /// Prefers EXIF <c>DateTimeOriginal</c> including sub-second precision, because a 10 fps burst
    /// needs more resolution than whole seconds. Falls back to the file write time.
    /// </summary>
    private static (DateTime CapturedAt, bool FromExif) ReadCaptureTime(PhotoSet photo)
    {
        foreach (var file in photo.Files)
        {
            var exif = TryReadExifTime(file);
            if (exif is not null)
            {
                return (exif.Value, true);
            }
        }

        try
        {
            return (File.GetLastWriteTime(photo.PreviewFile), false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (DateTime.MinValue, false);
        }
    }

    private static DateTime? TryReadExifTime(string path)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);

            foreach (var subIfd in directories.OfType<ExifSubIfdDirectory>())
            {
                if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var original))
                {
                    return original.AddTicks(ReadSubsecondTicks(subIfd, ExifDirectoryBase.TagSubsecondTimeOriginal));
                }
            }

            foreach (var ifd0 in directories.OfType<ExifIfd0Directory>())
            {
                if (ifd0.TryGetDateTime(ExifDirectoryBase.TagDateTime, out var modified))
                {
                    return modified;
                }
            }
        }
        catch (Exception e) when (e is ImageProcessingException or IOException or UnauthorizedAccessException)
        {
            // Timestamps are a hint, not a requirement; the file time takes over.
        }

        return null;
    }

    /// <summary>EXIF stores the fraction as digits after the decimal point, so "25" means 0.25 s.</summary>
    private static long ReadSubsecondTicks(ExifSubIfdDirectory directory, int tag)
    {
        var text = directory.GetString(tag)?.Trim();
        if (string.IsNullOrEmpty(text) || !text.All(char.IsAsciiDigit))
        {
            return 0;
        }

        var digits = text.Length > 7 ? text[..7] : text;
        var value = long.Parse(digits, CultureInfo.InvariantCulture);
        return (long)(value / Math.Pow(10, digits.Length) * TimeSpan.TicksPerSecond);
    }
}
