namespace Shirabe.Client.ViewModels;

/// <summary>
/// One page of the settings (Appearance, Connections...). The view is found by name like any page: a
/// <c>FooSettingsViewModel</c> is shown by <c>Views/FooSettingsView</c>. Add the page to <see cref="SettingsViewModel"/>.
/// </summary>
public abstract class SettingsPageViewModel(string title, string description) : ViewModelBase
{
    public string Title { get; } = title;

    /// <summary>One line under the title in the list of pages.</summary>
    public string Description { get; } = description;
}
