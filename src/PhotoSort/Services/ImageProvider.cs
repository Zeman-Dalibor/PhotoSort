using PhotoSort.Models;

namespace PhotoSort.Services;

/// <summary>
/// The single entry point the UI uses to obtain bitmaps: answers from the LRU cache when it can,
/// otherwise queues the work on the sequential loader and caches the result.
/// </summary>
public sealed class ImageProvider : IDisposable
{
    /// <summary>Keeps the last ten displayed photos in memory, as required.</summary>
    public const int FullImageCapacity = 10;

    public const int ThumbnailCapacity = 64;
    public const int FullImageMaxEdge = 2560;
    public const int ThumbnailMaxEdge = 240;

    private readonly SequentialImageLoader _loader;
    private readonly LruCache<string, DecodedImage> _fullImages;
    private readonly LruCache<string, DecodedImage> _thumbnails;

    public ImageProvider(SequentialImageLoader loader)
    {
        _loader = loader;
        _fullImages = new LruCache<string, DecodedImage>(FullImageCapacity, image => image.Dispose());
        _thumbnails = new LruCache<string, DecodedImage>(ThumbnailCapacity, image => image.Dispose());
    }

    public static string CacheKey(string path, ImageSize size) => $"{size}|{path}";

    public bool TryGetCached(string path, ImageSize size, out DecodedImage image) =>
        CacheFor(size).TryGet(CacheKey(path, size), out image);

    public bool IsCached(string path, ImageSize size) => CacheFor(size).Contains(CacheKey(path, size));

    /// <summary>Returns the image from cache, or queues a decode and caches the outcome.</summary>
    public async Task<DecodedImage?> GetAsync(string path, ImageSize size, LoadPriority priority)
    {
        var key = CacheKey(path, size);
        var cache = CacheFor(size);

        if (cache.TryGet(key, out var cached))
        {
            return cached;
        }

        try
        {
            var decoded = await _loader
                .EnqueueAsync(key, path, MaxEdgeFor(size), priority)
                .ConfigureAwait(true);

            // A parallel caller may have won the race; keep whichever is already cached.
            if (cache.TryGet(key, out var raced))
            {
                if (!ReferenceEquals(raced, decoded))
                {
                    decoded.Dispose();
                }

                return raced;
            }

            cache.Set(key, decoded);
            return decoded;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Fire-and-forget warm-up used for the ±2 prefetch window.</summary>
    public void Prefetch(string path, ImageSize size, LoadPriority priority)
    {
        if (IsCached(path, size))
        {
            return;
        }

        _ = GetAsync(path, size, priority);
    }

    /// <summary>Discards queued work that has fallen outside the prefetch window.</summary>
    public void DropPendingExcept(IReadOnlySet<string> keysToKeep) =>
        _loader.DropPending((key, priority) => priority == LoadPriority.Immediate || keysToKeep.Contains(key));

    /// <summary>Keeps decoded bitmaps alive across a file move by re-keying them to the new path.</summary>
    public void Remap(string oldPath, string newPath)
    {
        foreach (var size in new[] { ImageSize.Full, ImageSize.Thumbnail })
        {
            CacheFor(size).Rename(CacheKey(oldPath, size), CacheKey(newPath, size));
        }
    }

    public void Clear()
    {
        _fullImages.Clear();
        _thumbnails.Clear();
    }

    public void Dispose()
    {
        Clear();
        _loader.Dispose();
    }

    private LruCache<string, DecodedImage> CacheFor(ImageSize size) =>
        size == ImageSize.Full ? _fullImages : _thumbnails;

    private static int MaxEdgeFor(ImageSize size) =>
        size == ImageSize.Full ? FullImageMaxEdge : ThumbnailMaxEdge;
}
