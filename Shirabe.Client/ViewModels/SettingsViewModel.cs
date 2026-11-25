using CommunityToolkit.Mvvm.ComponentModel;

using Shirabe.Client.Services;
using Shirabe.Core.Playback;

namespace Shirabe.Client.ViewModels;

/// <summary>The settings page: a list of <see cref="SettingsPageViewModel"/> and the one that is open.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    /// <param name="output">Where the sound goes, for the Audio page; null when there is no player that can tell.</param>
    public SettingsViewModel(AppSettings settings, ThemeCatalog themes, IAudioOutput? output = null,
        PlayerViewModel? player = null, LastFmViewModel? lastFm = null)
    {
        Settings = settings;
        // To add a settings page: write its view model and view, then list it here.
        Pages =
        [
            new GeneralSettingsViewModel(settings),
            new AudioSettingsViewModel(settings, output, player),
            new AppearanceSettingsViewModel(settings, themes),
            new LyricsSettingsViewModel(settings),
            new ShortcutsSettingsViewModel(settings),
            new ConnectionsSettingsViewModel(settings, lastFm ?? new LastFmViewModel()),
            new AboutViewModel(),
        ];
        SelectedPage = Pages[0];
    }

    /// <summary>The settings the pages edit; other parts of the app read them too.</summary>
    public AppSettings Settings { get; }

    public IReadOnlyList<SettingsPageViewModel> Pages { get; }

    [ObservableProperty] public partial SettingsPageViewModel SelectedPage { get; set; }
}