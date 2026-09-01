using PhotoSort.Services;

namespace PhotoSort.Tests;

public sealed class LruCacheTests
{
    [Fact]
    public void EvictsTheLeastRecentlyUsedEntryWhenFull()
    {
        var evicted = new List<string>();
        var cache = new LruCache<string, string>(2, evicted.Add);

        cache.Set("a", "1");
        cache.Set("b", "2");
        cache.Set("c", "3");

        Assert.Equal(["1"], evicted);
        Assert.False(cache.Contains("a"));
        Assert.True(cache.Contains("b"));
        Assert.True(cache.Contains("c"));
    }

    [Fact]
    public void ReadingAnEntryMakesItTheMostRecentlyUsed()
    {
        var evicted = new List<string>();
        var cache = new LruCache<string, string>(2, evicted.Add);

        cache.Set("a", "1");
        cache.Set("b", "2");
        cache.TryGet("a", out _);
        cache.Set("c", "3");

        Assert.Equal(["2"], evicted);
        Assert.True(cache.Contains("a"));
    }

    [Fact]
    public void KeepsTheLastTenEntriesAtTheConfiguredCapacity()
    {
        var cache = new LruCache<int, int>(ImageProvider.FullImageCapacity);

        for (var i = 0; i < 25; i++)
        {
            cache.Set(i, i);
        }

        Assert.Equal(10, cache.Count);
        Assert.True(cache.Contains(15));
        Assert.False(cache.Contains(14));
    }

    [Fact]
    public void RenameMovesAValueWithoutEvictingIt()
    {
        var evicted = new List<string>();
        var cache = new LruCache<string, string>(2, evicted.Add);
        cache.Set("old", "value");

        cache.Rename("old", "new");

        Assert.True(cache.TryGet("new", out var value));
        Assert.Equal("value", value);
        Assert.False(cache.Contains("old"));
        Assert.Empty(evicted);
    }

    [Fact]
    public void ClearReportsEveryRemovedValue()
    {
        var evicted = new List<string>();
        var cache = new LruCache<string, string>(4, evicted.Add);
        cache.Set("a", "1");
        cache.Set("b", "2");

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Equal(2, evicted.Count);
    }
}
