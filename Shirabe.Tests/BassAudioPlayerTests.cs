using Shirabe.Audio;
using Shirabe.Core.Media;
using Shirabe.Core.Playback;
using Xunit;

namespace Shirabe.Tests;

/// <summary>
/// Runs the real BASS engine on its "no sound" device, so nothing is audible. Skipped when the
/// native library has not been downloaded, see native/README.md.
/// </summary>
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

    /// <summary>A 440 Hz mono 16-bit 44.1 kHz sine wave.</summary>
    private string WriteWav(double seconds)
    {
        const int rate = 44100;
        var samples = (int)(rate * seconds);
        var path = NewTempPath(".wav");

        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        for (var i = 0; i < samples; i++)
            writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000));

        return path;
    }
}
