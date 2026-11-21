using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Shirabe.Audio;
using Shirabe.Client.ViewModels;
using Shirabe.Client.Views;
using Shirabe.Core.Playback;

namespace Shirabe.Client;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var mainViewModel = CreateMainViewModel(out var player);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
            desktop.Exit += (_, _) => player?.Dispose();

            // "Open with Shirabe" / `Shirabe.Client.Desktop.exe song.mp3`: play the first argument.
            if (desktop.Args is [var path, ..] && mainViewModel.Player is { } playerViewModel)
                Dispatcher.UIThread.Post(async () => await playerViewModel.OpenAsync(path));
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory =
                () => new MainView { DataContext = mainViewModel };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    // The app still opens without a working audio engine so the reason can be shown, e.g. a missing bass.dll.
    private static MainViewModel CreateMainViewModel(out IAudioPlayer? player)
    {
        try
        {
            player = new BassAudioPlayer();
            return new MainViewModel(new PlayerViewModel(player), null);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException
                                       or EntryPointNotFoundException or InvalidOperationException)
        {
            player = null;
            return new MainViewModel(null, $"Could not start the audio engine: {ex.Message}\n\nSee native/README.md.");
        }
    }
}
