using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shirabe.Core.Library;
using Shirabe.Core.Metadata;
using Shirabe.Library;
using Shirabe.Metadata;
using Xunit;

namespace Shirabe.Tests;

/// <summary>Documents found with the audio file (sidecar and embedded tag), and applying documents, against a real database.</summary>
public sealed class FileDocumentsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shirabe-docs-{Guid.NewGuid():N}");
    private readonly string _music;
    private readonly string _dbPath;
    private LocalLibrary _library;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public FileDocumentsTests()
    {
        _music = Path.Combine(_root, "music");
        Directory.CreateDirectory(_music);
        _dbPath = Path.Combine(_root, "db", "library.db");
        _library = LocalLibrary.Open(_dbPath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    // ---- helpers ----

    private static string Doc(string layer, int revision = 1, string title = "A title", string field = "")
    {
        var fields = $"\"title\": \"{title}\"" + (field.Length > 0 ? $", {field}" : "");
        var definitions = field.Contains("local:mood") ? """, "definitions": { "local:mood": { "type": "text", "label": "Mood" } }""" : "";
        return $$"""
            { "format": "shirabe.metadata", "version": "0.1",
              "subject": { "type": "recording", "id": "0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55" },
              "layer": "{{layer}}", "source": { "kind": "{{(layer == "personal" ? "user" : "artist")}}", "name": "Someone", "created": "2026-03-02T09:00:00Z" },
              "revision": {{revision}}{{definitions}},
              "fields": { {{fields}} } }
            """;
    }

    private string Song(string fixture, string name)
    {
        var path = Path.Combine(_music, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), path, overwrite: true);
        return path;
    }

    private static void Sidecar(string audioPath, string json)
    {
        var path = SidecarFile.PathFor(audioPath);
        File.WriteAllText(path, json);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(Random.Shared.Next(1, 600))); // a clock that moved, even for a fast test
    }

    private static void Embed(string audioPath, string json)
    {
        var track = new ATL.Track(audioPath);
        track.AdditionalFields["SHIRABE_METADATA"] = json;
        Assert.True(track.Save());
    }

    private async Task<ScanResult> Scan() => await _library.ScanFolderAsync(_music, cancellationToken: Ct);

    private async Task<Guid> IdOf(string title) => (await _library.GetTracksAsync(Ct)).Single(t => t.Title == title).RecordingId;

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // ---- loading right away, at scan time ----

    [Fact]
    public async Task A_sidecar_beside_the_audio_is_loaded_by_the_scan()
    {
        Sidecar(Song("a1.flac", "a1.flac"), Doc("canonical", revision: 3, title: "From the sidecar"));

        var result = await Scan();

        Assert.Empty(result.DocumentProblems!);
        var stored = Assert.Single(await _library.GetDocumentsAsync(await IdOf("First Song"), Ct));
        Assert.Equal((DocumentOrigin.Sidecar, "canonical", 3, 0), (stored.Origin, stored.Layer, stored.Revision, stored.ErrorCount));
        Assert.True(stored.IsValid);
        Assert.Contains("From the sidecar", stored.Json);
    }

    [Theory]
    [InlineData("a1.flac", "First Song")]
    [InlineData("c1.mp3", "Sunrise")]
    public async Task A_metadata_tag_inside_the_audio_file_is_loaded_by_the_scan(string fixture, string title)
    {
        Embed(Song(fixture, fixture), Doc("canonical", title: "From the tag"));

        await Scan();

        var stored = Assert.Single(await _library.GetDocumentsAsync(await IdOf(title), Ct));
        Assert.Equal((DocumentOrigin.Embedded, "canonical"), (stored.Origin, stored.Layer));
        Assert.Contains("From the tag", stored.Json);
    }

    [Fact]
    public async Task Both_sources_are_loaded_the_persons_own_layer_first()
    {
        var path = Song("a1.flac", "a1.flac");
        Embed(path, Doc("canonical", title: "Embedded"));
        Sidecar(path, $"[{Doc("personal", title: "Mine")}, {Doc("community", title: "Others")}]");

        await Scan();

        var stored = await _library.GetDocumentsAsync(await IdOf("First Song"), Ct);
        Assert.Equal(
            [(DocumentOrigin.Sidecar, "personal"), (DocumentOrigin.Embedded, "canonical"), (DocumentOrigin.Sidecar, "community")],
            stored.Select(d => (d.Origin, d.Layer)));
    }

    [Fact]
    public async Task A_personal_document_that_comes_with_a_file_is_stored_but_never_merged_into_your_own_fields()
    {
        Sidecar(Song("a1.flac", "a1.flac"), Doc("personal", field: "\"local:mood\": \"someone elses mood\""));

        await Scan();

        var id = await IdOf("First Song");
        Assert.Equal("personal", Assert.Single(await _library.GetDocumentsAsync(id, Ct)).Layer);
        Assert.Empty(await _library.GetFieldsAsync(id, Ct)); // your fields are yours
        Assert.Empty(await _library.GetDefinitionsAsync(Ct));
    }

    [Fact]
    public async Task A_rescan_of_unchanged_files_changes_nothing_and_reads_nothing_again()
    {
        Sidecar(Song("a1.flac", "a1.flac"), Doc("canonical"));
        await Scan();
        var before = await _library.GetDocumentsAsync(await IdOf("First Song"), Ct);

        var again = await Scan();

        Assert.Equal((0, 1), (again.Updated, again.Unchanged));
        Assert.Equal(before.Single().LoadedUtc, (await _library.GetDocumentsAsync(await IdOf("First Song"), Ct)).Single().LoadedUtc);
    }

    [Fact]
    public async Task A_sidecar_that_changes_is_reloaded_although_the_audio_did_not_change()
    {
        var path = Song("a1.flac", "a1.flac");
        Sidecar(path, Doc("canonical", revision: 1, title: "Old"));
        await Scan();

        Sidecar(path, Doc("canonical", revision: 2, title: "New"));
        var result = await Scan();

        Assert.Equal(1, result.Updated);
        var stored = Assert.Single(await _library.GetDocumentsAsync(await IdOf("First Song"), Ct));
        Assert.Equal(2, stored.Revision);
        Assert.Contains("New", stored.Json);
    }

    [Fact]
    public async Task A_sidecar_that_appears_later_is_found_and_one_that_goes_away_is_forgotten()
    {
        var path = Song("a1.flac", "a1.flac");
        await Scan();
        var id = await IdOf("First Song");
        Assert.Empty(await _library.GetDocumentsAsync(id, Ct));

        Sidecar(path, Doc("canonical"));
        Assert.Equal(1, (await Scan()).Updated);
        Assert.Single(await _library.GetDocumentsAsync(id, Ct));

        File.Delete(SidecarFile.PathFor(path));
        Assert.Equal(1, (await Scan()).Updated);
        Assert.Empty(await _library.GetDocumentsAsync(id, Ct));
    }

    [Fact]
    public async Task Changing_the_tag_in_the_file_reloads_it_and_keeps_the_sidecar()
    {
        var path = Song("a1.flac", "a1.flac");
        Embed(path, Doc("canonical", revision: 1, title: "Tag one"));
        Sidecar(path, Doc("community", title: "Sidecar"));
        await Scan();

        Embed(path, Doc("canonical", revision: 2, title: "Tag two"));
        await Scan();

        var stored = await _library.GetDocumentsAsync(await IdOf("First Song"), Ct);
        Assert.Equal(2, stored.Single(d => d.Origin == DocumentOrigin.Embedded).Revision);
        Assert.Contains("Sidecar", stored.Single(d => d.Origin == DocumentOrigin.Sidecar).Json);
    }

    [Fact]
    public async Task A_damaged_sidecar_never_stops_the_song_from_being_scanned_and_is_reported()
    {
        var path = Song("a1.flac", "a1.flac");
        File.WriteAllText(SidecarFile.PathFor(path), "{ a hand-edited file with a typo");

        var result = await Scan();

        Assert.Equal(1, result.Added);
        Assert.Empty(result.Failed);
        var problem = Assert.Single(result.DocumentProblems!);
        Assert.Contains("is not a metadata document", problem.Reason);
        Assert.EndsWith("a1.flac", problem.Path);
        Assert.Empty(await _library.GetDocumentsAsync(await IdOf("First Song"), Ct));
    }

    [Fact]
    public async Task A_document_that_breaks_the_standard_is_kept_and_marked_not_dropped()
    {
        Sidecar(Song("a1.flac", "a1.flac"), Doc("canonical", field: "\"mood\": \"calm\""));

        await Scan();

        var stored = Assert.Single(await _library.GetDocumentsAsync(await IdOf("First Song"), Ct));
        Assert.Equal(1, stored.ErrorCount);
        Assert.False(stored.IsValid);
    }

    [Fact]
    public async Task Two_documents_of_one_layer_keep_the_newest_and_say_so()
    {
        Sidecar(Song("a1.flac", "a1.flac"), $"[{Doc("canonical", revision: 5, title: "Newest")}, {Doc("canonical", revision: 2, title: "Older")}]");

        var result = await Scan();

        var stored = Assert.Single(await _library.GetDocumentsAsync(await IdOf("First Song"), Ct));
        Assert.Equal(5, stored.Revision);
        Assert.Contains("only the newest", Assert.Single(result.DocumentProblems!).Reason);
    }

    [Fact]
    public async Task A_huge_sidecar_is_not_read()
    {
        var path = Song("a1.flac", "a1.flac");
        await using (var stream = File.Create(SidecarFile.PathFor(path))) stream.SetLength(2 * 1024 * 1024);

        var result = await Scan();

        Assert.Contains("far larger", Assert.Single(result.DocumentProblems!).Reason);
        Assert.Equal(1, result.Added);
    }

    [Fact]
    public async Task A_document_a_person_attached_survives_every_rescan()
    {
        var path = Song("a1.flac", "a1.flac");
        await Scan();
        var id = await IdOf("First Song");
        Assert.True(MetadataDocument.TryParseFile(Doc("canonical", revision: 7, title: "Kept"), out var attached, out _));
        await _library.ApplyAsync(id, attached.Single(), dryRun: false, Ct);

        Sidecar(path, Doc("canonical", revision: 1, title: "From the folder"));
        Embed(path, Doc("canonical", revision: 1, title: "From the tag"));
        await Scan();
        File.Delete(SidecarFile.PathFor(path));
        await Scan();

        var stored = await _library.GetDocumentsAsync(id, Ct);
        Assert.Equal([DocumentOrigin.Applied, DocumentOrigin.Embedded], stored.Select(d => d.Origin).Order(StringComparer.Ordinal));
        Assert.Equal(7, stored.Single(d => d.Origin == DocumentOrigin.Applied).Revision);
    }

    [Fact]
    public async Task A_library_scanned_by_an_older_version_reads_the_sidecars_at_the_next_scan()
    {
        var path = Song("a1.flac", "a1.flac");
        Sidecar(path, Doc("canonical", title: "Waiting"));
        var oldPath = Path.Combine(_root, "old", "library.db");
        Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
        var recordingId = Guid.CreateVersion7();
        var info = new FileInfo(path);
        await using (var old = new LibraryDbContext(new DbContextOptionsBuilder<LibraryDbContext>()
                         .UseSqlite(new SqliteConnectionStringBuilder { DataSource = oldPath }.ToString()).Options))
        {
            old.GetService<IMigrator>().Migrate("AddMetadataFields"); // before documents existed
            await old.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO Recordings (Id, Title, Duration, ArtworkFile) VALUES ({recordingId.ToString().ToUpperInvariant()}, 'Old Title', NULL, NULL)", Ct);
            await old.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO MediaSources (Id, RecordingId, Uri, SizeBytes, ModifiedUtcTicks, IsMissing, ScannerVersion) VALUES ({Guid.CreateVersion7().ToString().ToUpperInvariant()}, {recordingId.ToString().ToUpperInvariant()}, {new Uri(path).AbsoluteUri}, {info.Length}, {info.LastWriteTimeUtc.Ticks}, 0, 1)", Ct);
        }

        var upgraded = LocalLibrary.Open(oldPath);
        Assert.Empty(await upgraded.GetDocumentsAsync(recordingId, Ct)); // nothing yet
        var result = await upgraded.ScanFolderAsync(_music, cancellationToken: Ct);

        Assert.Equal(1, result.Updated); // the file did not change, but the old scanner never looked for documents
        Assert.Contains("Waiting", Assert.Single(await upgraded.GetDocumentsAsync(recordingId, Ct)).Json);
        Assert.Equal((0, 1), ((await upgraded.ScanFolderAsync(_music, cancellationToken: Ct)) is var again ? (again.Updated, again.Unchanged) : default));
    }

    // ---- loading when a song is opened ----

    [Fact]
    public async Task A_song_can_be_refreshed_from_its_files_without_a_scan()
    {
        var path = Song("a1.flac", "a1.flac");
        await Scan();
        var id = await IdOf("First Song");
        Assert.Empty(await _library.GetDocumentsAsync(id, Ct));

        Sidecar(path, Doc("canonical", title: "Edited by hand"));
        Embed(path, Doc("community", title: "Tag"));
        var problems = await _library.RefreshFromFileAsync(id, Ct);

        Assert.Empty(problems);
        Assert.Equal(["canonical", "community"], (await _library.GetDocumentsAsync(id, Ct)).Select(d => d.Layer));

        File.Delete(SidecarFile.PathFor(path));
        await _library.RefreshFromFileAsync(id, Ct);
        Assert.Equal([DocumentOrigin.Embedded], (await _library.GetDocumentsAsync(id, Ct)).Select(d => d.Origin));
    }

    [Fact]
    public async Task Refreshing_reports_what_cannot_be_read_and_leaves_attached_documents_alone()
    {
        var path = Song("a1.flac", "a1.flac");
        await Scan();
        var id = await IdOf("First Song");
        Assert.True(MetadataDocument.TryParseFile(Doc("canonical"), out var attached, out _));
        await _library.ApplyAsync(id, attached.Single(), dryRun: false, Ct);

        File.WriteAllText(SidecarFile.PathFor(path), "{ nope");
        var problems = await _library.RefreshFromFileAsync(id, Ct);

        Assert.Contains("is not a metadata document", Assert.Single(problems));
        Assert.Equal(DocumentOrigin.Applied, Assert.Single(await _library.GetDocumentsAsync(id, Ct)).Origin);

        File.Delete(path);
        Assert.Equal(["The file was moved or deleted."], await _library.RefreshFromFileAsync(id, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _library.RefreshFromFileAsync(Guid.NewGuid(), Ct));
    }

    [Fact]
    public async Task The_file_tag_listing_shortens_an_embedded_document_instead_of_flooding_the_dialog()
    {
        var path = Song("a1.flac", "a1.flac");
        Embed(path, Doc("canonical", title: new string('x', 2000)));
        await Scan();

        var tags = await _library.ReadFileTagsAsync(await IdOf("First Song"), Ct);

        var value = tags.Single(t => t.Name == "SHIRABE_METADATA").Value;
        Assert.True(value.Length < 400);
        Assert.Contains("characters)", value);
    }

    // ---- applying a document to a song ----

    private async Task<(Guid Id, MetadataDocument Document)> Setup(string json)
    {
        Song("a1.flac", "a1.flac");
        await Scan();
        Assert.True(MetadataDocument.TryParseFile(json, out var documents, out var error), error);
        return (await IdOf("First Song"), documents.Single());
    }

    private static string Personal(string fields, string definitions = "") => $$"""
        { "format": "shirabe.metadata", "version": "0.1",
          "subject": { "type": "recording", "id": "0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55" },
          "layer": "personal", "source": { "kind": "user", "created": "2026-11-22T18:04:00Z" }, "revision": 1
          {{(definitions.Length > 0 ? $", \"definitions\": {{ {definitions} }}" : "")}},
          "fields": { {{fields}} } }
        """;

    [Fact]
    public async Task A_dry_run_says_what_would_happen_and_changes_nothing()
    {
        var (id, document) = await Setup(Personal(
            "\"local:mood\": [\"calm\", \"late\"], \"local:bpm\": 120",
            "\"local:mood\": { \"type\": \"text\", \"multiple\": true, \"label\": \"Mood\" }"));

        var outcomes = await _library.ApplyAsync(id, document, dryRun: true, Ct);

        Assert.Equal([ApplyKind.Set, ApplyKind.Set], outcomes.Select(o => o.Kind).ToArray());
        Assert.Contains(outcomes, o => o.Text == "Set \"Mood\" to \"calm\", \"late\" (a new field).");
        Assert.Contains(outcomes, o => o.Text == "Set \"local:bpm\" to 120 (a new field).");
        Assert.Empty(await _library.GetDefinitionsAsync(Ct));
        Assert.Empty(await _library.GetFieldsAsync(id, Ct));
    }

    [Fact]
    public async Task Applying_a_personal_document_creates_the_fields_and_their_definitions()
    {
        var (id, document) = await Setup(Personal(
            "\"local:mood\": [\"calm\", \"late\"], \"local:bpm\": 120, \"local:live\": true",
            "\"local:mood\": { \"type\": \"text\", \"multiple\": true, \"label\": \"Mood\" }"));

        await _library.ApplyAsync(id, document, dryRun: false, Ct);

        var fields = (await _library.GetFieldsAsync(id, Ct)).ToDictionary(f => f.Definition.Key);
        Assert.Equal("""["calm","late"]""", fields["local:mood"].Value.GetRawText());
        Assert.Equal("Mood", fields["local:mood"].Definition.Label);
        Assert.Equal(FieldType.Number, fields["local:bpm"].Definition.Type); // inferred: the document gave no definition
        Assert.Equal(FieldType.Boolean, fields["local:live"].Definition.Type);
    }

    [Fact]
    public async Task A_value_you_already_have_is_replaced_and_the_sentence_says_from_what_to_what()
    {
        var (id, document) = await Setup(Personal("\"local:mood\": \"tense\""));
        await _library.SaveDefinitionAsync(new FieldDefinition("local:mood", FieldType.Text, Label: "My mood"), Ct);
        await _library.SetFieldAsync(id, "local:mood", Json("\"calm\""), Ct);

        var outcomes = await _library.ApplyAsync(id, document, dryRun: false, Ct);

        Assert.Equal("Replace \"My mood\": \"calm\" becomes \"tense\".", Assert.Single(outcomes).Text);
        var field = Assert.Single(await _library.GetFieldsAsync(id, Ct));
        Assert.Equal("tense", field.Value.GetString());
        Assert.Equal("My mood", field.Definition.Label); // your own label stays
    }

    [Fact]
    public async Task What_cannot_be_applied_is_skipped_with_the_reason_and_the_rest_goes_ahead()
    {
        var (id, document) = await Setup(Personal(
            "\"title\": \"A core field\", \"local:bpm\": \"fast\", \"local:same\": \"x\", \"local:odd\": { \"deep\": 1 }, \"local:good\": \"yes\"",
            "\"local:bpm\": { \"type\": \"number\" }"));
        // the value "fast" is not a number, so the document itself breaks the standard: fix that part and keep the rest of the case
        var node = System.Text.Json.Nodes.JsonNode.Parse(document.ToJson())!.AsObject();
        node["fields"]!["local:bpm"] = 100;
        Assert.True(MetadataDocument.TryParseFile(node.ToJsonString(), out var fixedDocument, out _));
        await _library.SaveDefinitionAsync(new FieldDefinition("local:bpm", FieldType.Text), Ct);   // here it is text, in the document a number
        await _library.SaveDefinitionAsync(new FieldDefinition("local:same", FieldType.Text), Ct);
        await _library.SetFieldAsync(id, "local:same", Json("\"x\""), Ct);

        var outcomes = await _library.ApplyAsync(id, fixedDocument.Single(), dryRun: false, Ct);

        Assert.Contains(outcomes, o => o.Kind == ApplyKind.Skip && o.Text.Contains("\"title\" is a core field"));
        Assert.Contains(outcomes, o => o.Kind == ApplyKind.Skip && o.Text.Contains("\"local:bpm\" is a text here, but a number in the document"));
        Assert.Contains(outcomes, o => o.Kind == ApplyKind.Skip && o.Text.Contains("\"local:same\" already has this value"));
        Assert.Contains(outcomes, o => o.Kind == ApplyKind.Skip && o.Text.Contains("\"local:odd\" has no definition"));
        Assert.Contains(outcomes, o => o.Kind == ApplyKind.Set && o.Text.Contains("local:good"));
        Assert.Equal(["local:good", "local:same"], (await _library.GetFieldsAsync(id, Ct)).Select(f => f.Definition.Key).Order());
    }

    [Fact]
    public async Task A_document_that_breaks_the_standard_is_refused_before_anything_is_written()
    {
        var (id, document) = await Setup(Personal("\"mood\": \"calm\"")); // a field without a namespace that is not core

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _library.ApplyAsync(id, document, dryRun: false, Ct));

        Assert.Contains("does not follow the standard", ex.Message);
        Assert.Contains("fields.mood", ex.Message);
        Assert.Empty(await _library.GetDefinitionsAsync(Ct));
    }

    [Fact]
    public async Task Applying_to_a_song_that_is_not_in_the_library_is_refused()
    {
        var (_, document) = await Setup(Personal("\"local:mood\": \"calm\""));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _library.ApplyAsync(Guid.NewGuid(), document, dryRun: false, Ct));
    }

    [Fact]
    public async Task Applying_the_same_personal_document_twice_changes_nothing_the_second_time()
    {
        var (id, document) = await Setup(Personal("\"local:mood\": \"calm\""));
        await _library.ApplyAsync(id, document, dryRun: false, Ct);

        var second = await _library.ApplyAsync(id, document, dryRun: true, Ct);

        Assert.Equal(ApplyKind.Skip, Assert.Single(second).Kind);
        Assert.Contains("already has this value", second[0].Text);
    }

    [Fact]
    public async Task An_artists_document_is_kept_whole_as_another_layer_and_a_newer_one_replaces_it()
    {
        var (id, first) = await Setup(Doc("canonical", revision: 1, title: "Version one"));
        Assert.True(MetadataDocument.TryParseFile(Doc("canonical", revision: 2, title: "Version two"), out var second, out _));

        var preview = await _library.ApplyAsync(id, first, dryRun: true, Ct);
        Assert.Equal(ApplyKind.Keep, Assert.Single(preview).Kind);
        Assert.Contains("Keep this canonical document (artist, Someone, revision 1)", preview[0].Text);
        Assert.Contains("do not change the Songs list yet", preview[0].Text); // honest about what it does today
        Assert.Empty(await _library.GetDocumentsAsync(id, Ct));

        await _library.ApplyAsync(id, first, dryRun: false, Ct);
        Assert.Contains("replaces the one kept before (revision 1)", (await _library.ApplyAsync(id, second.Single(), dryRun: true, Ct))[0].Text);
        await _library.ApplyAsync(id, second.Single(), dryRun: false, Ct);

        var stored = Assert.Single(await _library.GetDocumentsAsync(id, Ct));
        Assert.Equal((DocumentOrigin.Applied, "canonical", 2), (stored.Origin, stored.Layer, stored.Revision));
        Assert.Contains("Version two", stored.Json);
        Assert.Empty(await _library.GetFieldsAsync(id, Ct)); // an artist's document never becomes your own fields
    }
}
