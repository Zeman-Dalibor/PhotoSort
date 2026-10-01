using GroupsMaker.Models;

namespace GroupsMaker.Services;

/// <summary>
/// Clusters fingerprints into bursts and duplicates.
///
/// Two photographs end up in the same group when either rule fires:
/// <list type="bullet">
/// <item>burst — taken within <paramref name="timeGap"/> of each other and visually close;</item>
/// <item>duplicate — visually near-identical, no matter how far apart they were taken.</item>
/// </list>
/// The rules are transitive (union-find), so a continuous burst stays one group even when its
/// first and last frame no longer resemble each other.
/// </summary>
public sealed class BurstGrouper(TimeSpan timeGap, int burstDistance, int duplicateDistance, int minGroupSize)
{
    public IReadOnlyList<PhotoGroup> Group(IReadOnlyList<PhotoFingerprint> fingerprints)
    {
        var hashed = fingerprints
            .Where(f => f.Hash.HasValue)
            .OrderBy(f => f.CapturedAt)
            .ToArray();

        var union = new DisjointSet(hashed.Length);
        LinkBursts(hashed, union);
        LinkDuplicates(hashed, union);

        return union
            .Components()
            .Select(indices => new PhotoGroup([.. indices.Select(i => hashed[i]).OrderBy(f => f.CapturedAt)]))
            .Where(group => group.Count >= minGroupSize)
            .OrderBy(group => group.First.CapturedAt)
            .ToList();
    }

    /// <summary>
    /// Walks the time-sorted list and only compares inside the time window, so the cost stays
    /// close to linear even for a folder holding thousands of shots.
    /// </summary>
    private void LinkBursts(PhotoFingerprint[] hashed, DisjointSet union)
    {
        for (var i = 0; i < hashed.Length; i++)
        {
            for (var j = i + 1; j < hashed.Length; j++)
            {
                if (hashed[j].CapturedAt - hashed[i].CapturedAt > timeGap)
                {
                    break;
                }

                if (Distance(hashed[i], hashed[j]) <= burstDistance)
                {
                    union.Merge(i, j);
                }
            }
        }
    }

    /// <summary>
    /// Catches copies that time cannot connect: the same picture imported twice, or re-saved
    /// months later. Quadratic, but a 64-bit comparison is cheap enough for tens of thousands.
    /// </summary>
    private void LinkDuplicates(PhotoFingerprint[] hashed, DisjointSet union)
    {
        if (duplicateDistance < 0)
        {
            return;
        }

        for (var i = 0; i < hashed.Length; i++)
        {
            for (var j = i + 1; j < hashed.Length; j++)
            {
                if (Distance(hashed[i], hashed[j]) <= duplicateDistance)
                {
                    union.Merge(i, j);
                }
            }
        }
    }

    private static int Distance(PhotoFingerprint left, PhotoFingerprint right) =>
        DifferenceHash.Distance(left.Hash!.Value, right.Hash!.Value);

    private sealed class DisjointSet
    {
        private readonly int[] parents;

        public DisjointSet(int count)
        {
            parents = new int[count];
            for (var i = 0; i < count; i++)
            {
                parents[i] = i;
            }
        }

        public void Merge(int left, int right)
        {
            var a = Find(left);
            var b = Find(right);
            if (a != b)
            {
                parents[Math.Max(a, b)] = Math.Min(a, b);
            }
        }

        public IEnumerable<List<int>> Components()
        {
            var buckets = new Dictionary<int, List<int>>();

            for (var i = 0; i < parents.Length; i++)
            {
                var root = Find(i);
                if (!buckets.TryGetValue(root, out var bucket))
                {
                    bucket = [];
                    buckets[root] = bucket;
                }

                bucket.Add(i);
            }

            return buckets.Values;
        }

        private int Find(int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }

            return index;
        }
    }
}
