using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Platform;
using Avalonia.Threading;
using Shirabe.Audio;
using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;
using Shirabe.Client.Views;
using Shirabe.Core.Playback;
using Shirabe.Library;

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
            var window = new MainWindow
            {
                DataContext = mainViewModel
            };
            desktop.MainWindow = window;
            SetUpTray(desktop, window, mainViewModel.Player);
            if (mainViewModel.Player is { } accentSource) FollowCoverAccent(accentSource);
            var presence = DiscordPresence.ApplicationId.Length > 0 && mainViewModel.Player is { } shown
                ? new DiscordPresence(shown)
                : null;
            desktop.Exit += (_, _) =>
            {
                presence?.Dispose();
                player?.Dispose();
            };

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

    /// <summary>Recolours the accent to match the cover of the song that is playing; no cover (or a colourless one) restores the default.</summary>
    private void FollowCoverAccent(PlayerViewModel player)
    {
        var defaultAccent = (Color)this.FindResource("AccentColor")!;
        var palette = Styles.OfType<FluentTheme>().First().Palettes[ThemeVariant.Dark]!;

        player.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(PlayerViewModel.ArtworkPath)) return;

            var cover = ArtworkBitmaps.Load(player.ArtworkPath, 48);
            var accent = (cover is null ? null : CoverAccent.FromBitmap(cover)) ?? defaultAccent;
            Resources["AccentBrush"] = new SolidColorBrush(accent); // views use DynamicResource, so they follow
            palette.Accent = accent; // Fluent's own controls (selection, focus)
        };
    }

    /// <summary>Tray icon with Open, media controls and Quit. Closing the window hides it instead of ending the app.</summary>
    private void SetUpTray(IClassicDesktopStyleApplicationLifetime desktop, Window window, PlayerViewModel? player)
    {
        if (TrayIcon.GetIcons(this)?.FirstOrDefault() is not { } tray) return;

        void Open()
        {
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }

        var open = new NativeMenuItem("Open Shirabe");
        open.Click += (_, _) => Open();
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => desktop.Shutdown();

        var menu = new NativeMenu { open, new NativeMenuItemSeparator() };
        if (player is not null)
        {
            var playPause = new NativeMenuItem(player.PlayPauseText) { Command = player.TogglePlayPauseCommand };
            player.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PlayerViewModel.PlayPauseText)) playPause.Header = player.PlayPauseText;
            };
            menu.Add(new NativeMenuItem("Previous") { Command = player.PreviousCommand });
            menu.Add(playPause);
            menu.Add(new NativeMenuItem("Next") { Command = player.NextCommand });
            menu.Add(new NativeMenuItemSeparator());
        }

        menu.Add(quit);
        tray.Menu = menu;
        tray.Clicked += (_, _) => Open();

        // Only the user's own close (X, Alt+F4) is intercepted: Quit and OS shutdown must still end the app.
        window.Closing += (_, e) =>
        {
            if (e.CloseReason != WindowCloseReason.WindowClosing) return;
            e.Cancel = true;
            window.Hide();
        };
    }

    // The app still opens without a working audio engine so the reason can be shown, e.g. a missing bass.dll.
    private static MainViewModel CreateMainViewModel(out IAudioPlayer? player)
    {
        try
        {
            player = new BassAudioPlayer();
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException
                                       or EntryPointNotFoundException or InvalidOperationException)
        {
            player = null;
            return new MainViewModel(null, null, $"Could not start the audio engine: {ex.Message}\n\nSee native/README.md.");
        }

        var playerViewModel = new PlayerViewModel(player);

        // SHIRABE_LIBRARY_DB points the app at another database file (testing, portable setups).
        var libraryPath = Environment.GetEnvironmentVariable("SHIRABE_LIBRARY_DB") is { Length: > 0 } custom
            ? custom
            : LocalLibrary.DefaultPath;

        // A library that cannot be opened (damaged file, locked folder...) must not stop the player from working.
        try
        {
            var library = LocalLibrary.Open(libraryPath);
            var songs = new SongsViewModel(library, library, new FolderPicker(GetMainWindow), playerViewModel);
            return new MainViewModel(playerViewModel, songs, null);
        }
        catch (Exception ex)
        {
            return new MainViewModel(playerViewModel, null, null,
                $"The music library could not be opened ({libraryPath}): {ex.Message}");
        }
    }

    private static TopLevel? GetMainWindow() =>
        (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
