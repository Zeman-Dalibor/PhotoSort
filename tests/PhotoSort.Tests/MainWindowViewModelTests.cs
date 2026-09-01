using Avalonia.Headless.XUnit;
using PhotoSort.Models;
using PhotoSort.Services;
using PhotoSort.ViewModels;
using SkiaSharp;

namespace PhotoSort.Tests;

public sealed class MainWindowViewModelTests
{
    [AvaloniaFact]
    public async Task ShowsTheFirstPhotoAfterChoosingAFolder()
    {
        using var folder = new TempFolder();
        WriteJpeg(folder, "IMG_1.jpg");
        WriteJpeg(folder, "IMG_2.jpg");

        var vm = CreateViewModel(folder, out _);
        await vm.ChooseFolderCommand.ExecuteAsync(null);

        Assert.True(vm.HasPhotos);
        Assert.Equal("IMG_1.jpg", vm.PhotoName);
        Assert.Equal("1 / 2", vm.PositionText);
        Assert.NotNull(vm.CurrentImage);
    }

    [AvaloniaFact]
    public async Task CategorisingMovesTheFilesAndAdvancesToTheNextPhoto()
    {
        using var folder = new TempFolder();
        WriteJpeg(folder, "IMG_1.jpg");
        WriteJpeg(folder, "IMG_1.cr2");
        WriteJpeg(folder, "IMG_2.jpg");

        var vm = CreateViewModel(folder, out _);
        await vm.ChooseFolderCommand.ExecuteAsync(null);
        await vm.CategoriseCommand.ExecuteAsync(PhotoCategory.Archive);

        Assert.True(File.Exists(Path.Combine(folder.Path, "archive", "IMG_1.jpg")));
        Assert.True(File.Exists(Path.Combine(folder.Path, "archive", "IMG_1.cr2")));
        Assert.Equal("IMG_2.jpg", vm.PhotoName);
        Assert.True(vm.CanUndo);
    }

    [AvaloniaFact]
    public async Task TheBadgeReflectsTheCategoryOfThePhotoOnScreen()
    {
        using var folder = new TempFolder();
        WriteJpeg(folder, "IMG_1.jpg");

        var vm = CreateViewModel(folder, out _);
        await vm.ChooseFolderCommand.ExecuteAsync(null);

        Assert.False(vm.HasCategory);

        await vm.CategoriseCommand.ExecuteAsync(PhotoCategory.Delete);

        Assert.True(vm.HasCategory);
        Assert.Equal("DELETE", vm.CategoryLabel);
    }

    [AvaloniaFact]
    public async Task UndoPutsTheFilesBackAndReselectsThePhoto()
    {
        using var folder = new TempFolder();
        var original = WriteJpeg(folder, "IMG_1.jpg");
        WriteJpeg(folder, "IMG_2.jpg");

        var vm = CreateViewModel(folder, out _);
        await vm.ChooseFolderCommand.ExecuteAsync(null);
        await vm.CategoriseCommand.ExecuteAsync(PhotoCategory.Edit);
        await vm.UndoCommand.ExecuteAsync(null);

        Assert.True(File.Exists(original));
        Assert.Equal("IMG_1.jpg", vm.PhotoName);
        Assert.False(vm.HasCategory);
        Assert.False(vm.CanUndo);
    }

    [AvaloniaFact]
    public async Task OffersTheOtherFormatOfAPairAndSwitchesToIt()
    {
        using var folder = new TempFolder();
        WriteJpeg(folder, "IMG_1.jpg");
        WriteRaw(folder, "IMG_1.cr2");

        var vm = CreateViewModel(folder, out _);
        await vm.ChooseFolderCommand.ExecuteAsync(null);

        Assert.True(vm.HasMultipleVariants);
        Assert.Equal(["JPG", "CR2"], vm.Variants.Select(v => v.Label));

        await vm.NextVariantCommand.ExecuteAsync(null);

        Assert.Equal("IMG_1.cr2", vm.PhotoName);
        Assert.NotNull(vm.CurrentImage);
    }

    [AvaloniaFact]
    public async Task PrefetchesTheNeighboursOfTheCurrentPhoto()
    {
        using var folder = new TempFolder();
        foreach (var i in Enumerable.Range(1, 5))
        {
            WriteJpeg(folder, $"IMG_{i}.jpg");
        }

        var vm = CreateViewModel(folder, out var images);
        await vm.ChooseFolderCommand.ExecuteAsync(null);
        vm.NextCommand.Execute(null);
        await WaitForPrefetch(images, Path.Combine(folder.Path, "IMG_4.jpg"));

        Assert.True(images.IsCached(Path.Combine(folder.Path, "IMG_1.jpg"), ImageSize.Full));
        Assert.True(images.IsCached(Path.Combine(folder.Path, "IMG_3.jpg"), ImageSize.Full));
        Assert.True(images.IsCached(Path.Combine(folder.Path, "IMG_4.jpg"), ImageSize.Full));
    }

    private static async Task WaitForPrefetch(ImageProvider images, string path)
    {
        for (var attempt = 0; attempt < 100 && !images.IsCached(path, ImageSize.Full); attempt++)
        {
            await Task.Delay(20);
        }
    }

    private static MainWindowViewModel CreateViewModel(TempFolder folder, out ImageProvider images)
    {
        images = new ImageProvider(new SequentialImageLoader(new SkiaImageDecoder(new TiffPreviewExtractor())));
        return new MainWindowViewModel(
            new PhotoScanner(),
            new PhotoLibrary(new PhotoFileMover()),
            images,
            new StubFolderPicker(folder.Path));
    }

    private static string WriteJpeg(TempFolder folder, string name)
    {
        var path = Path.Combine(folder.Path, name);
        using var bitmap = new SKBitmap(600, 400);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.DarkSlateBlue);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    /// <summary>A bare JPEG renamed to .cr2 still exercises the RAW branch through the SOI scan.</summary>
    private static string WriteRaw(TempFolder folder, string name)
    {
        var jpeg = WriteJpeg(folder, name + ".tmp");
        var path = Path.Combine(folder.Path, name);
        File.Move(jpeg, path);
        return path;
    }

    private sealed class StubFolderPicker(string path) : IFolderPicker
    {
        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(path);
    }
}
