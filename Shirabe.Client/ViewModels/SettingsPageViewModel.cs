using Shirabe.Client.Localization;

namespace Shirabe.Client.ViewModels;

/// <summary>
/// One page of the settings (Appearance, Connections...). The view is found by name like any page: a
/// <c>FooSettingsViewModel</c> is shown by <c>Views/FooSettingsView</c>. Add the page to <see cref="SettingsViewModel"/>.
/// The title and description are keys of Resources/Strings.resx.
/// </summary>
public abstract class SettingsPageViewModel : ViewModelBase
{
    private readonly string _titleKey;
    private readonly string _descriptionKey;

    protected SettingsPageViewModel(string titleKey, string descriptionKey)
    {
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;
        RefreshOnLanguageChange(nameof(Title), nameof(Description));
    }

    public string Title => Loc.Get(_titleKey);

    /// <summary>One line under the title in the list of pages.</summary>
    public string Description => Loc.Get(_descriptionKey);
}