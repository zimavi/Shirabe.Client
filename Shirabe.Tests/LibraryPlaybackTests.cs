using Microsoft.Data.Sqlite;
using Shirabe.Audio;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Playback;
using Shirabe.Library;
using Xunit;

namespace Shirabe.Tests;

/// <summary>
/// The whole path a user takes: scan a folder, pick a song from the library, hear it. Real scanner, real
/// database, real BASS on its "no sound" device. Skipped when the BASS native library is missing.
/// </summary>
[Collection("BASS")]
public class LibraryPlaybackTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shirabe-e2e-{Guid.NewGuid():N}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_scanned_song_can_be_played_from_its_stored_address()
    {
        Assert.SkipUnless(IsBassAvailable(), "The BASS native library is missing, see native/README.md.");
        var music = Path.Combine(_root, "Müsik Ünï"); // spaces and non-ASCII must survive the file:// round trip
        Directory.CreateDirectory(music);
        TestAudio.WriteWav(Path.Combine(music, "Test Tone.wav"), seconds: 1);
        var library = LocalLibrary.Open(Path.Combine(_root, "library.db"));
        await library.ScanFolderAsync(music, cancellationToken: TestContext.Current.CancellationToken);
        var track = Assert.Single(await library.GetTracksAsync(TestContext.Current.CancellationToken));

        using var audio = new BassAudioPlayer(device: 0);
        var player = new PlayerViewModel(audio);
        await player.PlayAsync(track);

        Assert.Null(player.ErrorMessage);
        Assert.Equal("Test Tone", player.Title);
        Assert.Equal(PlaybackState.Playing, audio.State);
        Assert.Equal(1.0, audio.Duration!.Value.TotalSeconds, precision: 1);
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
}
