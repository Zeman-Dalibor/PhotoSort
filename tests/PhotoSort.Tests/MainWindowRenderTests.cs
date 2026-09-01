using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using PhotoSort.Services;
using PhotoSort.ViewModels;
using PhotoSort.Views;
using SkiaSharp;

namespace PhotoSort.Tests;

/// <summary>Renders the real window so a broken XAML binding fails the build rather than the user.</summary>
public sealed class MainWindowRenderTests
{
    [AvaloniaFact]
    public async Task RendersTheViewerWithAPhotoOnScreen()
    {
        using var folder = new TempFolder();
        WritePhoto(folder, "IMG_1.jpg", SKColors.SteelBlue);
        WritePhoto(folder, "IMG_2.jpg", SKColors.IndianRed);

        var window = await ShowWindowAsync(folder);

        var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Assert.True(frame!.PixelSize.Width > 0);
    }

    [AvaloniaFact]
    public async Task KeyboardShortcutMovesThePhotoIntoTheDeleteFolder()
    {
        using var folder = new TempFolder();
        WritePhoto(folder, "IMG_1.jpg", SKColors.SteelBlue);
        WritePhoto(folder, "IMG_2.jpg", SKColors.IndianRed);

        var window = await ShowWindowAsync(folder);
        var vm = (MainWindowViewModel)window.DataContext!;

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        await PumpAsync();

        Assert.True(File.Exists(Path.Combine(folder.Path, "delete", "IMG_1.jpg")));
        Assert.Equal("IMG_2.jpg", vm.PhotoName);
    }

    [AvaloniaFact]
    public async Task ArrowKeysNavigateBetweenPhotos()
    {
        using var folder = new TempFolder();
        WritePhoto(folder, "IMG_1.jpg", SKColors.SteelBlue);
        WritePhoto(folder, "IMG_2.jpg", SKColors.IndianRed);

        var window = await ShowWindowAsync(folder);
        var vm = (MainWindowViewModel)window.DataContext!;

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        await PumpAsync();
        Assert.Equal("IMG_2.jpg", vm.PhotoName);

        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        await PumpAsync();
        Assert.Equal("IMG_1.jpg", vm.PhotoName);
    }

    private static async Task<MainWindow> ShowWindowAsync(TempFolder folder)
    {
        var images = new ImageProvider(new SequentialImageLoader(new SkiaImageDecoder(new TiffPreviewExtractor())));
        var vm = new MainWindowViewModel(
            new PhotoScanner(),
            new PhotoLibrary(new PhotoFileMover()),
            images,
            new StubFolderPicker(folder.Path));

        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();

        await vm.ChooseFolderCommand.ExecuteAsync(null);
        await PumpAsync();
        return window;
    }

    private static async Task PumpAsync()
    {
        await Task.Delay(150);
        Dispatcher.UIThread.RunJobs();
    }

    private static void WritePhoto(TempFolder folder, string name, SKColor color)
    {
        using var bitmap = new SKBitmap(1200, 800);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
        File.WriteAllBytes(Path.Combine(folder.Path, name), data.ToArray());
    }

    private sealed class StubFolderPicker(string path) : IFolderPicker
    {
        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(path);
    }
}
