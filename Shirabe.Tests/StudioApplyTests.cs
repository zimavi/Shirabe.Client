using Shirabe.Client.Studio;
using Shirabe.Core.Library;
using Shirabe.Metadata;

using Xunit;

namespace Shirabe.Tests;

public sealed class StudioApplyTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"shirabe-studio-apply-{Guid.NewGuid():N}");
    private readonly FakeDocumentStore _store = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakeLibrary _library = new();

    public StudioApplyTests()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static TrackSummary Track(string title, string? artist = "Artist", string? album = "Album",
        Guid? id = null) =>
        new(id ?? Guid.NewGuid(), title, artist, album, null, $"file:///C:/music/{title}.flac");

    private static MetadataDocument Document(string subjectId = "0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55",
        string layer = "personal") =>
        ApplyViewModelTests.Parse($$"""
                                    { "format": "shirabe.metadata", "version": "0.1",
                                      "subject": { "type": "recording", "id": "{{subjectId}}" },
                                      "layer": "{{layer}}", "source": { "kind": "user", "created": "2026-11-22T18:04:00Z" }, "revision": 1,
                                      "fields": { "local:mood": "calm" } }
                                    """);

    // ---- the song picker ----

    [Fact]
    public void The_list_narrows_as_you_type_across_title_artist_and_album_in_any_case()
    {
        var songs = new[]
        {
            Track("Poker Face", "Lady Gaga", "The Fame"), Track("Bad Romance", "Lady Gaga", "The Fame Monster"),
            Track("Силуэты", "Группа", "Альбом"),
        };
        var dialog = new StudioApplyViewModel(_store, songs, Document(), "Doc");
        Assert.Equal(3, dialog.Songs.Count);

        dialog.Filter = "gaga";
        Assert.Equal(["Poker Face", "Bad Romance"], dialog.Songs.Select(t => t.Title));

        dialog.Filter = "lady monster"; // every word has to match, in any field
        Assert.Equal(["Bad Romance"], dialog.Songs.Select(t => t.Title));

        dialog.Filter = "силу";
        Assert.Equal(["Силуэты"], dialog.Songs.Select(t => t.Title));

        dialog.Filter = "   ";
        Assert.Equal(3, dialog.Songs.Count);
    }

    [Fact]
    public void A_long_library_is_listed_only_up_to_a_limit_until_you_type()
    {
        var songs = Enumerable.Range(1, 500).Select(i => Track($"Song {i}")).ToList();

        var dialog = new StudioApplyViewModel(_store, songs, Document(), "Doc");

        Assert.Equal(300, dialog.Songs.Count);
        dialog.Filter = "Song 499";
        Assert.Equal("Song 499", Assert.Single(dialog.Songs).Title);
    }

    [Fact]
    public async Task A_document_that_names_a_song_of_this_library_preselects_it_and_previews_at_once()
    {
        var mine = Track("The one it is about");
        var dialog = new StudioApplyViewModel(_store, [Track("Other"), mine],
            Document(subjectId: mine.RecordingId.ToString()), "Doc");

        await dialog.PreviewLoading;

        Assert.Same(mine, dialog.SelectedSong);
        Assert.Equal("This document says it is about this song.", dialog.Hint);
        Assert.NotNull(dialog.Preview);
        Assert.Equal([(mine.RecordingId, true)], _store.ApplyCalls);
    }

    [Fact]
    public void A_document_about_a_song_this_library_does_not_know_preselects_nothing()
    {
        var dialog = new StudioApplyViewModel(_store, [Track("A"), Track("B")], Document(), "Doc");

        Assert.Null(dialog.SelectedSong);
        Assert.Null(dialog.Preview);
        Assert.Null(dialog.Hint);
        Assert.Empty(_store.ApplyCalls);
    }

    [Fact]
    public async Task Choosing_a_song_previews_and_choosing_another_replaces_the_preview_and_clearing_removes_it()
    {
        var first = Track("First");
        var second = Track("Second");
        var dialog = new StudioApplyViewModel(_store, [first, second], Document(), "Doc");

        dialog.SelectedSong = first;
        await dialog.PreviewLoading;
        var firstPreview = dialog.Preview;
        Assert.Equal("Apply to \"First\"", firstPreview!.Heading);

        dialog.SelectedSong = second;
        await dialog.PreviewLoading;
        Assert.NotSame(firstPreview, dialog.Preview);
        Assert.Equal("Apply to \"Second\"", dialog.Preview!.Heading);
        Assert.Equal([(first.RecordingId, true), (second.RecordingId, true)], _store.ApplyCalls); // previews only

        dialog.SelectedSong = null;
        Assert.Null(dialog.Preview);
    }

    [Fact]
    public async Task Applying_from_the_preview_writes_and_closing_the_preview_closes_the_whole_dialog()
    {
        var song = Track("Song");
        var dialog = new StudioApplyViewModel(_store, [song], Document(), "Doc");
        var closed = 0;
        dialog.Closed += (_, _) => closed++;
        dialog.SelectedSong = song;
        await dialog.PreviewLoading;

        await dialog.Preview!.ApplyCommand.ExecuteAsync(null);
        Assert.Equal([(song.RecordingId, true), (song.RecordingId, false)], _store.ApplyCalls);

        dialog.Preview.CloseCommand.Execute(null);
        Assert.Equal(1, closed);
    }

    // ---- in Studio ----

    private StudioViewModel Studio(bool withServices = true) =>
        withServices ? new StudioViewModel(_picker, _library, _store) : new StudioViewModel(_picker);

    private string Write(string json)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.shirabe.json");
        File.WriteAllText(path, json);
        return path;
    }

    private const string Valid = """
                                 { "format": "shirabe.metadata", "version": "0.1",
                                   "subject": { "type": "recording", "id": "0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55" },
                                   "layer": "personal", "source": { "kind": "user", "created": "2026-11-22T18:04:00Z" }, "revision": 1,
                                   "fields": { "local:mood": "calm" } }
                                 """;

    [Fact]
    public async Task A_valid_document_can_be_applied_when_studio_has_a_library_and_not_otherwise()
    {
        var withLibrary = Studio();
        await withLibrary.OpenPathAsync(Write(Valid));
        var withoutLibrary = Studio(withServices: false);
        await withoutLibrary.OpenPathAsync(Write(Valid));

        Assert.True(withLibrary.Documents[0].CanApply);
        Assert.False(withoutLibrary.Documents[0].CanApply);
    }

    [Fact]
    public async Task A_document_that_breaks_the_standard_cannot_be_applied()
    {
        var studio = Studio();
        await studio.OpenPathAsync(Write(Valid.Replace("\"revision\": 1", "\"revision\": 0")));

        Assert.False(studio.Documents[0].CanApply);
        await studio.ApplyToSongCommand.ExecuteAsync(studio.Documents[0]);
        Assert.Null(studio.Applying);
    }

    [Fact]
    public async Task Apply_to_a_song_opens_the_picker_with_the_librarys_songs_and_closing_it_clears_it()
    {
        _library.Tracks = [Track("One"), Track("Two")];
        var studio = Studio();
        await studio.OpenPathAsync(Write(Valid));

        await studio.ApplyToSongCommand.ExecuteAsync(studio.Documents[0]);

        Assert.NotNull(studio.Applying);
        Assert.Equal(["One", "Two"], studio.Applying.Songs.Select(t => t.Title));

        studio.Applying.CloseCommand.Execute(null);
        Assert.Null(studio.Applying);
    }

    [Fact]
    public async Task A_library_that_cannot_be_read_is_reported()
    {
        _library.Tracks = null!; // the fake throws when asked
        var studio = new StudioViewModel(_picker, new ThrowingLibrary(), _store);
        await studio.OpenPathAsync(Write(Valid));

        await studio.ApplyToSongCommand.ExecuteAsync(studio.Documents[0]);

        Assert.Null(studio.Applying);
        Assert.Contains("Could not load the library", studio.Error);
    }

    private sealed class ThrowingLibrary : ILibrary
    {
        public Task<IReadOnlyList<TrackSummary>> GetTracksAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("database is locked");

        public Task<IReadOnlyList<AlbumSummary>> GetAlbumsAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("database is locked");
    }
}