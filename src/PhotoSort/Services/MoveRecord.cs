using PhotoSort.Models;

namespace PhotoSort.Services;

/// <summary>Everything needed to undo one categorisation.</summary>
/// <param name="Item">The affected photo.</param>
/// <param name="PreviousCategory">Category the item had before the move.</param>
/// <param name="NewCategory">Category the item has after the move.</param>
/// <param name="Paths">Old and new absolute path of every variant, in variant order.</param>
public sealed record MoveRecord(
    PhotoItem Item,
    PhotoCategory PreviousCategory,
    PhotoCategory NewCategory,
    IReadOnlyList<PathChange> Paths);

public sealed record PathChange(string OldPath, string NewPath);
