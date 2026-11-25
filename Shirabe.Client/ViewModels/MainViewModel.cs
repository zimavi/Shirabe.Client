using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Shirabe.Client.Services;
using Shirabe.Client.Studio;
using Shirabe.Core.Lyrics;

namespace Shirabe.Client.ViewModels;

/// <summary>The application shell: which page is showing, plus the player that outlives page changes.</summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly HomeViewModel _home;
    private readonly Func<StudioViewModel>? _studioFactory;
    private StudioViewModel? _studio;

    /// <param name="player">Null when the audio engine could not start.</param>
    /// <param name="songs">Null when the music library could not be opened.</param>
    /// <param name="startupError">Why the audio engine could not start, shown instead of the shell.</param>
    /// <param name="libraryNotice">Why the music library could not be opened, shown on the Home page.</param>
    /// <param name="settings">Null in tests: the settings page then works on defaults that are not saved.</param>
    /// <param name="studioFactory">Builds Studio when it is first opened, so listeners who never open it pay nothing. Null: no Studio.</param>
    /// <param name="lyricsSource">Where lyrics come from. Null: the lyrics panel always says there are none.</param>
    public MainViewModel(PlayerViewModel? player, SongsViewModel? songs, string? startupError,
        string? libraryNotice = null,
        SettingsViewModel? settings = null, Func<StudioViewModel>? studioFactory = null,
        ILyricsSource? lyricsSource = null)
    {
        Settings = settings ?? new SettingsViewModel(new AppSettings(), new ThemeCatalog([], []));
        Lyrics = player is null ? null : new LyricsViewModel(player, lyricsSource, Settings.Settings);
        _studioFactory = studioFactory;
        Player = player;
        Songs = songs;
        StartupError = startupError;
        _home = new HomeViewModel(libraryNotice);
        CurrentPage = _home;

        if (player is not null)
            player.AudioSettingsRequested += (_, _) =>
            {
                Settings.SelectedPage = Settings.Pages.OfType<AudioSettingsViewModel>().Single();
                CurrentPage = Settings;
            };

        // A document of a song that the person wants to edit goes to Studio.
        if (songs is not null && studioFactory is not null)
            songs.EditInStudioRequested += (_, request) => OpenEditorInStudio(request);
    }

    private void OpenEditorInStudio(StudioEditRequest request)
    {
        var studio = GetStudio();
        CurrentPage = studio;
        studio.OpenEditor(request.Document.Json, request.Song,
            $"Editing the {request.Document.Layer} document of \"{request.Song.Title}\"");
    }

    public PlayerViewModel? Player { get; }

    public SongsViewModel? Songs { get; }

    /// <summary>The lyrics panel's content; null when there is no player.</summary>
    public LyricsViewModel? Lyrics { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>Messages over the bottom right of the shell (a new version is available...).</summary>
    public NotificationCenter Notifications { get; } = new();

    /// <summary>The short message in the title bar about the last scrobble.</summary>
    public ScrobbleStatusViewModel ScrobbleStatus { get; } = new();

    public string? StartupError { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeSelected), nameof(IsSongsSelected), nameof(IsSettingsSelected),
        nameof(IsStudioSelected))]
    public partial ViewModelBase CurrentPage { get; set; }

    public bool IsHomeSelected => CurrentPage is HomeViewModel;

    public bool IsSongsSelected => CurrentPage is SongsViewModel;

    public bool IsSettingsSelected => CurrentPage is SettingsViewModel;

    public bool IsStudioSelected => CurrentPage is StudioViewModel;

    public bool HasStudio => _studioFactory is not null;

    public bool HasLibrary => Songs is not null;

    private const double SeekSeconds = 5;
    private const double VolumeStep = 0.05;

    /// <summary>Does what a keyboard shortcut stands for. False without a player: there is no shell to act on.</summary>
    public bool Run(ShortcutAction action)
    {
        if (Player is not { }) return false;

        switch (action)
        {
            case ShortcutAction.SeekBack: Player.SeekBy(-SeekSeconds); break;
            case ShortcutAction.SeekForward: Player.SeekBy(SeekSeconds); break;
            case ShortcutAction.VolumeUp: Player.ChangeVolume(VolumeStep); break;
            case ShortcutAction.VolumeDown: Player.ChangeVolume(-VolumeStep); break;
            default:
                ICommand command = action switch
                {
                    ShortcutAction.PlayPause => Player.TogglePlayPauseCommand,
                    ShortcutAction.Previous => Player.PreviousCommand,
                    ShortcutAction.Next => Player.NextCommand,
                    ShortcutAction.Mute => Player.ToggleMuteCommand,
                    ShortcutAction.Shuffle => Player.ToggleShuffleCommand,
                    ShortcutAction.Repeat => Player.CycleRepeatCommand,
                    ShortcutAction.Lyrics => Player.ToggleLyricsCommand,
                    ShortcutAction.Queue => Player.ToggleQueueCommand,
                    ShortcutAction.Home => ShowHomeCommand,
                    ShortcutAction.Songs => ShowSongsCommand,
                    ShortcutAction.Studio => ShowStudioCommand,
                    ShortcutAction.Settings => ShowSettingsCommand,
                    _ => throw new ArgumentOutOfRangeException(nameof(action)),
                };
                if (command.CanExecute(null)) command.Execute(null); // Execute alone does not ask
                break;
        }

        return true;
    }

    /// <summary>Closes the queue or lyrics panel; false when neither was open.</summary>
    public bool ClosePanel()
    {
        if (Player is null || !(Player.IsQueueOpen || Player.IsLyricsOpen)) return false;
        Player.IsQueueOpen = Player.IsLyricsOpen = false;
        return true;
    }

    [RelayCommand]
    private void ShowHome() => CurrentPage = _home;

    [RelayCommand]
    private void ShowSettings() => CurrentPage = Settings;

    [RelayCommand(CanExecute = nameof(HasStudio))]
    private void ShowStudio() => CurrentPage = GetStudio();

    /// <summary>Shows a metadata document in Studio: a <c>.shirabe.json</c> the app was asked to open.</summary>
    public async Task OpenInStudioAsync(string path)
    {
        if (_studioFactory is null) return;

        var studio = GetStudio();
        CurrentPage = studio;
        await studio.OpenPathAsync(path);
    }

    private StudioViewModel GetStudio() => _studio ??= _studioFactory!();

    [RelayCommand(CanExecute = nameof(HasLibrary))]
    private async Task ShowSongsAsync()
    {
        CurrentPage = Songs!;
        await Songs!.LoadAsync(); // always fresh: cheap, and picks up scans and files that went missing
    }
}