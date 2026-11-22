using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shirabe.Core.Metadata;
using Shirabe.Metadata;
using Shirabe.Library;
using Xunit;

namespace Shirabe.Tests;

/// <summary>The custom fields in <see cref="LocalLibrary"/>, against a real SQLite database.</summary>
public class MetadataStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shirabe-meta-{Guid.NewGuid():N}");
    private readonly string _music;
    private readonly string _dbPath;
    private readonly LocalLibrary _library;
    private readonly Guid _song;
    private readonly Guid _otherSong;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public MetadataStoreTests()
    {
        _music = Path.Combine(_root, "music");
        Directory.CreateDirectory(_music);
        _dbPath = Path.Combine(_root, "db", "library.db");
        Fixture("a1.flac");
        Fixture("b1.flac");
        _library = LocalLibrary.Open(_dbPath);
        _library.ScanFolderAsync(_music, cancellationToken: Ct).GetAwaiter().GetResult();
        var tracks = _library.GetTracksAsync(Ct).GetAwaiter().GetResult();
        _song = tracks.Single(t => t.Title == "First Song").RecordingId;
        _otherSong = tracks.Single(t => t.Title == "Lonely Single").RecordingId;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); // SQLite keeps the file open otherwise
        Directory.Delete(_root, recursive: true);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private Task Define(string key, FieldType type, bool multiple = false, string? label = null) =>
        _library.SaveDefinitionAsync(new FieldDefinition(key, type, multiple, label), Ct);

    [Fact]
    public async Task Definitions_are_listed_by_name_and_can_be_updated()
    {
        await Define("local:mood", FieldType.Text, multiple: true, label: "  Mood ");
        await Define("example.com:bpm", FieldType.Number);

        await _library.SaveDefinitionAsync(new FieldDefinition("local:mood", FieldType.Text, true, "Feeling", "How it feels"), Ct);

        var definitions = await _library.GetDefinitionsAsync(Ct);
        Assert.Equal(["example.com:bpm", "local:mood"], definitions.Select(d => d.Key));
        Assert.Equal(new FieldDefinition("local:mood", FieldType.Text, true, "Feeling", "How it feels"), definitions[1]);
        Assert.Null(definitions[0].Label);
    }

    [Fact]
    public async Task A_blank_label_is_stored_as_none_and_a_label_is_trimmed()
    {
        await Define("local:a", FieldType.Text, label: "   ");
        await Define("local:b", FieldType.Text, label: "  Name  ");

        var definitions = await _library.GetDefinitionsAsync(Ct);
        Assert.Null(definitions.Single(d => d.Key == "local:a").Label);
        Assert.Equal("Name", definitions.Single(d => d.Key == "local:b").Label);
    }

    [Theory]
    [InlineData("title")]            // a core field of the standard, not a custom field
    [InlineData("mood")]
    [InlineData("Local:Mood")]
    [InlineData("example:mood")]
    public async Task Only_well_formed_namespaced_names_can_be_defined_or_set(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Define(key, FieldType.Text));
        await Assert.ThrowsAsync<ArgumentException>(() => _library.SetFieldAsync(_song, key, Json("\"x\""), Ct));
    }

    [Fact]
    public async Task Values_of_every_type_come_back_as_they_were_set()
    {
        await Define("local:mood", FieldType.Text, multiple: true);
        await Define("local:bpm", FieldType.Number);
        await Define("local:live", FieldType.Boolean);
        await Define("local:heard", FieldType.Date);
        await Define("local:source", FieldType.Link);

        await _library.SetFieldAsync(_song, "local:mood", Json("""["late night", "driving"]"""), Ct);
        await _library.SetFieldAsync(_song, "local:bpm", Json("128.5"), Ct);
        await _library.SetFieldAsync(_song, "local:live", Json("true"), Ct);
        await _library.SetFieldAsync(_song, "local:heard", Json("\"2026-11\""), Ct);
        await _library.SetFieldAsync(_song, "local:source", Json("""{ "url": "https://example.com/a", "label": "Where" }"""), Ct);

        var fields = await _library.GetFieldsAsync(_song, Ct);

        Assert.Equal(["local:bpm", "local:heard", "local:live", "local:mood", "local:source"], fields.Select(f => f.Definition.Key));
        string Raw(string key) => fields.Single(f => f.Definition.Key == key).Value.GetRawText();
        Assert.Equal("""["late night","driving"]""", Raw("local:mood"));
        Assert.Equal("128.5", Raw("local:bpm"));
        Assert.Equal("true", Raw("local:live"));
        Assert.Equal("\"2026-11\"", Raw("local:heard"));
        Assert.Equal("""{"url":"https://example.com/a","label":"Where"}""", Raw("local:source"));
        Assert.Equal(FieldType.Number, fields.Single(f => f.Definition.Key == "local:bpm").Definition.Type);
    }

    [Fact]
    public async Task Setting_a_field_again_replaces_its_value()
    {
        await Define("local:mood", FieldType.Text);
        await _library.SetFieldAsync(_song, "local:mood", Json("\"calm\""), Ct);

        await _library.SetFieldAsync(_song, "local:mood", Json("\"tense\""), Ct);

        var field = Assert.Single(await _library.GetFieldsAsync(_song, Ct));
        Assert.Equal("tense", field.Value.GetString());
    }

    [Fact]
    public async Task Fields_belong_to_one_recording()
    {
        await Define("local:mood", FieldType.Text);
        await _library.SetFieldAsync(_song, "local:mood", Json("\"calm\""), Ct);

        Assert.Empty(await _library.GetFieldsAsync(_otherSong, Ct));

        await _library.SetFieldAsync(_otherSong, "local:mood", Json("\"wild\""), Ct);
        Assert.Equal("calm", (await _library.GetFieldsAsync(_song, Ct)).Single().Value.GetString());
        Assert.Equal("wild", (await _library.GetFieldsAsync(_otherSong, Ct)).Single().Value.GetString());
    }

    [Fact]
    public async Task A_field_needs_a_definition_and_a_value_that_fits_it()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _library.SetFieldAsync(_song, "local:mood", Json("\"x\""), Ct));

        await Define("local:bpm", FieldType.Number);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _library.SetFieldAsync(_song, "local:bpm", Json("\"fast\""), Ct));
        Assert.Contains("local:bpm", ex.Message);
        Assert.Contains("number", ex.Message);
        Assert.Empty(await _library.GetFieldsAsync(_song, Ct)); // a refused value changes nothing
    }

    [Fact]
    public async Task A_file_link_is_accepted_in_the_personal_layer()
    {
        await Define("local:cover", FieldType.Link);

        await _library.SetFieldAsync(_song, "local:cover", Json("""{ "url": "file:///C:/Music/cover.jpg" }"""), Ct);

        Assert.Single(await _library.GetFieldsAsync(_song, Ct));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _library.SetFieldAsync(_song, "local:cover", Json("""{ "url": "http://example.com/cover.jpg" }"""), Ct));
    }

    [Fact]
    public async Task A_song_that_is_not_in_the_library_is_refused_with_a_clear_error()
    {
        await Define("local:mood", FieldType.Text);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _library.SetFieldAsync(Guid.NewGuid(), "local:mood", Json("\"x\""), Ct));

        Assert.Contains("not in the library", ex.Message);
    }

    [Fact]
    public async Task Removing_a_field_removes_only_that_value_and_removing_nothing_is_fine()
    {
        await Define("local:a", FieldType.Text);
        await Define("local:b", FieldType.Text);
        await _library.SetFieldAsync(_song, "local:a", Json("\"1\""), Ct);
        await _library.SetFieldAsync(_song, "local:b", Json("\"2\""), Ct);

        await _library.RemoveFieldAsync(_song, "local:a", Ct);
        await _library.RemoveFieldAsync(_song, "local:a", Ct);          // already gone
        await _library.RemoveFieldAsync(_song, "local:never-set", Ct);

        Assert.Equal(["local:b"], (await _library.GetFieldsAsync(_song, Ct)).Select(f => f.Definition.Key));
        Assert.Single(await _library.GetDefinitionsAsync(Ct), d => d.Key == "local:a"); // the definition stays
    }

    [Fact]
    public async Task The_type_of_a_field_with_values_cannot_change_but_its_label_can()
    {
        await Define("local:bpm", FieldType.Number);
        await _library.SetFieldAsync(_song, "local:bpm", Json("120"), Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Define("local:bpm", FieldType.Text));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Define("local:bpm", FieldType.Number, multiple: true));
        await Define("local:bpm", FieldType.Number, label: "Tempo");

        Assert.Equal("Tempo", (await _library.GetFieldsAsync(_song, Ct)).Single().Definition.Label);
        Assert.Equal("120", (await _library.GetFieldsAsync(_song, Ct)).Single().Value.GetRawText()); // untouched

        await _library.RemoveFieldAsync(_song, "local:bpm", Ct);
        await Define("local:bpm", FieldType.Text); // nobody has a value any more: the type may change
        Assert.Equal(FieldType.Text, Assert.Single(await _library.GetDefinitionsAsync(Ct)).Type);
    }

    [Fact]
    public async Task A_definition_that_has_values_cannot_be_removed_until_they_are()
    {
        await Define("local:mood", FieldType.Text);
        await _library.SetFieldAsync(_song, "local:mood", Json("\"calm\""), Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _library.RemoveDefinitionAsync("local:mood", Ct));
        Assert.Single(await _library.GetFieldsAsync(_song, Ct)); // nothing was lost

        await _library.RemoveFieldAsync(_song, "local:mood", Ct);
        await _library.RemoveDefinitionAsync("local:mood", Ct);
        await _library.RemoveDefinitionAsync("local:mood", Ct); // and removing it twice is fine

        Assert.Empty(await _library.GetDefinitionsAsync(Ct));
    }

    [Fact]
    public async Task Values_are_stored_compactly_whatever_whitespace_they_came_with()
    {
        await Define("local:mood", FieldType.Text, multiple: true);

        await _library.SetFieldAsync(_song, "local:mood", Json("[ \"a\" ,\n \"b\" ]"), Ct);

        await using var db = OpenDb();
        Assert.Equal("""["a","b"]""", await db.Database.SqlQueryRaw<string>("SELECT ValueJson AS Value FROM MetadataEntries").SingleAsync(Ct));
    }

    [Fact]
    public async Task A_rescan_of_a_changed_file_never_touches_custom_fields()
    {
        await Define("local:mood", FieldType.Text);
        await _library.SetFieldAsync(_song, "local:mood", Json("\"calm\""), Ct);

        Fixture("a1-retagged.flac", "a1.flac"); // same path, different tags: the scanner rewrites its own tables
        var result = await _library.ScanFolderAsync(_music, cancellationToken: Ct);

        Assert.Equal(1, result.Updated);
        var tracks = await _library.GetTracksAsync(Ct);
        Assert.Contains(tracks, t => t.RecordingId == _song); // same recording, same id
        Assert.Equal("calm", (await _library.GetFieldsAsync(_song, Ct)).Single().Value.GetString());
    }

    [Fact]
    public async Task A_recording_with_custom_fields_cannot_be_deleted_by_accident()
    {
        await Define("local:mood", FieldType.Text);
        await _library.SetFieldAsync(_song, "local:mood", Json("\"calm\""), Ct);

        await using var db = OpenDb();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;", Ct);
        var id = _song.ToString().ToUpperInvariant();
        // Remove everything else that points at the recording, so the custom field is the only thing in the way.
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ArtistCredits WHERE RecordingId = {id}", Ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ReleaseTracks WHERE RecordingId = {id}", Ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM MediaSources WHERE RecordingId = {id}", Ct);

        await Assert.ThrowsAnyAsync<Exception>(() => db.Recordings.Where(r => r.Id == _song).ExecuteDeleteAsync(Ct));

        await _library.RemoveFieldAsync(_song, "local:mood", Ct); // with the field gone, the same delete works
        Assert.Equal(1, await db.Recordings.Where(r => r.Id == _song).ExecuteDeleteAsync(Ct));
    }

    [Fact]
    public async Task A_library_from_before_custom_fields_upgrades_keeps_its_songs_and_gets_the_feature()
    {
        var oldPath = Path.Combine(_root, "old", "library.db");
        Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
        var recordingId = Guid.CreateVersion7();
        var options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = oldPath }.ToString()).Options;
        await using (var old = new LibraryDbContext(options))
        {
            old.GetService<IMigrator>().Migrate("AddArtwork"); // the schema before custom fields existed
            await old.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO Recordings (Id, Title, Duration, ArtworkFile) VALUES ({recordingId.ToString().ToUpperInvariant()}, 'Old Song', NULL, NULL)", Ct);
            await old.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO MediaSources (Id, RecordingId, Uri, SizeBytes, ModifiedUtcTicks, IsMissing, ScannerVersion) VALUES ({Guid.CreateVersion7().ToString().ToUpperInvariant()}, {recordingId.ToString().ToUpperInvariant()}, 'file:///old.flac', 1, 1, 0, 1)", Ct);
        }

        var upgraded = LocalLibrary.Open(oldPath);

        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(oldPath)!, "*.bak-*")); // a copy was kept
        Assert.Equal("Old Song", Assert.Single(await upgraded.GetTracksAsync(Ct)).Title);
        Assert.Empty(await upgraded.GetDefinitionsAsync(Ct));

        await upgraded.SaveDefinitionAsync(new FieldDefinition("local:mood", FieldType.Text), Ct);
        await upgraded.SetFieldAsync(recordingId, "local:mood", Json("\"calm\""), Ct);
        Assert.Single(await upgraded.GetFieldsAsync(recordingId, Ct));
    }

    private void Fixture(string name, string? target = null)
    {
        var destination = Path.Combine(_music, target ?? name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), destination, overwrite: true);
    }

    private LibraryDbContext OpenDb() =>
        new(new DbContextOptionsBuilder<LibraryDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString()).Options);
}
