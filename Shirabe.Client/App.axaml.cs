using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using Shirabe.Audio;
using Shirabe.Client.Localization;
using Shirabe.Client.Services;
using Shirabe.Client.Studio;
using Shirabe.Client.ViewModels;
using Shirabe.Client.Views;
using Shirabe.Core.Metadata;
using Shirabe.Core.Playback;
using Shirabe.Library;

namespace Shirabe.Client;

public partial class App : Application
{
    /// <summary>Where the newest released version is asked from; the head provides one. Null: no update check.</summary>
    public IReleaseSource? UpdateSource { get; init; }

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

        // The language is set before anything is read that can produce a text (the theme problems below) or any view
        // exists, and follows the setting while the app runs.
        Loc.Instance.SetLanguage(settings.Language);
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.Language)) Loc.Instance.SetLanguage(settings.Language);
        };

        ShortcutHints.Instance.Attach(settings);

        // shortcut: the theme problems are texts made once here, so they keep the startup language until a restart
        var themes = ThemeCatalog.Load(Path.Combine(dataFolder, "themes"));

        var mainViewModel = CreateMainViewModel(
            themes,
            (library, store) => new StudioViewModel(new FilePicker(GetMainWindow), library, store), settings,
            out var player);
        _ = new Appearance(this, settings, themes,
            mainViewModel.Player); // subscribes itself; applies the theme before any window exists

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { DataContext = mainViewModel };
            desktop.MainWindow = window;
            SetUpTray(desktop, window, mainViewModel.Player, settings);

            // Animation styles apply under ".motion" (Styles/Controls.axaml); "Reduce motion" removes the class.
            void SyncMotion() => window.Classes.Set("motion", !settings.ReduceMotion);
            settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.ReduceMotion)) SyncMotion();
            };
            SyncMotion();

            // Once at startup, in the background; a newer release shows as a notification (Settings > Connections turns it off).
            if (settings.CheckForUpdatesEnabled && UpdateSource is not null)
                _ = new UpdateNotifier(UpdateSource, mainViewModel.Notifications, AppVersion.Current).CheckAsync();

            // Follows the "Discord presence" setting while the app runs.
            DiscordPresence? presence = null;

            void SyncPresence()
            {
                var wanted = settings.DiscordPresenceEnabled && DiscordPresence.ApplicationId.Length > 0 &&
                             mainViewModel.Player is not null;
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
                    if (settings.ResumeLastSession && session.Load() is { } saved)
                        await playerViewModel.RestoreAsync(saved);
                    session.Attach(playerViewModel);

                    // "Open with Shirabe" / `Shirabe.Client.Desktop.exe song.mp3`: play the first argument instead of the restored song.
                    // A metadata document opens in Studio; anything else is audio to play.
                    if (desktop.Args is [var path, ..])
                    {
                        if (path.EndsWith(".shirabe.json", StringComparison.OrdinalIgnoreCase))
                            await mainViewModel.OpenInStudioAsync(path);
                        else await playerViewModel.OpenAsync(path);
                    }
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
            singleViewPlatform.MainView = new MainView { DataContext = mainViewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Tray icon with Open, media controls and Quit. Closing the window hides it instead of ending the app, unless the
    /// user turned "Keep running in the tray" off.
    /// </summary>
    private void SetUpTray(IClassicDesktopStyleApplicationLifetime desktop, Window window, PlayerViewModel? player,
        AppSettings settings)
    {
        if (TrayIcon.GetIcons(this)?.FirstOrDefault() is not { } tray) return;

        // Items go into the menu declared in App.axaml; replacing it (tray.Menu = new ...) crashes the macOS backend.
        if (tray.Menu is not { } menu) return;

        void Open()
        {
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }

        // Each item gets its text from a key, again whenever the language changes.
        void AddItem(string key, Action? click = null, ICommand? command = null)
        {
            var item = new NativeMenuItem { Command = command };
            if (click is not null) item.Click += (_, _) => click();
            void SetText() => item.Header = Loc.Get(key);
            SetText();
            Loc.Instance.LanguageChanged += (_, _) => SetText();
            menu.Add(item);
        }

        AddItem("Tray_Open", Open);
        menu.Add(new NativeMenuItemSeparator());
        if (player is not null)
        {
            var playPause = new NativeMenuItem(player.PlayPauseText) { Command = player.TogglePlayPauseCommand };
            player.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PlayerViewModel.PlayPauseText)) playPause.Header = player.PlayPauseText;
            };
            AddItem("Player_Previous", command: player.PreviousCommand);
            menu.Add(playPause);
            AddItem("Player_Next", command: player.NextCommand);
            menu.Add(new NativeMenuItemSeparator());
        }

        AddItem("Tray_Quit", () => desktop.Shutdown());
        tray.Clicked += (_, _) => Open();

        // Only the user's own close (X, Alt+F4) is intercepted: Quit and OS shutdown must still end the app.
        window.Closing += (_, e) =>
        {
            if (e.CloseReason != WindowCloseReason.WindowClosing || !settings.KeepRunningInTray) return;
            e.Cancel = true;
            window.Hide();
        };
    }

    // The app still opens without a working audio engine so the reason can be shown, e.g. a missing bass.dll.
    private static MainViewModel CreateMainViewModel(
        ThemeCatalog themes, Func<LocalLibrary?, IDocumentStore?, StudioViewModel> studio,
        AppSettings appSettings, out IAudioPlayer? player)
    {
        try
        {
            player = new BassAudioPlayer();
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException
                                       or EntryPointNotFoundException or InvalidOperationException)
        {
            player = null;
            return new MainViewModel(null, null,
                Loc.Get("Startup_AudioEngine", ex.Message));
        }

        var playerViewModel = new PlayerViewModel(player, appSettings, new ReplayGainReader());
        // A build made without a Last.fm API key has no client, and so no account to sign in to.
        var lastFm = LastFmClient.FromBuild();
        var account = lastFm is null ? null : CreateLastFmAccount(lastFm);

        // Made now, not when first shown: it moves the player to the saved output device.
        var settings = new SettingsViewModel(appSettings, themes, player as IAudioOutput, playerViewModel,
            new LastFmViewModel(account, OpenInBrowser, appSettings));

        var libraryPath = GetLibraryPath();

        // A library that cannot be opened (damaged file, locked folder...) must not stop the player from working.
        try
        {
            var library = LocalLibrary.Open(libraryPath);
            _ = new LinkedCovers(playerViewModel, appSettings, library); // subscribes itself
            var songs = new SongsViewModel(library, library, new FolderPicker(GetMainWindow), playerViewModel,
                track => new PropertiesViewModel(library, library, track, new FilePicker(GetMainWindow), library));
            // Studio writes through the player-aware store: the song that is playing is open, and a file that is open cannot be written to.
            var shell = new MainViewModel(playerViewModel, songs, null, settings: settings,
                studioFactory: () => studio(library, new PlayerAwareDocumentStore(library, playerViewModel)),
                lyricsSource: new LocalLyricsSource());
            if (account is not null)
                StartScrobbling(new Scrobbler(playerViewModel, appSettings, account, lastFm!, library), shell);
            return shell;
        }
        catch (Exception ex)
        {
            var shell = new MainViewModel(playerViewModel, null, null,
                Loc.Get("Startup_LibraryOpen", libraryPath, ex.Message), settings,
                () => studio(null, null));
            if (account is not null)
                StartScrobbling(new Scrobbler(playerViewModel, appSettings, account, lastFm!), shell);
            return shell;
        }
    }

    private static LastFmAccount CreateLastFmAccount(LastFmClient client)
    {
        var account = new LastFmAccount(client, ISecretStore.ForThisPlatform());
        Dispatcher.UIThread.Post(() => _ = account.LoadAsync()); // the saved session, without holding up the start
        return account;
    }

    private static void StartScrobbling(Scrobbler scrobbler, MainViewModel shell)
    {
        scrobbler.Reported += (_, report) => _ = shell.ScrobbleStatus.ShowAsync(report);
        DispatcherTimer.Run(() =>
        {
            _ = scrobbler.TickAsync();
            return true; // for as long as the app runs
        }, TimeSpan.FromSeconds(1));
    }

    private static void OpenInBrowser(string address)
    {
        if (GetMainWindow() is { } window) LinkLauncher.Open(window, address);
    }

    // SHIRABE_LIBRARY_DB points the app at another database file (testing, portable setups); settings and themes follow it.
    private static string GetLibraryPath() =>
        Environment.GetEnvironmentVariable("SHIRABE_LIBRARY_DB") is { Length: > 0 } custom
            ? custom
            : LocalLibrary.DefaultPath;

    private static TopLevel? GetMainWindow() =>
        (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}