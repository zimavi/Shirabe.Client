using System.Text.Json;

using Avalonia.Media;

using Shirabe.Client.Localization;

namespace Shirabe.Client.Services;

/// <summary>
/// The colours of the UI. A theme is only data: two are built in, more come from .json files (see <see cref="Parse"/>).
/// The accent is the default one; it can be replaced by the cover's colour (<see cref="AppSettings.FollowCoverAccent"/>).
/// </summary>
public sealed record Theme(
    string Name,
    Color Accent,
    Color Background,
    Color Sidebar,
    Color Surface,
    Color SurfaceHover,
    Color Divider,
    Color TextPrimary,
    Color TextSecondary,
    Color Track)
{
    // Keep in sync with the design-time defaults in Styles/Theme.axaml.
    public static readonly Theme Dark = new(
        "Dark", Color.Parse("#E8456B"), Color.Parse("#121214"), Color.Parse("#1A1A1D"), Color.Parse("#232327"),
        Color.Parse("#2E2E33"), Color.Parse("#2A2A2F"), Color.Parse("#F2F2F4"), Color.Parse("#9A9AA3"),
        Color.Parse("#3A3A40"));

    /// <summary>True black backgrounds for OLED screens; surfaces stay just visible.</summary>
    public static readonly Theme Oled = Dark with
    {
        Name = "OLED",
        Background = Colors.Black,
        Sidebar = Colors.Black,
        Surface = Color.Parse("#141416"),
        SurfaceHover = Color.Parse("#1F1F23"),
        Divider = Color.Parse("#1E1E22"),
        Track = Color.Parse("#2E2E33"),
    };

    /// <summary>
    /// Reads a theme file: a JSON object with a <c>"name"</c> and any of <c>accent, background, sidebar, surface,
    /// surfaceHover, divider, textPrimary, textSecondary, track</c> as "#RRGGBB" strings. Colours left out come
    /// from <see cref="Dark"/>, so themes are meant to be dark ones (the cover accent is tuned for dark surfaces too).
    /// </summary>
    /// <returns>Null, with the reason in <paramref name="error"/>, when the file is not a usable theme.</returns>
    public static Theme? Parse(string json, out string? error)
    {
        error = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(name.GetString()))
            {
                error = Loc.Get("Theme_NeedsName");
                return null;
            }

            Color Read(string key, Color fallback)
            {
                if (!root.TryGetProperty(key, out var value)) return fallback;
                if (value.ValueKind == JsonValueKind.String && Color.TryParse(value.GetString(), out var color))
                    return color;
                throw new FormatException(Loc.Get("Theme_BadColour", key));
            }

            return new Theme(
                name.GetString()!.Trim(), Read("accent", Dark.Accent), Read("background", Dark.Background),
                Read("sidebar", Dark.Sidebar), Read("surface", Dark.Surface), Read("surfaceHover", Dark.SurfaceHover),
                Read("divider", Dark.Divider), Read("textPrimary", Dark.TextPrimary),
                Read("textSecondary", Dark.TextSecondary), Read("track", Dark.Track));
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            error = ex.Message;
            return null;
        }
    }
}

/// <summary>The built-in themes plus the ones found in a folder. Files that cannot be used are listed in <see cref="Problems"/>.</summary>
public sealed class ThemeCatalog
{
    public ThemeCatalog(IEnumerable<Theme> custom, IEnumerable<string> problems, string? folder = null)
    {
        Themes = [Theme.Dark, Theme.Oled, .. custom];
        Problems = [.. problems];
        Folder = folder;
    }

    /// <summary>Built-in themes first, then the custom ones by file name.</summary>
    public IReadOnlyList<Theme> Themes { get; }

    public IReadOnlyList<string> Problems { get; }

    /// <summary>Where custom themes are looked for; null when there is no such place (tests).</summary>
    public string? Folder { get; }

    /// <summary>The theme called <paramref name="name"/> (any case), or Dark when there is none, e.g. its file was deleted.</summary>
    public Theme Find(string? name) =>
        Themes.FirstOrDefault(theme => string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase)) ??
        Theme.Dark;

    /// <summary>Reads every *.json in <paramref name="folder"/>; a missing folder just means no custom themes.</summary>
    public static ThemeCatalog Load(string folder)
    {
        var custom = new List<Theme>();
        var problems = new List<string>();
        var taken = new HashSet<string>([Theme.Dark.Name, Theme.Oled.Name], StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
            {
                var label = Path.GetFileName(file);
                try
                {
                    if (Theme.Parse(File.ReadAllText(file), out var error) is not { } theme)
                        problems.Add($"{label}: {error}");
                    else if (!taken.Add(theme.Name)) problems.Add($"{label}: {Loc.Get("Theme_NameTaken", theme.Name)}");
                    else custom.Add(theme);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    problems.Add($"{label}: {ex.Message}");
                }
            }
        }

        return new ThemeCatalog(custom, problems, folder);
    }
}