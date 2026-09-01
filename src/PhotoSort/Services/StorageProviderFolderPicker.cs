using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PhotoSort.Services;

/// <summary>Folder dialog backed by Avalonia's cross-platform storage provider.</summary>
public sealed class StorageProviderFolderPicker(TopLevel topLevel) : IFolderPicker
{
    public async Task<string?> PickFolderAsync()
    {
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the folder with photos",
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
