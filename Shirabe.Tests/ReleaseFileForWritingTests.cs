using Microsoft.Data.Sqlite;
using Shirabe.Audio;
using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Shirabe.Core.Metadata;
using Shirabe.Core.Playback;
using Shirabe.Library;
using Shirabe.Metadata;
using Xunit;

namespace Shirabe.Tests;

/// <summary>A song that is loaded in the player is open, and Windows refuses to write into a file that is open: Studio must not hit that.</summary>
public sealed class ReleaseFileForWritingTests
{
    private static TrackSummary Track(string title) =>
        new(Guid.NewGuid(), title, "Artist", null, null, new Uri(Path.Combine(Path.GetTempPath(), title + ".mp3")).AbsoluteUri);

    // ---- the player view model, with a fake engine ----

    [Fact]
    public async Task The_loaded_song_is_released_during_the_action_and_comes_back_where_it_was_playing()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var audio = new FakeAudioPlayer();
        var player = new PlayerViewModel(audio);
        var track = Track("A");
        await player.PlayAsync(track);
        audio.Position = TimeSpan.FromSeconds(42);
        PlaybackState? duringAction = null;
        object? loadedDuringAction = "unset";

        var result = await player.WithFileReleasedAsync(track.RecordingId, () =>
        {
            duringAction = audio.State;
            loadedDuringAction = audio.Loaded;
            return Task.FromResult("written");
        });

        Assert.Equal("written", result);
        Assert.Equal((PlaybackState.Stopped, null), (duringAction, loadedDuringAction));
        Assert.Equal(1, audio.Releases);
        Assert.Equal(2, audio.Loads.Count);
        Assert.Equal(TimeSpan.FromSeconds(42), audio.Position);
        Assert.Equal(PlaybackState.Playing, audio.State);
        Assert.Equal(42, player.PositionSeconds);
    }

    [Fact]
    public async Task A_paused_song_stays_paused()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var audio = new FakeAudioPlayer();
        var player = new PlayerViewModel(audio);
        var track = Track("A");
        await player.PlayAsync(track);
        audio.Pause();

        await player.WithFileReleasedAsync(track.RecordingId, () => Task.FromResult(0));

        Assert.Equal(PlaybackState.Paused, audio.State);
    }

    [Fact]
    public async Task Another_song_or_nothing_loaded_is_left_alone()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var audio = new FakeAudioPlayer();
        var player = new PlayerViewModel(audio);
        await player.WithFileReleasedAsync(Guid.NewGuid(), () => Task.FromResult(0)); // nothing loaded yet
        await player.PlayAsync(Track("A"));

        await player.WithFileReleasedAsync(Guid.NewGuid(), () => Task.FromResult(0));

        Assert.Equal(0, audio.Releases);
        Assert.Single(audio.Loads);
        Assert.Equal(PlaybackState.Playing, audio.State);
    }

    [Fact]
    public async Task The_song_comes_back_even_when_the_action_fails()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var audio = new FakeAudioPlayer();
        var player = new PlayerViewModel(audio);
        var track = Track("A");
        await player.PlayAsync(track);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            player.WithFileReleasedAsync<int>(track.RecordingId, () => throw new InvalidOperationException("write failed")));

        Assert.Equal(2, audio.Loads.Count);
        Assert.Equal(PlaybackState.Playing, audio.State);
    }

    [Fact]
    public async Task A_song_that_cannot_be_loaded_again_is_reported_not_thrown()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var audio = new FakeAudioPlayer();
        var player = new PlayerViewModel(audio);
        var track = Track("A");
        await player.PlayAsync(track);
        audio.LoadException = new IOException("the file is gone");

        await player.WithFileReleasedAsync(track.RecordingId, () => Task.FromResult(0));

        Assert.Contains("the file is gone", player.ErrorMessage);
    }

    [Fact]
    public async Task Only_writing_into_the_audio_file_releases_it_not_a_sidecar()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var audio = new FakeAudioPlayer();
        var player = new PlayerViewModel(audio);
        var track = Track("A");
        await player.PlayAsync(track);
        Assert.True(MetadataDocument.TryParseFile(
            $$"""{ "format": "shirabe.metadata", "version": "0.1", "subject": { "type": "recording", "id": "{{track.RecordingId}}" }, "layer": "personal", "source": { "kind": "user", "created": "2026-03-02T09:00:00Z" }, "revision": 1, "fields": {} }""",
            out var documents, out _));
        var store = new PlayerAwareDocumentStore(new RecordingStore(), player);

        await store.WriteToFileAsync(track.RecordingId, documents[0], DocumentOrigin.Sidecar);
        Assert.Equal(0, audio.Releases);

        await store.WriteToFileAsync(track.RecordingId, documents[0], DocumentOrigin.Embedded);
        Assert.Equal(1, audio.Releases);
    }

    private sealed class RecordingStore : IDocumentStore
    {
        public Task<IReadOnlyList<StoredDocument>> GetDocumentsAsync(Guid recordingId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> RefreshFromFileAsync(Guid recordingId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> WriteToFileAsync(Guid recordingId, MetadataDocument document, string origin, CancellationToken cancellationToken = default) =>
            Task.FromResult("ok");

        public Task<IReadOnlyList<ApplyOutcome>> ApplyAsync(Guid recordingId, MetadataDocument document, bool dryRun, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

/// <summary>Another program holding the file open is explained, and the file is left alone.</summary>
public sealed class FileOpenElsewhereTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shirabe-open-{Guid.NewGuid():N}");

    public FileOpenElsewhereTests() => Directory.CreateDirectory(Path.Combine(_root, "music"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Writing_into_a_file_another_program_has_open_says_so_and_changes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(_root, "music", "a1.flac");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "a1.flac"), path);
        var library = LocalLibrary.Open(Path.Combine(_root, "db", "library.db"));
        await library.ScanFolderAsync(Path.Combine(_root, "music"), cancellationToken: ct);
        var id = Assert.Single(await library.GetTracksAsync(ct)).RecordingId;
        var before = await File.ReadAllBytesAsync(path, ct);

        InvalidOperationException error;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) // what a player does
            error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                library.WriteToFileAsync(id, DocumentTemplates.New(id, "canonical", DateTime.UtcNow), DocumentOrigin.Embedded, ct));

        Assert.Contains("open in another program", error.Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(path, ct));
    }
}

/// <summary>The real engine and the real library: the song that is playing can be written into, and plays on afterwards.</summary>
[Collection("BASS")]
public sealed class WriteIntoPlayingSongTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shirabe-playing-{Guid.NewGuid():N}");

    public WriteIntoPlayingSongTests() => Directory.CreateDirectory(Path.Combine(_root, "music"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_document_is_written_into_the_file_that_is_playing_and_the_song_plays_on_from_the_same_place()
    {
        var ct = TestContext.Current.CancellationToken;
        try
        {
            _ = ManagedBass.Bass.Version;
        }
        catch (DllNotFoundException)
        {
            Assert.Skip("The BASS native library is missing, see native/README.md.");
        }

        var audioPath = Path.Combine(_root, "music", "c1.mp3");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "c1.mp3"), audioPath);
        var library = LocalLibrary.Open(Path.Combine(_root, "db", "library.db"));
        await library.ScanFolderAsync(Path.Combine(_root, "music"), cancellationToken: ct);
        var track = Assert.Single(await library.GetTracksAsync(ct));

        SynchronizationContext.SetSynchronizationContext(null);
        using var engine = new BassAudioPlayer(device: 0);
        var player = new PlayerViewModel(engine);
        await player.PlayAsync(track);
        engine.Seek(TimeSpan.FromSeconds(0.2));
        var before = engine.Position.TotalSeconds;
        var store = new PlayerAwareDocumentStore(library, player);
        Assert.True(MetadataDocument.TryParseFile(
            Doc(track.RecordingId), out var documents, out _));

        // Without the release, the file is open for playing and this throws "being used by another process".
        await store.WriteToFileAsync(track.RecordingId, documents[0], DocumentOrigin.Embedded, ct);

        var written = Assert.Single(await library.GetDocumentsAsync(track.RecordingId, ct), d => d.Origin == DocumentOrigin.Embedded);
        Assert.Contains("canonical", written.Layer);
        Assert.Equal(PlaybackState.Playing, engine.State);
        Assert.InRange(engine.Position.TotalSeconds, before - 0.01, 0.5);
        Assert.Null(player.ErrorMessage);
    }

    private static string Doc(Guid id) => $$"""
        { "format": "shirabe.metadata", "version": "0.1", "subject": { "type": "recording", "id": "{{id}}" },
          "layer": "canonical", "source": { "kind": "artist", "created": "2026-03-02T09:00:00Z" }, "revision": 1,
          "fields": { "title": "Written while playing" } }
        """;
}
