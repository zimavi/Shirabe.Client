using CommunityToolkit.Mvvm.ComponentModel;
using Shirabe.Client.Services;

namespace Shirabe.Client.ViewModels;

/// <summary>The settings page: a list of <see cref="SettingsPageViewModel"/> and the one that is open.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel(AppSettings settings, ThemeCatalog themes)
    {
        // To add a settings page: write its view model and view, then list it here.
        Pages =
        [
            new GeneralSettingsViewModel(settings),
            new AppearanceSettingsViewModel(settings, themes),
            new ConnectionsSettingsViewModel(settings),
        ];
        SelectedPage = Pages[0];
    }

    public IReadOnlyList<SettingsPageViewModel> Pages { get; }

    [ObservableProperty]
    public partial SettingsPageViewModel SelectedPage { get; set; }
}
