using Avalonia;
using Avalonia.Headless;
using PhotoSort;

[assembly: AvaloniaTestApplication(typeof(PhotoSort.Tests.TestAppBuilder))]

namespace PhotoSort.Tests;

/// <summary>Boots Avalonia headlessly so tests can create real bitmaps.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
