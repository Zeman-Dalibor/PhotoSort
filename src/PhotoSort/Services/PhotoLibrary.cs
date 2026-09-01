using PhotoSort.Models;

namespace PhotoSort.Services;

/// <summary>
/// Owns the ordered list of photos, the cursor into it, and the categorise/undo operations.
/// Knows nothing about bitmaps or the UI; it reports path changes through <see cref="PhotoRelocated"/>.
/// </summary>
public sealed class PhotoLibrary(PhotoFileMover mover)
{
    private const int UndoDepth = 20;

    private readonly List<PhotoItem> _items = [];
    private readonly Stack<MoveRecord> _undo = new();

    /// <summary>Raised for every file whose path changed, so caches can be re-keyed.</summary>
    public event Action<string, string>? PhotoRelocated;

    public IReadOnlyList<PhotoItem> Items => _items;

    public string RootPath { get; private set; } = string.Empty;

    public int CurrentIndex { get; private set; } = -1;

    public PhotoItem? Current => ItemAt(CurrentIndex);

    public PhotoItem? Previous => ItemAt(CurrentIndex - 1);

    public PhotoItem? Next => ItemAt(CurrentIndex + 1);

    public bool CanUndo => _undo.Count > 0;

    public void Load(string rootPath, IReadOnlyList<PhotoItem> items)
    {
        RootPath = rootPath;
        _items.Clear();
        _items.AddRange(items);
        _undo.Clear();
        CurrentIndex = _items.Count > 0 ? 0 : -1;
    }

    public PhotoItem? ItemAt(int index) =>
        index >= 0 && index < _items.Count ? _items[index] : null;

    /// <summary>Moves the cursor and reports whether it actually changed.</summary>
    public bool MoveTo(int index)
    {
        if (_items.Count == 0)
        {
            return false;
        }

        var clamped = Math.Clamp(index, 0, _items.Count - 1);
        if (clamped == CurrentIndex)
        {
            return false;
        }

        CurrentIndex = clamped;
        return true;
    }

    public bool MoveNext() => MoveTo(CurrentIndex + 1);

    public bool MovePrevious() => MoveTo(CurrentIndex - 1);

    public bool MoveFirst() => MoveTo(0);

    public bool MoveLast() => MoveTo(_items.Count - 1);

    /// <summary>
    /// Moves the current photo's files into the folder for <paramref name="target"/>.
    /// Returns the record of the move, or <c>null</c> if the photo was already categorised there.
    /// </summary>
    public MoveRecord? Categorise(PhotoCategory target)
    {
        var item = Current;
        if (item is null)
        {
            return null;
        }

        var record = mover.Move(item, target, RootPath);
        if (record is null)
        {
            return null;
        }

        PushUndo(record);
        NotifyRelocations(record);
        return record;
    }

    /// <summary>Reverts the most recent categorisation and points the cursor at the restored photo.</summary>
    public MoveRecord? Undo()
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        var record = _undo.Pop();
        mover.Undo(record);

        foreach (var change in record.Paths)
        {
            PhotoRelocated?.Invoke(change.NewPath, change.OldPath);
        }

        var index = _items.IndexOf(record.Item);
        if (index >= 0)
        {
            CurrentIndex = index;
        }

        return record;
    }

    private void PushUndo(MoveRecord record)
    {
        _undo.Push(record);

        if (_undo.Count > UndoDepth)
        {
            var kept = _undo.Take(UndoDepth).Reverse().ToList();
            _undo.Clear();
            foreach (var entry in kept)
            {
                _undo.Push(entry);
            }
        }
    }

    private void NotifyRelocations(MoveRecord record)
    {
        foreach (var change in record.Paths)
        {
            PhotoRelocated?.Invoke(change.OldPath, change.NewPath);
        }
    }
}
