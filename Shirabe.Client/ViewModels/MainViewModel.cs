using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Shirabe.Client.ViewModels;

/// <summary>The application shell: which page is showing, plus the player that outlives page changes.</summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly HomeViewModel _home;

    /// <param name="player">Null when the audio engine could not start.</param>
    /// <param name="songs">Null when the music library could not be opened.</param>
    /// <param name="startupError">Why the audio engine could not start, shown instead of the shell.</param>
    /// <param name="libraryNotice">Why the music library could not be opened, shown on the Home page.</param>
    public MainViewModel(PlayerViewModel? player, SongsViewModel? songs, string? startupError, string? libraryNotice = null)
    {
        Player = player;
        Songs = songs;
        StartupError = startupError;
        _home = new HomeViewModel(libraryNotice);
        CurrentPage = _home;
    }

    public PlayerViewModel? Player { get; }

    public SongsViewModel? Songs { get; }

    public string? StartupError { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeSelected), nameof(IsSongsSelected))]
    public partial ViewModelBase CurrentPage { get; set; }

    public bool IsHomeSelected => CurrentPage is HomeViewModel;

    public bool IsSongsSelected => CurrentPage is SongsViewModel;

    public bool HasLibrary => Songs is not null;

    [RelayCommand]
    private void ShowHome() => CurrentPage = _home;

    [RelayCommand(CanExecute = nameof(HasLibrary))]
    private async Task ShowSongsAsync()
    {
        CurrentPage = Songs!;
        await Songs!.LoadAsync(); // always fresh: cheap, and picks up scans and files that went missing
    }
}
