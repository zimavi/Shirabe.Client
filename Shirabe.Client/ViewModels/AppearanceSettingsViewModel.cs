using Shirabe.Client.Services;

namespace Shirabe.Client.ViewModels;

public sealed class AppearanceSettingsViewModel : SettingsPageViewModel
{
    public AppearanceSettingsViewModel(AppSettings settings, ThemeCatalog themes)
        : base("Appearance", "Theme and accent colour")
    {
        Settings = settings;
        ThemeNames = [.. themes.Themes.Select(theme => theme.Name)];
        ThemeProblems = string.Join('\n', themes.Problems);
        CustomThemesHint = "Add your own: put a .json file with a \"name\" and any of these colours as #RRGGBB "
                           + "(accent, background, sidebar, surface, surfaceHover, divider, textPrimary, textSecondary, track) "
                           + (themes.Folder is null ? "in the themes folder" : $"in {themes.Folder}")
                           + ", then restart. Colours left out come from Dark.";
    }

    public AppSettings Settings { get; }

    public IReadOnlyList<string> ThemeNames { get; }

    public string CustomThemesHint { get; }

    /// <summary>Theme files that could not be used and why, one per line.</summary>
    public string ThemeProblems { get; }

    public bool HasThemeProblems => ThemeProblems.Length > 0;
}
