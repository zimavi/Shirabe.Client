using Shirabe.Client.Localization;
using Shirabe.Client.Services;

namespace Shirabe.Client.ViewModels;

public sealed class AppearanceSettingsViewModel : SettingsPageViewModel
{
    private readonly string? _themesFolder;

    public AppearanceSettingsViewModel(AppSettings settings, ThemeCatalog themes)
        : base("Appearance_Title", "Appearance_Description")
    {
        Settings = settings;
        _themesFolder = themes.Folder;
        ThemeNames = [.. themes.Themes.Select(theme => theme.Name)];
        ThemeProblems = string.Join('\n', themes.Problems);
        RefreshOnLanguageChange(nameof(CustomThemesHint));
    }

    public AppSettings Settings { get; }

    public IReadOnlyList<string> ThemeNames { get; }

    public string CustomThemesHint =>
        Loc.Get("Appearance_CustomThemesHint", _themesFolder ?? Loc.Get("Appearance_ThemesFolder"));

    /// <summary>Theme files that could not be used and why, one per line.</summary>
    public string ThemeProblems { get; }

    public bool HasThemeProblems => ThemeProblems.Length > 0;
}