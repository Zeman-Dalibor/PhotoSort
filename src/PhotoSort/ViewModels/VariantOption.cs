namespace PhotoSort.ViewModels;

/// <summary>One selectable file format of the current photo (for example JPG or CR2).</summary>
public sealed record VariantOption(int Index, string Label, bool IsSelected);
