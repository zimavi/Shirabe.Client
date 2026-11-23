using System.Globalization;

using Shirabe.Client.Localization;
using Shirabe.Client.Services;

namespace Shirabe.Client.ViewModels;

public sealed class LyricsSettingsViewModel : SettingsPageViewModel
{
    public LyricsSettingsViewModel(AppSettings settings) : base("Lyrics_Title", "Lyrics_Description")
    {
        Settings = settings;
        // The labels next to the sliders follow them.
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.LyricsTextSize)) OnPropertyChanged(nameof(TextSizeText));
            else if (e.PropertyName == nameof(AppSettings.LyricsDelayMs)) OnPropertyChanged(nameof(DelayText));
        };
        RefreshOnLanguageChange(nameof(TextSizeText), nameof(DelayText));
    }

    public AppSettings Settings { get; }

    public string TextSizeText => Loc.Get("Lyrics_TextSizeValue",
        string.Create(CultureInfo.InvariantCulture, $"{Settings.LyricsTextSize:0}"));

    public string DelayText => Settings.LyricsDelayMs switch
    {
        0 => Loc.Get("Lyrics_NoDelay"),
        var ms => Loc.Get("Lyrics_DelayValue",
            string.Create(CultureInfo.InvariantCulture, $"{(ms > 0 ? "+" : "")}{ms:0}")),
    };
}