namespace PhotoSort.Models;

/// <summary>Order in which queued disk reads are served. Lower value wins.</summary>
public enum LoadPriority
{
    /// <summary>The photo the user is looking at right now.</summary>
    Immediate = 0,

    /// <summary>Side thumbnails of the neighbouring photos.</summary>
    Thumbnail = 1,

    /// <summary>Full-size images around the current one, loaded speculatively.</summary>
    Prefetch = 2
}

/// <summary>Which rendition of a photo is being asked for.</summary>
public enum ImageSize
{
    Full,
    Thumbnail
}
