using Shirabe.Client.Localization;

namespace Shirabe.Client.ViewModels;

/// <summary>
/// One page of the settings (Appearance, Connections...). The view is found by name like any page: a
/// <c>FooSettingsViewModel</c> is shown by <c>Views/FooSettingsView</c>. Add the page to <see cref="SettingsViewModel"/>.
/// The title and description are keys of Resources/Strings.resx, the icon is one of Styles/Icons.axaml.
/// </summary>
public abstract class SettingsPageViewModel : ViewModelBase
{
    protected SettingsPageViewModel(string titleKey, string descriptionKey, string iconKey)
    {
        IconKey = iconKey;
        Title = titleKey;
        Description = descriptionKey;
        RefreshOnLanguageChange(nameof(Title), nameof(Description));
    }

    /// <summary>Name of the icon beside the title in the list of pages, such as "Icon.Home".</summary>
    public string IconKey { get; }

    public string Title => Loc.Get(field);

    /// <summary>One line under the title in the list of pages.</summary>
    public string Description => Loc.Get(field);
}