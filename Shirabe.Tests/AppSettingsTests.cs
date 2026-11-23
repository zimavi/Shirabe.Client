using Shirabe.Client.Services;
using Xunit;

namespace Shirabe.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "shirabe-settings-" + Guid.NewGuid());
    private string SettingsFile => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Without_a_file_the_defaults_apply_and_nothing_is_written()
    {
        var settings = AppSettings.Load(SettingsFile);

        Assert.True(settings.ResumeLastSession);
        Assert.False(settings.ReduceMotion);
        Assert.True(settings.DiscordPresenceEnabled);
        Assert.True(settings.CoverLookupEnabled);
        Assert.True(settings.DownloadArtworkEnabled);
        Assert.Equal((17, true, true, 0), (settings.LyricsTextSize, settings.ShowBackgroundVocals, settings.ShowPauseMarkers, settings.LyricsDelayMs));
        Assert.True(settings.FollowCoverAccent);
        Assert.Equal("Dark", settings.ThemeName);
        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public void A_change_is_saved_at_once_and_loaded_next_time()
    {
        var settings = AppSettings.Load(SettingsFile);

        settings.FollowCoverAccent = false;
        settings.ThemeName = "OLED";

        var again = AppSettings.Load(SettingsFile);
        Assert.False(again.FollowCoverAccent);
        Assert.Equal("OLED", again.ThemeName);
        Assert.True(again.DiscordPresenceEnabled); // untouched
        Assert.False(File.Exists(SettingsFile + ".tmp"));
    }

    [Fact]
    public void A_setting_missing_from_the_file_keeps_its_default()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsFile, """{ "DiscordPresenceEnabled": false, "SomethingFromANewerVersion": 1 }""");

        var settings = AppSettings.Load(SettingsFile);

        Assert.False(settings.DiscordPresenceEnabled);
        Assert.True(settings.CoverLookupEnabled);
    }

    [Fact]
    public void A_damaged_file_gives_the_defaults_instead_of_failing()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsFile, "{ not json");

        var settings = AppSettings.Load(SettingsFile);

        Assert.True(settings.DiscordPresenceEnabled);
    }

    [Fact]
    public void Settings_that_were_not_loaded_from_a_file_do_not_touch_the_disk()
    {
        var settings = new AppSettings();

        settings.ThemeName = "OLED";

        Assert.False(Directory.Exists(_folder));
    }
}
