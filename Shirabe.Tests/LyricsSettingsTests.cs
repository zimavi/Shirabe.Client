using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;
using Xunit;

namespace Shirabe.Tests;

public class LyricsSettingsTests
{
    [Fact]
    public void The_labels_next_to_the_sliders_follow_them()
    {
        var settings = new AppSettings();
        var page = new LyricsSettingsViewModel(settings);
        var changed = new List<string?>();
        page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Equal(("17 pt", "No delay"), (page.TextSizeText, page.DelayText));

        settings.LyricsTextSize = 22;
        settings.LyricsDelayMs = 120;
        Assert.Equal(("22 pt", "+120 ms"), (page.TextSizeText, page.DelayText));

        settings.LyricsDelayMs = -50;
        Assert.Equal("-50 ms", page.DelayText);
        Assert.Contains(nameof(LyricsSettingsViewModel.TextSizeText), changed);
        Assert.Contains(nameof(LyricsSettingsViewModel.DelayText), changed);
    }

    [Fact]
    public void The_lyrics_settings_are_saved_with_the_others_and_a_missing_value_keeps_its_default()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shirabe-settings-{Guid.NewGuid():N}.json");
        try
        {
            var settings = AppSettings.Load(path);
            settings.LyricsTextSize = 24;
            settings.ShowPauseMarkers = false;
            settings.LyricsDelayMs = 80;

            var again = AppSettings.Load(path);

            Assert.Equal((24, true, false, 80), (again.LyricsTextSize, again.ShowBackgroundVocals, again.ShowPauseMarkers, again.LyricsDelayMs));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
