using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoSort.Models;
using PhotoSort.Services;

namespace PhotoSort.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly PhotoScanner _scanner;
    private readonly PhotoLibrary _library;
    private readonly ImageProvider _images;
    private readonly IFolderPicker _folderPicker;

    private int _displayGeneration;

    [ObservableProperty] private string _rootPath = string.Empty;
    [ObservableProperty] private bool _includeFilterFolders;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isDecoding;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty] private Bitmap? _currentImage;
    [ObservableProperty] private Bitmap? _previousThumbnail;
    [ObservableProperty] private Bitmap? _nextThumbnail;

    [ObservableProperty] private string _photoName = string.Empty;
    [ObservableProperty] private string _positionText = string.Empty;
    [ObservableProperty] private string _dimensionsText = string.Empty;
    [ObservableProperty] private string _fileSizeText = string.Empty;
    [ObservableProperty] private PhotoCategory _currentCategory = PhotoCategory.None;

    public MainWindowViewModel(
        PhotoScanner scanner,
        PhotoLibrary library,
        ImageProvider images,
        IFolderPicker folderPicker)
    {
        _scanner = scanner;
        _library = library;
        _images = images;
        _folderPicker = folderPicker;

        _library.PhotoRelocated += _images.Remap;
        StatusMessage = "Choose a folder with photos to start.";
    }

    public ObservableCollection<VariantOption> Variants { get; } = [];

    public bool HasPhotos => _library.Items.Count > 0;

    public bool HasFolder => !string.IsNullOrEmpty(RootPath);

    public bool CanUndo => _library.CanUndo;

    public bool HasCategory => CurrentCategory != PhotoCategory.None;

    public string CategoryLabel => CurrentCategory.ToString().ToUpperInvariant();

    public bool HasMultipleVariants => Variants.Count > 1;

    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        var folder = await _folderPicker.PickFolderAsync();
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        RootPath = folder;
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (!HasFolder)
        {
            return;
        }

        IsScanning = true;
        ErrorMessage = null;
        StatusMessage = "Scanning folder…";
        _images.Clear();

        try
        {
            var root = RootPath;
            var includeFiltered = IncludeFilterFolders;
            var items = await Task.Run(() => _scanner.Scan(root, includeFiltered));

            _library.Load(root, items);
            OnPropertyChanged(nameof(HasPhotos));
            OnPropertyChanged(nameof(HasFolder));

            StatusMessage = items.Count == 0
                ? "No supported photos found in this folder."
                : $"Loaded {items.Count} photos.";

            await ShowCurrentAsync();
        }
        catch (Exception e)
        {
            ErrorMessage = e.Message;
            StatusMessage = "Scanning failed.";
        }
        finally
        {
            IsScanning = false;
        }
    }

    // Navigation is deliberately synchronous: the cursor and the caption move on the key press,
    // and the decode catches up in the background. An async command would swallow key repeats
    // while the previous decode is still running.
    [RelayCommand]
    private void Next() => Navigate(_library.MoveNext());

    [RelayCommand]
    private void Previous() => Navigate(_library.MovePrevious());

    [RelayCommand]
    private void First() => Navigate(_library.MoveFirst());

    [RelayCommand]
    private void Last() => Navigate(_library.MoveLast());

    [RelayCommand]
    private void GoTo(int index) => Navigate(_library.MoveTo(index));

    private void Navigate(bool moved)
    {
        if (moved)
        {
            _ = ShowCurrentAsync();
        }
    }

    /// <summary>Serialised on purpose: a double key press must not move the same photo twice.</summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task CategoriseAsync(PhotoCategory category) => ApplyCategoryAsync(category);

    [RelayCommand]
    private async Task UndoAsync()
    {
        try
        {
            if (_library.Undo() is null)
            {
                return;
            }

            ErrorMessage = null;
            StatusMessage = "Reverted the last move.";
            await ShowCurrentAsync();
        }
        catch (Exception e)
        {
            ErrorMessage = $"Undo failed: {e.Message}";
        }
        finally
        {
            OnPropertyChanged(nameof(CanUndo));
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task NextVariantAsync()
    {
        var item = _library.Current;
        if (item is null || !item.HasMultipleVariants)
        {
            return;
        }

        item.SelectNextVariant();
        await ShowCurrentAsync();
    }

    [RelayCommand]
    private async Task SelectVariantAsync(int index)
    {
        var item = _library.Current;
        if (item is null || index < 0 || index >= item.Variants.Count || index == item.SelectedVariantIndex)
        {
            return;
        }

        item.SelectVariant(index);
        await ShowCurrentAsync();
    }

    private async Task ApplyCategoryAsync(PhotoCategory category)
    {
        var item = _library.Current;
        if (item is null)
        {
            return;
        }

        try
        {
            var record = _library.Categorise(category);
            if (record is not null)
            {
                ErrorMessage = null;
                StatusMessage = $"{item.DisplayName} → {DescribeTarget(category)}";
            }
        }
        catch (Exception e)
        {
            ErrorMessage = $"Could not move {item.DisplayName}: {e.Message}";
            return;
        }
        finally
        {
            OnPropertyChanged(nameof(CanUndo));
        }

        _library.MoveNext();
        await ShowCurrentAsync();
    }

    private static string DescribeTarget(PhotoCategory category) =>
        CategoryFolders.FolderName(category) ?? "root folder";

    /// <summary>
    /// Renders the photo under the cursor, then schedules the surrounding prefetch window.
    /// A generation counter makes sure a slow decode cannot overwrite a newer one.
    /// </summary>
    private async Task ShowCurrentAsync()
    {
        var generation = Interlocked.Increment(ref _displayGeneration);
        var item = _library.Current;

        UpdateMetadata(item);

        if (item is null)
        {
            CurrentImage = null;
            PreviousThumbnail = null;
            NextThumbnail = null;
            return;
        }

        var path = item.SelectedVariant.FullPath;

        if (_images.TryGetCached(path, ImageSize.Full, out var cached))
        {
            ApplyMainImage(cached, generation);
        }
        else
        {
            IsDecoding = true;
            try
            {
                var decoded = await _images.GetAsync(path, ImageSize.Full, LoadPriority.Immediate);

                if (generation != Volatile.Read(ref _displayGeneration))
                {
                    return;
                }

                if (decoded is not null)
                {
                    ApplyMainImage(decoded, generation);
                }
            }
            finally
            {
                IsDecoding = false;
            }
        }

        await UpdateThumbnailsAsync(generation);
        SchedulePrefetch();
    }

    private void ApplyMainImage(DecodedImage decoded, int generation)
    {
        if (generation != Volatile.Read(ref _displayGeneration))
        {
            return;
        }

        CurrentImage = decoded.Bitmap;
        ErrorMessage = decoded.Error;

        if (decoded.IsSuccess)
        {
            DimensionsText = $"{decoded.SourceWidth} × {decoded.SourceHeight}";
        }
    }

    private async Task UpdateThumbnailsAsync(int generation)
    {
        PreviousThumbnail = await LoadThumbnailAsync(_library.Previous, generation);
        NextThumbnail = await LoadThumbnailAsync(_library.Next, generation);
    }

    private async Task<Bitmap?> LoadThumbnailAsync(PhotoItem? item, int generation)
    {
        if (item is null)
        {
            return null;
        }

        var decoded = await _images.GetAsync(item.SelectedVariant.FullPath, ImageSize.Thumbnail, LoadPriority.Thumbnail);
        return generation == Volatile.Read(ref _displayGeneration) ? decoded?.Bitmap : null;
    }

    /// <summary>Warms the cache for ±1 and ±2 and drops queued work outside that window.</summary>
    private void SchedulePrefetch()
    {
        var index = _library.CurrentIndex;
        var window = new List<(PhotoItem Item, ImageSize Size)>();

        foreach (var offset in new[] { 1, -1, 2, -2 })
        {
            if (_library.ItemAt(index + offset) is { } neighbour)
            {
                window.Add((neighbour, ImageSize.Full));

                if (Math.Abs(offset) == 1)
                {
                    window.Add((neighbour, ImageSize.Thumbnail));
                }
            }
        }

        var keys = window
            .Select(entry => ImageProvider.CacheKey(entry.Item.SelectedVariant.FullPath, entry.Size))
            .ToHashSet(StringComparer.Ordinal);

        _images.DropPendingExcept(keys);

        foreach (var (neighbour, size) in window)
        {
            var priority = size == ImageSize.Thumbnail ? LoadPriority.Thumbnail : LoadPriority.Prefetch;
            _images.Prefetch(neighbour.SelectedVariant.FullPath, size, priority);
        }
    }

    private void UpdateMetadata(PhotoItem? item)
    {
        UpdateVariants(item);

        if (item is null)
        {
            PhotoName = string.Empty;
            PositionText = string.Empty;
            DimensionsText = string.Empty;
            FileSizeText = string.Empty;
            CurrentCategory = PhotoCategory.None;
            return;
        }

        PhotoName = item.SelectedVariant.FileName;
        PositionText = $"{_library.CurrentIndex + 1} / {_library.Items.Count}";
        FileSizeText = FormatSize(item.TotalSizeBytes);
        CurrentCategory = item.Category;
        DimensionsText = string.Empty;
    }

    private void UpdateVariants(PhotoItem? item)
    {
        Variants.Clear();

        if (item is null || !item.HasMultipleVariants)
        {
            return;
        }

        for (var i = 0; i < item.Variants.Count; i++)
        {
            Variants.Add(new VariantOption(i, item.Variants[i].Label, i == item.SelectedVariantIndex));
        }

        OnPropertyChanged(nameof(HasMultipleVariants));
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "kB", "MB", "GB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.CurrentCulture, $"{value:0.#} {units[unit]}");
    }

    partial void OnIncludeFilterFoldersChanged(bool value)
    {
        if (HasFolder)
        {
            _ = ReloadAsync();
        }
    }

    partial void OnRootPathChanged(string value) => OnPropertyChanged(nameof(HasFolder));

    partial void OnCurrentCategoryChanged(PhotoCategory value)
    {
        OnPropertyChanged(nameof(HasCategory));
        OnPropertyChanged(nameof(CategoryLabel));
    }
}
