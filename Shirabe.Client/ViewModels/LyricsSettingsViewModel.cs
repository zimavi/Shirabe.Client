using System.Globalization;
using Shirabe.Client.Services;

namespace Shirabe.Client.ViewModels;

public sealed class LyricsSettingsViewModel : SettingsPageViewModel
{
    public LyricsSettingsViewModel(AppSettings settings) : base("Lyrics", "Text size, vocals and timing")
    {
        Settings = settings;
        // The labels next to the sliders follow them.
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.LyricsTextSize)) OnPropertyChanged(nameof(TextSizeText));
            else if (e.PropertyName == nameof(AppSettings.LyricsDelayMs)) OnPropertyChanged(nameof(DelayText));
        };
    }

    public AppSettings Settings { get; }

    public string TextSizeText => string.Create(CultureInfo.InvariantCulture, $"{Settings.LyricsTextSize:0} pt");

    public string DelayText => Settings.LyricsDelayMs switch
    {
        0 => "No delay",
        var ms => string.Create(CultureInfo.InvariantCulture, $"{(ms > 0 ? "+" : "")}{ms:0} ms"),
    };
}
