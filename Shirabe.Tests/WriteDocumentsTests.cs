using Microsoft.Data.Sqlite;
using Shirabe.Core.Metadata;
using Shirabe.Library;
using Shirabe.Metadata;
using Xunit;

namespace Shirabe.Tests;

/// <summary>Writing a document into a song's sidecar file or into its audio file, and getting the original back when that goes wrong.</summary>
public sealed class WriteDocumentsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shirabe-write-{Guid.NewGuid():N}");
    private readonly string _music;
    private readonly LocalLibrary _library;
    private readonly HashSet<string> _backupsBefore;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public WriteDocumentsTests()
    {
        _music = Path.Combine(_root, "music");
        Directory.CreateDirectory(_music);
        _library = LocalLibrary.Open(Path.Combine(_root, "db", "library.db"));
        _backupsBefore = [.. Directory.GetFiles(Path.GetTempPath(), "shirabe-backup-*")];
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal); // a test may have made one read-only
        Directory.Delete(_root, recursive: true);
    }

    private async Task<(Guid Id, string Path)> Song(string fixture, string title)
    {
        var path = Path.Combine(_music, fixture);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), path, overwrite: true);
        await _library.ScanFolderAsync(_music, cancellationToken: Ct);
        return ((await _library.GetTracksAsync(Ct)).Single(t => t.Title == title).RecordingId, path);
    }

    private static MetadataDocument Doc(string layer, string title, int revision = 1)
    {
        var document = DocumentTemplates.New(Guid.Parse("0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55"), layer, new DateTime(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc), title: title);
        document.Root["revision"] = revision;
        document.Root["fields"]!["title"] = title; // a personal template starts empty; the tests want to tell documents apart
        return document;
    }

    private static IReadOnlyList<MetadataDocument> Read(string json)
    {
        Assert.True(MetadataDocument.TryParseFile(json, out var documents, out var error), error);
        return documents;
    }

    private IEnumerable<string> LeftoverBackups() => Directory.GetFiles(Path.GetTempPath(), "shirabe-backup-*").Where(f => !_backupsBefore.Contains(f));

    [Fact]
    public async Task A_file_without_a_title_tag_does_not_get_its_file_name_as_one()
    {
        var path = Path.Combine(_music, "Untitled take.wav");
        TestAudio.WriteWav(path, 1);
        var (tagged, _) = await Song("cover-a1.flac", "Covered One"); // the scan picks up both files
        var untitled = (await _library.GetTracksAsync(Ct)).Single(t => t.Title == "Untitled take").RecordingId;
        async Task<bool> HasTitleTag(Guid id) => (await _library.ReadFileTagsAsync(id, Ct))
            .Any(t => t is { Group: FileTag.TagsGroup, Name: "Title" });

        Assert.False(await HasTitleTag(untitled));
        Assert.True(await HasTitleTag(tagged));

        await _library.WriteToFileAsync(untitled, Doc("canonical", "Document title"), DocumentOrigin.Embedded, Ct);

        var bytes = await File.ReadAllBytesAsync(path, Ct);
        Assert.True(bytes.AsSpan().IndexOf("SHIRABE_METADATA"u8) >= 0);
        Assert.True(bytes.AsSpan().IndexOf("Untitled take"u8) < 0);
        Assert.True(bytes.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes("Untitled take")) < 0);
        Assert.False(await HasTitleTag(untitled));
    }

    // ---- the sidecar ----

    [Fact]
    public async Task A_sidecar_is_written_beside_the_audio_and_the_song_shows_it_at_once()
    {
        var (id, path) = await Song("a1.flac", "First Song");

        var message = await _library.WriteToFileAsync(id, Doc("canonical", "Mine"), DocumentOrigin.Sidecar, Ct);

        Assert.Equal("Saved to a1.flac.shirabe.json, beside the audio file.", message);
        Assert.Equal("Mine", Assert.Single(Read(File.ReadAllText(SidecarFile.PathFor(path)))).Title);
        var stored = Assert.Single(await _library.GetDocumentsAsync(id, Ct));
        Assert.Equal((DocumentOrigin.Sidecar, "canonical"), (stored.Origin, stored.Layer));
        Assert.Empty(Directory.GetFiles(_music, "*.tmp"));
    }

    [Fact]
    public async Task Other_layers_in_the_sidecar_are_kept_and_the_same_layer_is_replaced_with_a_higher_revision()
    {
        var (id, path) = await Song("a1.flac", "First Song");
        await _library.WriteToFileAsync(id, Doc("canonical", "The artists"), DocumentOrigin.Sidecar, Ct);
        await _library.WriteToFileAsync(id, Doc("personal", "Mine"), DocumentOrigin.Sidecar, Ct);

        await _library.WriteToFileAsync(id, Doc("personal", "Mine, edited"), DocumentOrigin.Sidecar, Ct);

        var documents = Read(File.ReadAllText(SidecarFile.PathFor(path)));
        Assert.Equal(["canonical", "personal"], documents.Select(d => d.Layer));
        Assert.Equal("The artists", documents[0].Title);
        Assert.Equal(("Mine, edited", 2), (documents[1].Title, documents[1].Revision));
    }

    [Fact]
    public async Task A_sidecar_that_cannot_be_read_is_left_exactly_as_it_is()
    {
        var (id, path) = await Song("a1.flac", "First Song");
        File.WriteAllText(SidecarFile.PathFor(path), "{ a hand-edited file with a typo");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _library.WriteToFileAsync(id, Doc("personal", "Mine"), DocumentOrigin.Sidecar, Ct));

        Assert.Contains("left alone", ex.Message);
        Assert.Equal("{ a hand-edited file with a typo", File.ReadAllText(SidecarFile.PathFor(path)));
    }

    // ---- into the audio file ----

    [Theory]
    [InlineData("a1.flac", "First Song")]
    [InlineData("c1.mp3", "Sunrise")]
    [InlineData("cover-a1.flac", "Covered One")]
    public async Task A_tag_is_written_into_the_audio_file_and_nothing_else_about_the_song_changes(string fixture, string title)
    {
        var (id, path) = await Song(fixture, title);
        var before = (await _library.ReadFileTagsAsync(id, Ct)).Where(t => t.Name != "SHIRABE_METADATA").ToList();

        var message = await _library.WriteToFileAsync(id, Doc("canonical", "Embedded title"), DocumentOrigin.Embedded, Ct);

        Assert.Equal($"Written into the audio file {fixture}.", message);
        var after = (await _library.ReadFileTagsAsync(id, Ct)).Where(t => t.Name != "SHIRABE_METADATA").ToList();
        Assert.Equal(before, after); // title, artist, pictures, audio facts: all as they were
        var stored = Assert.Single(await _library.GetDocumentsAsync(id, Ct));
        Assert.Equal((DocumentOrigin.Embedded, "canonical"), (stored.Origin, stored.Layer));
        Assert.Contains("Embedded title", stored.Json);
        Assert.Empty(LeftoverBackups());
        Assert.True(new ATL.Track(path).DurationMs > 0);
    }

    [Fact]
    public async Task Documents_of_other_layers_already_in_the_tag_are_kept_and_the_same_layer_is_replaced()
    {
        var (id, _) = await Song("a1.flac", "First Song");
        await _library.WriteToFileAsync(id, Doc("canonical", "The artists"), DocumentOrigin.Embedded, Ct);
        await _library.WriteToFileAsync(id, Doc("personal", "Mine"), DocumentOrigin.Embedded, Ct);

        await _library.WriteToFileAsync(id, Doc("canonical", "The artists, corrected"), DocumentOrigin.Embedded, Ct);

        var stored = await _library.GetDocumentsAsync(id, Ct);
        Assert.Equal(["personal", "canonical"], stored.Select(d => d.Layer));
        var canonical = stored.Single(d => d.Layer == "canonical");
        Assert.Equal(2, canonical.Revision);
        Assert.Contains("corrected", canonical.Json);
    }

    [Fact]
    public async Task A_file_that_cannot_be_written_is_left_as_it_was_and_no_temporary_copy_remains()
    {
        var (id, path) = await Song("a1.flac", "First Song");
        var original = await File.ReadAllBytesAsync(path, Ct);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _library.WriteToFileAsync(id, Doc("canonical", "x"), DocumentOrigin.Embedded, Ct));

        Assert.Contains("left as it was", ex.Message);
        Assert.Equal(original, await File.ReadAllBytesAsync(path, Ct));
        Assert.Empty(LeftoverBackups());
        Assert.Empty(await _library.GetDocumentsAsync(id, Ct));
    }

    [Fact]
    public async Task A_tag_that_is_already_there_but_cannot_be_read_stops_the_write()
    {
        var (id, path) = await Song("a1.flac", "First Song");
        var track = new ATL.Track(path);
        track.AdditionalFields["SHIRABE_METADATA"] = "{ not json";
        Assert.True(track.Save());
        var original = await File.ReadAllBytesAsync(path, Ct);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _library.WriteToFileAsync(id, Doc("personal", "x"), DocumentOrigin.Embedded, Ct));

        Assert.Contains("left alone", ex.Message);
        Assert.Equal(original, await File.ReadAllBytesAsync(path, Ct)); // not even rewritten
        Assert.Empty(LeftoverBackups());
    }

    // ---- refusals ----

    [Fact]
    public async Task A_document_that_breaks_the_standard_is_refused_before_anything_is_touched()
    {
        var (id, path) = await Song("a1.flac", "First Song");
        var broken = Doc("personal", "x");
        broken.Root["layer"] = "private";

        await Assert.ThrowsAsync<ArgumentException>(() => _library.WriteToFileAsync(id, broken, DocumentOrigin.Sidecar, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => _library.WriteToFileAsync(id, broken, DocumentOrigin.Embedded, Ct));

        Assert.False(File.Exists(SidecarFile.PathFor(path)));
    }

    [Fact]
    public async Task Documents_can_only_be_written_to_the_sidecar_or_into_the_file()
    {
        var (id, _) = await Song("a1.flac", "First Song");

        await Assert.ThrowsAsync<ArgumentException>(() => _library.WriteToFileAsync(id, Doc("personal", "x"), DocumentOrigin.Applied, Ct));
    }

    [Fact]
    public async Task A_song_that_is_not_in_the_library_or_whose_file_is_gone_is_refused()
    {
        var (id, path) = await Song("a1.flac", "First Song");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _library.WriteToFileAsync(Guid.NewGuid(), Doc("personal", "x"), DocumentOrigin.Sidecar, Ct));

        File.Delete(path);

        await Assert.ThrowsAsync<FileNotFoundException>(() => _library.WriteToFileAsync(id, Doc("personal", "x"), DocumentOrigin.Sidecar, Ct));
    }
}
