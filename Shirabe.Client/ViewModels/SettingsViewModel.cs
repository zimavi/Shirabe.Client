using CommunityToolkit.Mvvm.ComponentModel;

using Shirabe.Client.Services;

namespace Shirabe.Client.ViewModels;

/// <summary>The settings page: a list of <see cref="SettingsPageViewModel"/> and the one that is open.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel(AppSettings settings, ThemeCatalog themes)
    {
        Settings = settings;
        // To add a settings page: write its view model and view, then list it here.
        Pages =
        [
            new GeneralSettingsViewModel(settings),
            new AppearanceSettingsViewModel(settings, themes),
            new LyricsSettingsViewModel(settings),
            new ConnectionsSettingsViewModel(settings),
            new AboutViewModel(),
        ];
        SelectedPage = Pages[0];
    }

    /// <summary>The settings the pages edit; other parts of the app read them too.</summary>
    public AppSettings Settings { get; }

    public IReadOnlyList<SettingsPageViewModel> Pages { get; }

    [ObservableProperty] public partial SettingsPageViewModel SelectedPage { get; set; }
}