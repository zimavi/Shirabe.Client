using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
        // Settings and custom themes live next to the library database.
        var dataFolder = Path.GetDirectoryName(Path.GetFullPath(GetLibraryPath()))!;
        var settings = AppSettings.Load(Path.Combine(dataFolder, "settings.json"));
        var themes = ThemeCatalog.Load(Path.Combine(dataFolder, "themes"));
        var mainViewModel = CreateMainViewModel(new SettingsViewModel(settings, themes), out var player);
        _ = new Appearance(this, settings, themes, mainViewModel.Player); // subscribes itself; applies the theme before any window exists

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow
            {
                DataContext = mainViewModel
            };
            desktop.MainWindow = window;
            SetUpTray(desktop, window, mainViewModel.Player);

            // Animation styles apply under ".motion" (Styles/Controls.axaml); "Reduce motion" removes the class.
            void SyncMotion() => window.Classes.Set("motion", !settings.ReduceMotion);
            settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.ReduceMotion)) SyncMotion();
            };
            SyncMotion();

            // Follows the "Discord presence" setting while the app runs.
            DiscordPresence? presence = null;
            void SyncPresence()
            {
                var wanted = settings.DiscordPresenceEnabled && DiscordPresence.ApplicationId.Length > 0 && mainViewModel.Player is not null;
                if (wanted && presence is null) presence = new DiscordPresence(mainViewModel.Player!, settings);
                else if (!wanted && presence is not null)
                {
                    presence.Dispose();
                    presence = null;
                }
            }

            settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.DiscordPresenceEnabled)) SyncPresence();
            };
            SyncPresence();
            var session = new SessionStore(Path.Combine(dataFolder, "session.json"), settings);
            desktop.Exit += (_, _) =>
            {
                session.Flush(); // before the player goes away: it reads the position
                presence?.Dispose();
                player?.Dispose();
            };

            if (mainViewModel.Player is { } playerViewModel)
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    // Bring back the last session (paused), then save changes from here on.
                    if (settings.ResumeLastSession && session.Load() is { } saved) await playerViewModel.RestoreAsync(saved);
                    session.Attach(playerViewModel);

                    // "Open with Shirabe" / `Shirabe.Client.Desktop.exe song.mp3`: play the first argument instead of the restored song.
                    if (desktop.Args is [var path, ..]) await playerViewModel.OpenAsync(path);
                });
            }
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
    private static MainViewModel CreateMainViewModel(SettingsViewModel settings, out IAudioPlayer? player)
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

        var libraryPath = GetLibraryPath();

        // A library that cannot be opened (damaged file, locked folder...) must not stop the player from working.
        try
        {
            var library = LocalLibrary.Open(libraryPath);
            var songs = new SongsViewModel(library, library, new FolderPicker(GetMainWindow), playerViewModel);
            return new MainViewModel(playerViewModel, songs, null, settings: settings);
        }
        catch (Exception ex)
        {
            return new MainViewModel(playerViewModel, null, null,
                $"The music library could not be opened ({libraryPath}): {ex.Message}", settings);
        }
    }

    // SHIRABE_LIBRARY_DB points the app at another database file (testing, portable setups); settings and themes follow it.
    private static string GetLibraryPath() =>
        Environment.GetEnvironmentVariable("SHIRABE_LIBRARY_DB") is { Length: > 0 } custom ? custom : LocalLibrary.DefaultPath;

    private static TopLevel? GetMainWindow() =>
        (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
