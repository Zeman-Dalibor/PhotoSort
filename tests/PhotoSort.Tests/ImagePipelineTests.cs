using Avalonia.Headless.XUnit;
using PhotoSort.Models;
using PhotoSort.Services;
using SkiaSharp;

namespace PhotoSort.Tests;

/// <summary>
/// Exercises the real decode path: file on disk → SkiaSharp → Avalonia bitmap, plus the
/// single-threaded loader and the LRU cache in front of it.
/// </summary>
public sealed class ImagePipelineTests
{
    [AvaloniaFact]
    public async Task DecodesAJpegAndDownscalesItToTheRequestedEdge()
    {
        using var folder = new TempFolder();
        var path = WriteJpeg(folder, "photo.jpg", 1600, 1000);

        using var provider = CreateProvider();
        var image = await provider.GetAsync(path, ImageSize.Thumbnail, LoadPriority.Immediate);

        Assert.NotNull(image);
        Assert.True(image!.IsSuccess, image.Error);
        Assert.Equal(1600, image.SourceWidth);
        Assert.Equal(1000, image.SourceHeight);
        Assert.InRange(image.Bitmap!.PixelSize.Width, 1, ImageProvider.ThumbnailMaxEdge);
        Assert.True(image.Bitmap.PixelSize.Width > image.Bitmap.PixelSize.Height);
    }

    [AvaloniaFact]
    public async Task ServesTheSecondRequestFromCacheWithoutDecodingAgain()
    {
        using var folder = new TempFolder();
        var path = WriteJpeg(folder, "photo.jpg", 800, 600);

        using var provider = CreateProvider();
        var first = await provider.GetAsync(path, ImageSize.Full, LoadPriority.Immediate);
        var second = await provider.GetAsync(path, ImageSize.Full, LoadPriority.Immediate);

        Assert.Same(first, second);
        Assert.True(provider.IsCached(path, ImageSize.Full));
    }

    [AvaloniaFact]
    public async Task KeepsTheBitmapAliveWhenTheFileIsMovedToAnotherFolder()
    {
        using var folder = new TempFolder();
        var path = WriteJpeg(folder, "photo.jpg", 800, 600);
        var movedPath = Path.Combine(folder.Path, "edit", "photo.jpg");

        using var provider = CreateProvider();
        var original = await provider.GetAsync(path, ImageSize.Full, LoadPriority.Immediate);

        Directory.CreateDirectory(Path.GetDirectoryName(movedPath)!);
        File.Move(path, movedPath);
        provider.Remap(path, movedPath);

        Assert.True(provider.TryGetCached(movedPath, ImageSize.Full, out var cached));
        Assert.Same(original, cached);
    }

    [AvaloniaFact]
    public async Task ReadsTheEmbeddedPreviewOutOfARawFile()
    {
        using var folder = new TempFolder();
        var raw = Path.Combine(folder.Path, "shot.cr2");
        File.WriteAllBytes(raw, BuildRawWithPreview(1200, 800));

        using var provider = CreateProvider();
        var image = await provider.GetAsync(raw, ImageSize.Full, LoadPriority.Immediate);

        Assert.NotNull(image);
        Assert.True(image!.IsSuccess, image.Error);
        Assert.Equal(1200, image.SourceWidth);
    }

    [AvaloniaFact]
    public async Task ReportsAFailureInsteadOfThrowingOnAnUnreadableFile()
    {
        using var folder = new TempFolder();
        var broken = folder.CreateFile("broken.jpg", 128);

        using var provider = CreateProvider();
        var image = await provider.GetAsync(broken, ImageSize.Full, LoadPriority.Immediate);

        Assert.NotNull(image);
        Assert.False(image!.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(image.Error));
    }

    [AvaloniaFact]
    public async Task DecodesQueuedRequestsOneAtATime()
    {
        using var folder = new TempFolder();
        var paths = Enumerable.Range(0, 6)
            .Select(i => WriteJpeg(folder, $"p{i}.jpg", 900, 600))
            .ToArray();

        var decoder = new ConcurrencyTrackingDecoder();
        using var loader = new SequentialImageLoader(decoder);
        var tasks = paths
            .Select(p => loader.EnqueueAsync(p, p, ImageProvider.FullImageMaxEdge, LoadPriority.Prefetch))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error));
        Assert.Equal(1, decoder.MaxConcurrency);
        Assert.Equal(paths.Length, decoder.DecodeCount);
    }

    private static ImageProvider CreateProvider() =>
        new(new SequentialImageLoader(new SkiaImageDecoder(new TiffPreviewExtractor())));

    private static string WriteJpeg(TempFolder folder, string name, int width, int height)
    {
        var path = Path.Combine(folder.Path, name);
        File.WriteAllBytes(path, EncodeJpeg(width, height));
        return path;
    }

    private static byte[] EncodeJpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
            canvas.DrawCircle(width / 2f, height / 2f, height / 4f, new SKPaint { Color = SKColors.Orange });
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data.ToArray();
    }

    /// <summary>TIFF container whose IFD0 points at a JPEG strip, mirroring how Canon CR2 works.</summary>
    private static byte[] BuildRawWithPreview(int width, int height)
    {
        var jpeg = EncodeJpeg(width, height);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        const int ifdOffset = 8;
        var dataOffset = ifdOffset + 2 + 3 * 12 + 4;

        writer.Write((byte)0x49);
        writer.Write((byte)0x49);
        writer.Write((ushort)42);
        writer.Write((uint)ifdOffset);

        writer.Write((ushort)3);
        WriteEntry(writer, 0x0103, 3, 6);
        WriteEntry(writer, 0x0111, 4, (uint)dataOffset);
        WriteEntry(writer, 0x0117, 4, (uint)jpeg.Length);
        writer.Write(0u);

        writer.Write(jpeg);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteEntry(BinaryWriter writer, ushort tag, ushort type, uint value)
    {
        writer.Write(tag);
        writer.Write(type);
        writer.Write(1u);
        writer.Write(value);
    }

    /// <summary>Real decoder wrapped in bookkeeping that detects overlapping decodes.</summary>
    private sealed class ConcurrencyTrackingDecoder : IImageDecoder
    {
        private readonly SkiaImageDecoder _inner = new(new TiffPreviewExtractor());
        private int _running;
        private int _maxConcurrency;
        private int _decodeCount;

        public int MaxConcurrency => Volatile.Read(ref _maxConcurrency);

        public int DecodeCount => Volatile.Read(ref _decodeCount);

        public DecodedImage Decode(string path, int maxEdge)
        {
            var running = Interlocked.Increment(ref _running);
            RecordMaximum(running);
            Interlocked.Increment(ref _decodeCount);

            try
            {
                Thread.Sleep(5);
                return _inner.Decode(path, maxEdge);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }

        private void RecordMaximum(int value)
        {
            int current;
            while ((current = Volatile.Read(ref _maxConcurrency)) < value &&
                   Interlocked.CompareExchange(ref _maxConcurrency, value, current) != current)
            {
            }
        }
    }
}
