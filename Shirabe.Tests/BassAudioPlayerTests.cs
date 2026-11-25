using Shirabe.Audio;
using Shirabe.Core.Media;
using Shirabe.Core.Playback;
using Xunit;

namespace Shirabe.Tests;

/// <summary>
/// Runs the real BASS engine on its "no sound" device, so nothing is audible. Skipped when the
/// native library has not been downloaded, see native/README.md.
/// </summary>
// BASS has process-wide state (one Init/Free per process), so every test class that uses it runs in this collection,
// i.e. never in parallel with another one.
[Collection("BASS")]
public class BassAudioPlayerTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var file in _files) File.Delete(file);
    }

    [Fact]
    public async Task Load_reports_the_duration_and_waits_paused_at_the_start()
    {
        using var player = CreatePlayer();

        await player.LoadAsync(new LocalFileSource(WriteWav(seconds: 1)), TestContext.Current.CancellationToken);

        Assert.Equal(PlaybackState.Paused, player.State);
        Assert.Equal(1.0, player.Duration!.Value.TotalSeconds, precision: 2);
        Assert.Equal(TimeSpan.Zero, player.Position);
    }

    [Fact]
    public async Task Seek_moves_the_position_exactly()
    {
        using var player = CreatePlayer();
        await player.LoadAsync(new LocalFileSource(WriteWav(seconds: 1)), TestContext.Current.CancellationToken);

        player.Seek(TimeSpan.FromSeconds(0.5));

        Assert.Equal(0.5, player.Position.TotalSeconds, precision: 2);
    }

    [Fact]
    public async Task Play_advances_the_position_and_Pause_holds_it()
    {
        using var player = CreatePlayer();
        await player.LoadAsync(new LocalFileSource(WriteWav(seconds: 3)), TestContext.Current.CancellationToken);

        player.Play();
        await Task.Delay(500, TestContext.Current.CancellationToken);
        var playing = player.Position.TotalSeconds;
        player.Pause();
        await Task.Delay(100, TestContext.Current.CancellationToken); // BASS applies a pause at its next update period
        var paused = player.Position;
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.InRange(playing, 0.3, 0.8);
        Assert.Equal(PlaybackState.Paused, player.State);
        Assert.Equal(paused, player.Position);
    }

    [Fact]
    public async Task Stop_rewinds_to_the_start()
    {
        using var player = CreatePlayer();
        await player.LoadAsync(new LocalFileSource(WriteWav(seconds: 2)), TestContext.Current.CancellationToken);
        player.Seek(TimeSpan.FromSeconds(1));
        player.Play();

        player.Stop();

        Assert.Equal(PlaybackState.Stopped, player.State);
        Assert.Equal(TimeSpan.Zero, player.Position);
    }

    [Fact]
    public async Task Ended_is_raised_when_the_media_finishes()
    {
        using var player = CreatePlayer();
        var ended = new TaskCompletionSource();
        player.Ended += (_, _) => ended.TrySetResult();
        await player.LoadAsync(new LocalFileSource(WriteWav(seconds: 0.3)), TestContext.Current.CancellationToken);

        player.Play();
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(PlaybackState.Stopped, player.State);
    }

    [Fact]
    public async Task Load_throws_for_data_that_is_not_audio()
    {
        using var player = CreatePlayer();
        var path = NewTempPath(".mp3");
        await File.WriteAllTextAsync(path, "this is not audio", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => player.LoadAsync(new LocalFileSource(path), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Speed_moves_through_the_source_faster_and_stays_for_the_next_one()
    {
        using var player = CreatePlayer();
        player.Speed = 2;
        await player.LoadAsync(new LocalFileSource(WriteWav(seconds: 4)), TestContext.Current.CancellationToken);

        player.Play();
        await Task.Delay(600, TestContext.Current.CancellationToken);

        Assert.InRange(player.Position.TotalSeconds, 0.8, 1.7); // about 1.2 s of the source in 0.6 s
        Assert.Equal(4.0, player.Duration!.Value.TotalSeconds, precision: 2); // in the source's own time
    }

    [Fact]
    public async Task The_source_and_output_can_be_described_and_an_unknown_device_falls_back_to_the_default()
    {
        using var player = CreatePlayer();
        Assert.Null(player.GetSource());
        await player.LoadAsync(new LocalFileSource(WriteWav(seconds: 1)), TestContext.Current.CancellationToken);
        player.Gain = 2;

        var source = player.GetSource()!;
        Assert.Equal("WAV", source.Format);
        Assert.True(source.SampleRate > 0 && source.Channels > 0 && source.BitDepth > 0);
        Assert.False(player.UseDevice("no such device"));
        Assert.True(player.UseDevice(""));
        Assert.NotNull(player.GetOutput());
        Assert.DoesNotContain(player.GetDevices(), device => device.Id.Length == 0 || device.Name == "No sound");
    }

    [Fact]
    public async Task Keeping_the_pitch_can_be_switched_while_playing_without_losing_the_place()
    {
        using var player = CreateEffectsPlayer();
        player.Speed = 2;
        await player.LoadAsync(new LocalFileSource(WriteWav(seconds: 8)), TestContext.Current.CancellationToken);
        player.Play();
        player.Seek(TimeSpan.FromSeconds(2));

        foreach (var keep in new[] { true, false, true })
        {
            var before = player.Position.TotalSeconds;
            player.KeepPitch = keep;

            Assert.Equal(PlaybackState.Playing, player.State);
            Assert.InRange(player.Position.TotalSeconds, before - 0.1, before + 0.5);
            await Task.Delay(400, TestContext.Current.CancellationToken);
            Assert.InRange(player.Position.TotalSeconds - before, 0.4, 1.4); // twice as fast either way
            Assert.Equal(8.0, player.Duration!.Value.TotalSeconds, precision: 1);
            Assert.Equal("WAV", player.GetSource()!.Format); // the file, not the processing stage
        }
    }

    [Fact]
    public async Task A_paused_song_stays_paused_where_it_is_when_keep_pitch_changes()
    {
        using var player = CreateEffectsPlayer();
        await player.LoadAsync(new LocalFileSource(WriteWav(seconds: 4)), TestContext.Current.CancellationToken);
        player.Seek(TimeSpan.FromSeconds(1.5));

        player.KeepPitch = true;

        Assert.Equal(PlaybackState.Paused, player.State);
        Assert.Equal(1.5, player.Position.TotalSeconds, precision: 1);
    }

    [Fact]
    public async Task Ended_is_raised_with_keep_pitch_and_every_effect_on()
    {
        using var player = CreateEffectsPlayer();
        var ended = new TaskCompletionSource();
        player.Ended += (_, _) => ended.TrySetResult();
        player.KeepPitch = true;
        player.Speed = 1.5;
        player.Reverb = true;
        player.Equalizer = [6, 0, 0, 0, 0, -6, 0, 0, 0, 6]; // 16 kHz is above this file's range: ignored
        await player.LoadAsync(new LocalFileSource(WriteWav(seconds: 0.5)), TestContext.Current.CancellationToken);

        player.Play();
        player.Reverb = false;
        player.Equalizer = [];
        player.Reverb = true;
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(PlaybackState.Stopped, player.State);
    }

    [Fact]
    public async Task The_file_is_closed_after_keep_pitch_made_the_stream_again()
    {
        using var player = CreateEffectsPlayer();
        var path = WriteWav(seconds: 1);
        await player.LoadAsync(new LocalFileSource(path), TestContext.Current.CancellationToken);
        player.KeepPitch = true;
        player.KeepPitch = false;

        player.Release();

        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    // Attaching the effect without an error says nothing about hearing it, so this reads what BASS renders.
    [Fact]
    public async Task Reverb_leaves_an_echo_loud_enough_to_hear_after_the_sound_stops()
    {
        using var player = CreateEffectsPlayer();
        const double burst = 0.49; // peak of the noise below: 0.2 s of it after half a second of silence
        var path = NewTempPath(".wav");
        WriteNoiseBurst(path);
        player.Reverb = true;
        await player.LoadAsync(new LocalFileSource(path), TestContext.Current.CancellationToken);
        // The player does not hand out its BASS handle, and nothing else can read the processed samples.
        var handle = (int)typeof(BassAudioPlayer)
            .GetField("_handle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(player)!;

        player.Play();
        var samples = new float[2048];
        var echo = 0f;
        while (player.State == PlaybackState.Playing && player.Position.TotalSeconds < 1.4)
        {
            var inTail = player.Position.TotalSeconds > 0.85;
            var bytes = ManagedBass.Bass.ChannelGetData(handle, samples,
                (int)ManagedBass.DataFlags.Float | samples.Length * sizeof(float));
            if (inTail && bytes > 0) echo = Math.Max(echo, samples.Take(bytes / sizeof(float)).Max(Math.Abs));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.InRange(echo / burst, 0.2, 1);
    }

    private static void WriteNoiseBurst(string path)
    {
        const int rate = 44100, frames = rate * 2;
        var random = new Random(1);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + frames * 4);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)2); // stereo
        writer.Write(rate);
        writer.Write(rate * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(frames * 4);
        for (var frame = 0; frame < frames; frame++)
        {
            var sample = frame >= rate / 2 && frame < rate * 7 / 10 ? (short)random.Next(-16000, 16000) : (short)0;
            writer.Write(sample);
            writer.Write(sample);
        }
    }

    private static BassAudioPlayer CreateEffectsPlayer()
    {
        var player = CreatePlayer();
        if (player.SupportsEffects) return player;

        player.Dispose();
        Assert.Skip("The BASS_FX native library is missing, see native/README.md.");
        return null!;
    }

    private static BassAudioPlayer CreatePlayer()
    {
        Assert.SkipUnless(IsBassAvailable(), "The BASS native library is missing, see native/README.md.");
        return new BassAudioPlayer(device: 0);
    }

    private static bool IsBassAvailable()
    {
        try
        {
            _ = ManagedBass.Bass.Version;
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    private string NewTempPath(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"shirabe-{Guid.NewGuid():N}{extension}");
        _files.Add(path);
        return path;
    }

    private string WriteWav(double seconds)
    {
        var path = NewTempPath(".wav");
        TestAudio.WriteWav(path, seconds);
        return path;
    }
}
