using Avalonia;
using Avalonia.Headless;
using PhotoSort;

[assembly: AvaloniaTestApplication(typeof(PhotoSort.Tests.TestAppBuilder))]

namespace PhotoSort.Tests;

/// <summary>Boots Avalonia headlessly so tests can create real bitmaps.</summary>
public static class TestAppBuilder
{
    // WithInterFont keeps the tests independent of whatever fonts the CI machine happens to have.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
