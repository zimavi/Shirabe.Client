using System.Text.Json;

using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Shirabe.Metadata;

using Xunit;

namespace Shirabe.Tests;

/// <summary>"Export..." and "Save next to the audio" in the properties dialog: local out, nothing uploaded.</summary>
public sealed class PropertiesExportTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"shirabe-export-{Guid.NewGuid():N}");
    private readonly FakeMetadataStore _store = new();
    private readonly FakeFileTags _fileTags = new();
    private readonly FakeFilePicker _picker = new();
    private readonly TrackSummary _song;

    public PropertiesExportTests()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        Directory.CreateDirectory(_folder);
        _song = new TrackSummary(Guid.NewGuid(), "Example Song", "Artist", "Album", null,
            new Uri(Path.Combine(_folder, "song.flac")).AbsoluteUri);
        _store.Definitions["local:mood"] =
            new FieldDefinition("local:mood", FieldType.Text, Multiple: true, Label: "Mood");
        _store.Definitions["local:bpm"] = new FieldDefinition("local:bpm", FieldType.Number, Label: "Tempo");
        _store.Values[(_song.RecordingId, "local:mood")] =
            JsonDocument.Parse("""["calm","late"]""").RootElement.Clone();
        _store.Values[(_song.RecordingId, "local:bpm")] = JsonDocument.Parse("120").RootElement.Clone();
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private async Task<PropertiesViewModel> Open(TrackSummary? track = null)
    {
        var dialog = new PropertiesViewModel(_store, _fileTags, track ?? _song, _picker);
        await dialog.LoadAsync();
        return dialog;
    }

    private static MetadataDocument ReadDocument(string path)
    {
        Assert.True(MetadataDocument.TryParseFile(File.ReadAllText(path), out var documents, out var error), error);
        return documents.First();
    }

    [Fact]
    public async Task Export_writes_a_valid_personal_document_to_the_chosen_file()
    {
        var target = Path.Combine(_folder, "out", "mine.shirabe.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        _picker.SaveResult = target;
        var dialog = await Open();

        await dialog.ExportCommand.ExecuteAsync(null);

        var document = ReadDocument(target);
        Assert.Empty(document.Validate());
        Assert.Equal(("personal", "user", _song.RecordingId.ToString()),
            (document.Layer, document.SourceKind, document.SubjectId));
        Assert.Equal(["local:bpm", "local:mood"], document.FieldNames);
        Assert.Equal("""["calm","late"]""", document.Root["fields"]!["local:mood"]!.ToJsonString());
        Assert.Equal("Mood", document.Root["definitions"]!["local:mood"]!["label"]!.GetValue<string>());
        Assert.Equal($"Exported to {target}", dialog.Notice);
        Assert.False(dialog.HasError);
    }

    [Fact]
    public async Task Export_suggests_a_file_name_from_the_title_without_characters_files_cannot_have()
    {
        var dialog = await Open(_song with { Title = "AC/DC: Back In Black?" });
        _picker.SaveResult = null;

        await dialog.ExportCommand.ExecuteAsync(null);

        // Only what this system forbids in a file name is replaced: Windows forbids more than Linux and macOS.
        var name = OperatingSystem.IsWindows() ? "AC_DC_ Back In Black_" : "AC_DC: Back In Black?";
        Assert.Equal($"{name}.shirabe.json", _picker.LastSuggestedName);
    }

    [Fact]
    public async Task A_cancelled_export_writes_nothing_and_says_nothing()
    {
        var dialog = await Open();
        _picker.SaveResult = null;

        await dialog.ExportCommand.ExecuteAsync(null);

        Assert.Equal(1, _picker.SaveAsked);
        Assert.False(dialog.HasNotice);
        Assert.False(dialog.HasError);
    }

    [Fact]
    public async Task Export_takes_what_the_dialog_shows_including_edits_not_saved_yet_and_leaves_out_removed_fields()
    {
        var target = Path.Combine(_folder, "edited.shirabe.json");
        _picker.SaveResult = target;
        var dialog = await Open();
        dialog.Fields.Single(f => f.Definition.Key == "local:bpm").Text = "128";
        dialog.RemoveFieldCommand.Execute(dialog.Fields.Single(f => f.Definition.Key == "local:mood"));

        await dialog.ExportCommand.ExecuteAsync(null);

        var document = ReadDocument(target);
        Assert.Equal(["local:bpm"], document.FieldNames);
        Assert.Equal("128", document.Root["fields"]!["local:bpm"]!.ToJsonString());
        Assert.Null(document.Root["definitions"]!["local:mood"]); // a removed field is not described either
        Assert.Empty(_store.Calls); // exporting changed nothing in the library
    }

    [Fact]
    public async Task A_field_that_does_not_fit_stops_the_export_before_any_dialog_opens()
    {
        _picker.SaveResult = Path.Combine(_folder, "never.shirabe.json");
        var dialog = await Open();
        dialog.Fields.Single(f => f.Definition.Key == "local:bpm").Text = "fast";

        await dialog.ExportCommand.ExecuteAsync(null);

        Assert.Equal(0, _picker.SaveAsked);
        Assert.Equal("Needs a number.", dialog.Fields.Single(f => f.Definition.Key == "local:bpm").Error);
        Assert.False(File.Exists(_picker.SaveResult));
    }

    [Fact]
    public async Task With_no_custom_fields_there_is_nothing_to_export()
    {
        _store.Values.Clear();
        var dialog = await Open();

        await dialog.ExportCommand.ExecuteAsync(null);

        Assert.Equal("There are no custom fields to export.", dialog.Error);
        Assert.Equal(0, _picker.SaveAsked);
    }

    [Fact]
    public async Task A_place_that_cannot_be_written_is_reported()
    {
        _picker.SaveResult = Path.Combine(_folder, "no-such-folder", "x.shirabe.json");
        var dialog = await Open();

        await dialog.ExportCommand.ExecuteAsync(null);

        Assert.Contains("Could not write", dialog.Error);
        Assert.False(dialog.HasNotice);
    }

    [Fact]
    public async Task Without_a_file_picker_there_is_no_export()
    {
        var dialog = new PropertiesViewModel(_store, _fileTags, _song);

        Assert.False(dialog.HasExport);
        await dialog.ExportCommand.ExecuteAsync(null); // harmless
        Assert.False(dialog.HasNotice);
        Assert.True((await Open()).HasExport); // with a picker the buttons show
    }

    // ---- the sidecar beside the audio ----

    [Fact]
    public async Task The_sidecar_is_written_beside_the_audio_file()
    {
        var dialog = await Open();

        await dialog.SaveSidecarCommand.ExecuteAsync(null);

        var sidecar = Path.Combine(_folder, "song.flac.shirabe.json");
        var document = ReadDocument(sidecar);
        Assert.Empty(document.Validate());
        Assert.Equal(["local:bpm", "local:mood"], document.FieldNames);
        Assert.Equal("Saved next to the audio file as song.flac.shirabe.json", dialog.Notice);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp")); // the swap file is gone
        Assert.Equal(0, _picker.SaveAsked); // no dialog: the place is decided by the audio file
    }

    [Fact]
    public async Task An_artists_document_already_in_the_sidecar_is_kept_when_personal_notes_are_saved()
    {
        var sidecar = Path.Combine(_folder, "song.flac.shirabe.json");
        const string artist = """
                              { "format": "shirabe.metadata", "version": "0.1",
                                "subject": { "type": "recording", "id": "0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55" },
                                "layer": "canonical", "source": { "kind": "artist", "created": "2026-03-02T09:00:00Z" }, "revision": 3,
                                "fields": { "title": "The Artists Title" } }
                              """;
        File.WriteAllText(sidecar, artist);
        var dialog = await Open();

        await dialog.SaveSidecarCommand.ExecuteAsync(null);

        Assert.True(MetadataDocument.TryParseFile(File.ReadAllText(sidecar), out var documents, out _));
        Assert.Equal(["canonical", "personal"], documents.Select(d => d.Layer));
        Assert.Equal("The Artists Title", documents[0].Title);
        Assert.Equal(3, documents[0].Revision);
    }

    [Fact]
    public async Task Saving_again_replaces_the_personal_document_and_raises_its_revision()
    {
        var dialog = await Open();
        await dialog.SaveSidecarCommand.ExecuteAsync(null);
        dialog.Fields.Single(f => f.Definition.Key == "local:bpm").Text = "99";

        await dialog.SaveSidecarCommand.ExecuteAsync(null);

        var document = ReadDocument(Path.Combine(_folder, "song.flac.shirabe.json"));
        Assert.Equal(2, document.Revision);
        Assert.Equal("99", document.Root["fields"]!["local:bpm"]!.ToJsonString());
    }

    [Fact]
    public async Task A_sidecar_that_cannot_be_read_is_never_overwritten()
    {
        var sidecar = Path.Combine(_folder, "song.flac.shirabe.json");
        File.WriteAllText(sidecar, "{ someone's hand-edited file with a typo");
        var dialog = await Open();

        await dialog.SaveSidecarCommand.ExecuteAsync(null);

        Assert.Contains("left alone", dialog.Error);
        Assert.Equal("{ someone's hand-edited file with a typo", File.ReadAllText(sidecar));
        Assert.False(dialog.HasNotice);
    }

    [Fact]
    public async Task A_song_that_is_not_a_local_file_has_no_place_for_a_sidecar()
    {
        var dialog = await Open(_song with { MediaUri = "https://example.test/song.mp3" });

        await dialog.SaveSidecarCommand.ExecuteAsync(null);

        Assert.Contains("not a file on this computer", dialog.Error);
    }

    [Fact]
    public async Task A_mistake_in_a_field_stops_the_sidecar_too()
    {
        var dialog = await Open();
        dialog.Fields.Single(f => f.Definition.Key == "local:bpm").Text = "fast";

        await dialog.SaveSidecarCommand.ExecuteAsync(null);

        Assert.False(File.Exists(Path.Combine(_folder, "song.flac.shirabe.json")));
    }
}