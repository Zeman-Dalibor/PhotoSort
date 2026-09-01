using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PhotoSort.Services;
using PhotoSort.ViewModels;
using PhotoSort.Views;

namespace PhotoSort;

public sealed class App : Application
{
    private ImageProvider? _images;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();

            _images = new ImageProvider(new SequentialImageLoader(new SkiaImageDecoder(new TiffPreviewExtractor())));

            window.DataContext = new MainWindowViewModel(
                new PhotoScanner(),
                new PhotoLibrary(new PhotoFileMover()),
                _images,
                new StorageProviderFolderPicker(window));

            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => _images.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
