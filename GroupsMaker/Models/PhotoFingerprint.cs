namespace GroupsMaker.Models;

/// <summary>
/// Everything the grouper needs about one photograph: when it was taken and what it looks like.
/// </summary>
/// <param name="Photo">The files this fingerprint describes.</param>
/// <param name="CapturedAt">EXIF capture time when available, otherwise the file write time.</param>
/// <param name="TimeFromExif">False when <paramref name="CapturedAt"/> is only the file timestamp.</param>
/// <param name="Hash">64-bit difference hash, or <c>null</c> when the image could not be decoded.</param>
/// <param name="Error">Why the hash is missing; <c>null</c> on success.</param>
public sealed record PhotoFingerprint(
    PhotoSet Photo,
    DateTime CapturedAt,
    bool TimeFromExif,
    ulong? Hash,
    string? Error);
