namespace PhotoSort.Services;

/// <summary>Abstracts the platform folder dialog so the view model stays testable.</summary>
public interface IFolderPicker
{
    /// <summary>Returns the chosen folder, or <c>null</c> when the user cancels.</summary>
    Task<string?> PickFolderAsync();
}
