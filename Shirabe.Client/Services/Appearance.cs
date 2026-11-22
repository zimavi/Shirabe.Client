using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Shirabe.Client.ViewModels;

namespace Shirabe.Client.Services;

/// <summary>
/// Puts the chosen theme and accent on the running app by replacing the colour tokens of Styles/Theme.axaml
/// (views use DynamicResource, so they follow at once). The accent is the cover's colour while that is switched on
/// and the song has a colourful cover; otherwise the theme's own.
/// </summary>
public sealed class Appearance
{
    private readonly Application _app;
    private readonly AppSettings _settings;
    private readonly ThemeCatalog _themes;
    private readonly ColorPaletteResources _palette; // Fluent's own controls (selection, focus, popups)
    private Color? _coverAccent;

    public Appearance(Application app, AppSettings settings, ThemeCatalog themes, PlayerViewModel? player)
    {
        _app = app;
        _settings = settings;
        _themes = themes;
        _palette = app.Styles.OfType<FluentTheme>().First().Palettes[ThemeVariant.Dark]!;

        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppSettings.ThemeName) or nameof(AppSettings.FollowCoverAccent)) Apply();
        };

        if (player is not null)
        {
            player.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(PlayerViewModel.ArtworkPath)) return;

                var cover = ArtworkBitmaps.Load(player.ArtworkPath, 48);
                _coverAccent = cover is null ? null : CoverAccent.FromBitmap(cover);
                Apply();
            };
        }

        Apply();
    }

    private void Apply()
    {
        var theme = _themes.Find(_settings.ThemeName);
        Set("AppBackgroundBrush", theme.Background);
        Set("SidebarBrush", theme.Sidebar);
        Set("SurfaceBrush", theme.Surface);
        Set("SurfaceHoverBrush", theme.SurfaceHover);
        Set("DividerBrush", theme.Divider);
        Set("TextPrimaryBrush", theme.TextPrimary);
        Set("TextSecondaryBrush", theme.TextSecondary);
        Set("TrackBrush", theme.Track);
        _palette.RegionColor = theme.Background;

        var accent = (_settings.FollowCoverAccent ? _coverAccent : null) ?? theme.Accent;
        Set("AccentBrush", accent);
        _palette.Accent = accent;
    }

    private void Set(string key, Color color) => _app.Resources[key] = new SolidColorBrush(color);
}
