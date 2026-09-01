using PhotoSort.Services;

namespace PhotoSort.Models;

/// <summary>
/// A single photo as the user perceives it. Files that share a directory and a base name
/// (IMG_0042.JPG + IMG_0042.CR2) form one item and are always moved together.
/// </summary>
public sealed class PhotoItem
{
    private readonly List<PhotoVariant> _variants;

    public PhotoItem(string displayName, PhotoCategory category, IEnumerable<PhotoVariant> variants)
    {
        DisplayName = displayName;
        Category = category;
        _variants = variants.OrderBy(v => SupportedFormats.DisplayRank(v.Extension)).ToList();

        if (_variants.Count == 0)
        {
            throw new ArgumentException("A photo item needs at least one file.", nameof(variants));
        }
    }

    public string DisplayName { get; }

    public PhotoCategory Category { get; private set; }

    public IReadOnlyList<PhotoVariant> Variants => _variants;

    public int SelectedVariantIndex { get; private set; }

    public PhotoVariant SelectedVariant => _variants[SelectedVariantIndex];

    public bool HasMultipleVariants => _variants.Count > 1;

    public long TotalSizeBytes => _variants.Sum(v => v.SizeBytes);

    /// <summary>Directory all variants live in; they are kept together by construction.</summary>
    public string Directory => Path.GetDirectoryName(_variants[0].FullPath) ?? string.Empty;

    public void SelectVariant(int index)
    {
        if (index < 0 || index >= _variants.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        SelectedVariantIndex = index;
    }

    public void SelectNextVariant() => SelectedVariantIndex = (SelectedVariantIndex + 1) % _variants.Count;

    /// <summary>Applies the outcome of a completed file move to the in-memory model.</summary>
    public void ApplyRelocation(PhotoCategory category, IReadOnlyList<string> newPaths)
    {
        if (newPaths.Count != _variants.Count)
        {
            throw new ArgumentException("Every variant needs a new path.", nameof(newPaths));
        }

        for (var i = 0; i < _variants.Count; i++)
        {
            _variants[i] = _variants[i].WithPath(newPaths[i]);
        }

        Category = category;
    }
}
