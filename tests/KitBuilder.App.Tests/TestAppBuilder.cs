using Avalonia;
using Avalonia.Headless;
using KitBuilder.App;
using KitBuilder.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

// Serialize the whole assembly: parallel collections race Avalonia's headless platform
// setup (same reason as PT Script Builder's App tests).
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace KitBuilder.App.Tests;

/// <summary>Boots the real App under Avalonia's headless platform for UI tests.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
