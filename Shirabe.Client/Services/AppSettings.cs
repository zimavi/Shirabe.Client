using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Shirabe.Client.Services;

/// <summary>
/// The user's app settings. Every change is saved to the file it was loaded from, at once. A value missing from
/// the file keeps its default here, so adding a setting later needs no migration.
/// </summary>
public partial class AppSettings : ObservableObject
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private string? _path;

    /// <summary>Bring back the last song, volume, shuffle and repeat at startup (see <see cref="SessionStore"/>).</summary>
    [ObservableProperty]
    public partial bool ResumeLastSession { get; set; } = true;

    /// <summary>Show the song that is playing on the user's Discord profile.</summary>
    [ObservableProperty]
    public partial bool DiscordPresenceEnabled { get; set; } = true;

    /// <summary>Ask the iTunes Search API for a cover to show on the Discord card (sends artist and album text).</summary>
    [ObservableProperty]
    public partial bool CoverLookupEnabled { get; set; } = true;

    /// <summary>Download the covers that a song's metadata links to (the host of the link sees the user's address).</summary>
    [ObservableProperty]
    public partial bool DownloadArtworkEnabled { get; set; } = true;

    /// <summary>Take the accent colour from the cover of the song that is playing.</summary>
    [ObservableProperty]
    public partial bool FollowCoverAccent { get; set; } = true;

    /// <summary>Turns off the fade and slide animations (see the <c>.motion</c> class in Styles/Controls.axaml).</summary>
    [ObservableProperty]
    public partial bool ReduceMotion { get; set; }

    /// <summary>Name of the chosen <see cref="Theme"/>.</summary>
    [ObservableProperty]
    public partial string ThemeName { get; set; } = Theme.Dark.Name;

    /// <summary>Loads <paramref name="path"/>, or the defaults when there is no usable file there.</summary>
    public static AppSettings Load(string path)
    {
        AppSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // shortcut: an unreadable file is replaced by the defaults on the next change, keep a .bad copy if this ever holds more than toggles
            settings = new AppSettings();
        }

        settings._path = path; // after loading, so reading the file does not write it back
        return settings;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        Save();
    }

    private void Save()
    {
        if (_path is null) return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp"; // write beside, then swap, so a crash cannot leave half a file
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The setting still applies for this session.
        }
    }
}
