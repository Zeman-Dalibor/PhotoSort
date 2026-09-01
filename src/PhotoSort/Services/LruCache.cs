namespace PhotoSort.Services;

/// <summary>
/// Fixed-capacity least-recently-used cache. Thread-safe; evicted values are handed to
/// <paramref name="onEvicted"/> so bitmaps can be disposed.
/// </summary>
public sealed class LruCache<TKey, TValue>(int capacity, Action<TValue>? onEvicted = null)
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map = new(capacity);
    private readonly LinkedList<Entry> _order = new();
    private readonly Lock _gate = new();

    public int Capacity { get; } = capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity));

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = default!;
        return false;
    }

    public bool Contains(TKey key)
    {
        lock (_gate)
        {
            return _map.ContainsKey(key);
        }
    }

    public void Set(TKey key, TValue value)
    {
        var discarded = new List<TValue>(2);

        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                if (!Equals(existing.Value.Value, value))
                {
                    discarded.Add(existing.Value.Value);
                }

                _order.Remove(existing);
                _map.Remove(key);
            }

            _map[key] = _order.AddFirst(new Entry(key, value));

            while (_map.Count > Capacity)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
                discarded.Add(last.Value.Value);
            }
        }

        if (onEvicted is null)
        {
            return;
        }

        foreach (var item in discarded)
        {
            onEvicted(item);
        }
    }

    /// <summary>
    /// Moves a cached value to a new key without reloading it. Used after a file move so the
    /// already decoded bitmap survives the path change.
    /// </summary>
    public void Rename(TKey oldKey, TKey newKey)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue(oldKey, out var node) || _map.ContainsKey(newKey))
            {
                return;
            }

            _map.Remove(oldKey);
            node.Value = node.Value with { Key = newKey };
            _map[newKey] = node;
        }
    }

    public void Clear()
    {
        List<TValue> values;

        lock (_gate)
        {
            values = _order.Select(e => e.Value).ToList();
            _order.Clear();
            _map.Clear();
        }

        if (onEvicted is null)
        {
            return;
        }

        foreach (var value in values)
        {
            onEvicted(value);
        }
    }

    private sealed record Entry(TKey Key, TValue Value);
}
