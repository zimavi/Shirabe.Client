using System;

using Avalonia;

using Shirabe.Client.Services;

using Velopack;

namespace Shirabe.Client.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        DebugLog.Attach(); // Debug builds: every thrown exception goes to the console
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure(() => new App { UpdateSource = new GitHubReleases() })
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}