using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Shirabe.Core.Playback;

using Xunit;

namespace Shirabe.Tests;

public class AudioSettingsTests
{
    private readonly AppSettings _settings = new();
    private readonly FakeAudioPlayer _engine = new();

    [Theory]
    [InlineData(NormalizationMode.Off, -6.0, 1.0, -12.0, 1.0, 1.0)]
    [InlineData(NormalizationMode.Track, -6.0206, 1.0, -12.0, 1.0, 0.5)]
    [InlineData(NormalizationMode.Album, -6.0, 1.0, -20.0, 1.0, 0.1)]
    [InlineData(NormalizationMode.Album, -20.0, 1.0, null, null, 0.1)] // no album value: the track's
    [InlineData(NormalizationMode.Track, null, null, -20.0, 1.0, 0.1)]
    [InlineData(NormalizationMode.Track, 6.0206, 0.25, null, null, 2.0)] // a boost the peak leaves room for
    [InlineData(NormalizationMode.Track, 12.0, 0.8, null, null, 1.25)] // ...stopped at the peak
    [InlineData(NormalizationMode.Track, 6.0, null, null, null, 1.0)] // no peak: never louder than recorded
    [InlineData(NormalizationMode.Track, null, null, null, null, 1.0)]
    public void The_gain_follows_the_mode_and_never_clips(NormalizationMode mode, double? track, double? trackPeak,
        double? album, double? albumPeak, double expected)
    {
        Assert.Equal(expected, new ReplayGain(track, trackPeak, album, albumPeak).Factor(mode), 3);
    }

    [Fact]
    public void The_speed_applies_only_while_the_speed_control_is_on()
    {
        _settings.PlaybackSpeed = 1.25;
        var player = Player();
        Assert.Equal(1, _engine.Speed);

        _settings.PlaybackSpeedControl = true;
        Assert.Equal(1.25, _engine.Speed);
        _settings.PlaybackSpeedControl = false;
        Assert.Equal(1, _engine.Speed);
        Assert.Equal(1.25, _settings.PlaybackSpeed); // remembered for next time
        Assert.Equal(1, player.EffectiveSpeed);
    }

    [Theory]
    [InlineData(0.8, "0.8×", true, false, false)]
    [InlineData(1.0, "1×", false, true, false)]
    [InlineData(1.25, "1.25×", false, false, true)]
    [InlineData(1.1, "1.1×", false, false, false)]
    public void The_speed_menu_marks_the_preset_in_use(double speed, string text, bool slowed, bool normal, bool spedUp)
    {
        var player = Player();
        var changed = new List<string?>();
        player.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _settings.PlaybackSpeed = speed == 1 ? 1.5 : 1; // so that the next line is a change
        _settings.PlaybackSpeed = speed;

        Assert.Equal((text, slowed, normal, spedUp, !normal),
            (player.SpeedText, player.IsSlowed, player.IsNormalSpeed, player.IsSpedUp, player.IsSpeedChanged));
        Assert.Contains(nameof(player.SpeedText), changed);
        Assert.Contains(nameof(player.IsSpedUp), changed);
    }

    [Fact]
    public void The_preset_buttons_set_the_speed()
    {
        var player = Player();

        player.UseSlowedSpeedCommand.Execute(null);
        Assert.Equal(0.8, _settings.PlaybackSpeed);
        player.UseSpedUpSpeedCommand.Execute(null);
        Assert.Equal(1.25, _settings.PlaybackSpeed);
        player.UseNormalSpeedCommand.Execute(null);
        Assert.Equal(1, _settings.PlaybackSpeed);
    }

    [Fact]
    public void The_player_follows_keep_pitch_reverb_and_the_equaliser()
    {
        _settings.Reverb = true;
        _settings.EqualizerGains = [3, -3];
        Player();
        Assert.True(_engine.Reverb);
        Assert.False(_engine.KeepPitch);
        Assert.Empty(_engine.Equalizer); // switched off: the saved bands wait

        _settings.KeepPitch = true;
        _settings.Reverb = false;
        _settings.EqualizerEnabled = true;

        Assert.True(_engine.KeepPitch);
        Assert.False(_engine.Reverb);
        Assert.Equal([3, -3, 0, 0, 0, 0, 0, 0, 0, 0], _engine.Equalizer);
    }

    [Theory]
    [InlineData("""{ "EqualizerGains": null }""", 0, 0)]
    [InlineData("""{ "EqualizerGains": [99, -99, 1.25, 0, 0, 0, 0, 0, 0, 0, 7, 7] }""", 12, -12)]
    [InlineData("""{ "EqualizerGains": [5] }""", 5, 0)]
    public void Equaliser_values_from_the_file_are_made_to_fit(string json, double first, double second)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, json);

            var gains = AppSettings.Load(path).EqualizerGains;

            Assert.Equal(10, gains.Length);
            Assert.Equal((first, second), (gains[0], gains[1]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task The_player_follows_speed_and_volume_curve()
    {
        _settings.PlaybackSpeedControl = true;
        _settings.PlaybackSpeed = 1.25;
        var player = Player();
        Assert.Equal(1.25, _engine.Speed);

        player.Volume = 0.5;
        Assert.Equal(0.5f, _engine.Volume);
        _settings.ExponentialVolume = true;
        Assert.Equal(0.125f, _engine.Volume);
        Assert.Equal(0.5, player.Volume); // the slider stays where it is
        player.Volume = 1;
        Assert.Equal(1f, _engine.Volume);

        _settings.PlaybackSpeed = 0.75;
        Assert.Equal(0.75, _engine.Speed);
        await player.PlayAsync(Song("A"));
        Assert.Equal(0.75, _engine.Speed);
    }

    [Fact]
    public async Task Normalisation_sets_the_gain_for_each_song_and_follows_the_setting_while_playing()
    {
        var source = new FakeReplayGain();
        var player = Player(source);

        await player.PlayAsync(Song("loud"));
        Assert.Equal(1f, _engine.Gain);
        Assert.Equal(0, source.Reads); // off: the file is not read

        _settings.Normalization = NormalizationMode.Track;
        Assert.Equal(0.5f, _engine.Gain, 3);
        _settings.Normalization = NormalizationMode.Album;
        Assert.Equal(0.25f, _engine.Gain, 3);

        await player.PlayAsync(Song("untagged"));
        Assert.Equal(1f, _engine.Gain);
        await player.PlayAsync(Song("loud"));
        Assert.Equal(0.25f, _engine.Gain, 3);

        _settings.Normalization = NormalizationMode.Off;
        Assert.Equal(1f, _engine.Gain);
    }

    [Fact]
    public void The_wheel_moves_the_volume_by_the_chosen_step()
    {
        var player = Player();
        player.Volume = 0.504;

        player.ScrollVolume(-1);
        Assert.Equal(0.49, player.Volume);
        _settings.VolumeScrollStep = 10;
        player.ScrollVolume(1);
        Assert.Equal(0.59, player.Volume);
        for (var i = 0; i < 10; i++) player.ScrollVolume(1);
        Assert.Equal(1, player.Volume);
    }

    [Fact]
    public void Audio_settings_are_saved_and_odd_values_in_the_file_are_made_usable()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, """{ "OutputDevice": null, "PlaybackSpeed": 0, "VolumeScrollStep": 900 }""");
            var settings = AppSettings.Load(path);

            Assert.Equal("", settings.OutputDevice);
            Assert.Equal(0.5, settings.PlaybackSpeed);
            Assert.Equal(25, settings.VolumeScrollStep);

            settings.Normalization = NormalizationMode.Album;

            Assert.Contains("\"Album\"", File.ReadAllText(path));
            Assert.Equal(NormalizationMode.Album, AppSettings.Load(path).Normalization);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private PlayerViewModel Player(IReplayGainSource? source = null)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        return new PlayerViewModel(_engine, _settings, source);
    }

    private static TrackSummary Song(string title) =>
        new(Guid.NewGuid(), title, null, null, null, new Uri(Path.Combine(Path.GetTempPath(), title + ".mp3")).AbsoluteUri);

    private sealed class FakeReplayGain : IReplayGainSource
    {
        public int Reads { get; private set; }

        public Task<ReplayGain?> ReadAsync(string mediaUri, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(mediaUri.Contains("loud") ? new ReplayGain(-6.0206, 1, -12.0412, 1) : null);
        }
    }
}
