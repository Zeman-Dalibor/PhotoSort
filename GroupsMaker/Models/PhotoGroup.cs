namespace GroupsMaker.Models;

/// <summary>A burst or a set of duplicates: photographs the user should pick one winner from.</summary>
/// <param name="Members">Ordered by capture time; never empty.</param>
public sealed record PhotoGroup(IReadOnlyList<PhotoFingerprint> Members)
{
    public PhotoFingerprint First => Members[0];

    public PhotoFingerprint Last => Members[^1];

    public int Count => Members.Count;

    /// <summary>How far apart the first and last shot of the group were taken.</summary>
    public TimeSpan Span => Last.CapturedAt - First.CapturedAt;
}
