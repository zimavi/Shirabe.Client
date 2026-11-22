using System.Text.Json;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Shirabe.Core.Metadata;
using Shirabe.Metadata;
using Xunit;

namespace Shirabe.Tests;

public class ApplyViewModelTests
{
    private static readonly TrackSummary Song = new(Guid.NewGuid(), "Example Song", "Artist", "Album", null, "file:///C:/music/a.flac");

    private readonly FakeDocumentStore _store = new();

    public ApplyViewModelTests() => SynchronizationContext.SetSynchronizationContext(null);

    internal static MetadataDocument PersonalDocument(string fields = "\"local:mood\": \"calm\"") =>
        Parse($$"""
            { "format": "shirabe.metadata", "version": "0.1",
              "subject": { "type": "recording", "id": "0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55" },
              "layer": "personal", "source": { "kind": "user", "created": "2026-11-22T18:04:00Z" }, "revision": 1,
              "fields": { {{fields}} } }
            """);

    internal static MetadataDocument Parse(string json)
    {
        Assert.True(MetadataDocument.TryParseFile(json, out var documents, out var error), error);
        return documents.Single();
    }

    [Fact]
    public async Task Loading_works_out_what_would_happen_and_writes_nothing()
    {
        _store.OnApply = (_, _) =>
        [
            new ApplyOutcome(ApplyKind.Set, "Set \"Mood\" to \"calm\" (a new field)."),
            new ApplyOutcome(ApplyKind.Skip, "Skipped \"title\" is a core field."),
        ];
        var apply = new ApplyViewModel(_store, Song, PersonalDocument());

        await apply.LoadAsync();

        Assert.Equal("Apply to \"Example Song\"", apply.Heading);
        Assert.Equal([("Change", false), ("Skipped", true)], apply.Rows.Select(r => (r.Label, r.IsSkip)));
        Assert.Equal("\"title\" is a core field.", apply.Rows[1].Text); // the label already says "Skipped"
        Assert.True(apply.CanApply);
        Assert.True(apply.ApplyCommand.CanExecute(null));
        Assert.Equal([(Song.RecordingId, true)], _store.ApplyCalls); // only a dry run so far
    }

    [Fact]
    public async Task When_everything_would_be_skipped_there_is_nothing_to_apply()
    {
        _store.OnApply = (_, _) => [new ApplyOutcome(ApplyKind.Skip, "Skipped \"local:mood\" already has this value.")];
        var apply = new ApplyViewModel(_store, Song, PersonalDocument());

        await apply.LoadAsync();

        Assert.False(apply.CanApply);
        Assert.False(apply.ApplyCommand.CanExecute(null));
        Assert.Contains("nothing to apply", apply.Result);
    }

    [Fact]
    public async Task Applying_writes_says_what_was_done_and_tells_the_caller()
    {
        var applied = 0;
        var apply = new ApplyViewModel(_store, Song, PersonalDocument(), () =>
        {
            applied++;
            return Task.CompletedTask;
        });
        await apply.LoadAsync();

        await apply.ApplyCommand.ExecuteAsync(null);

        Assert.Equal([(Song.RecordingId, true), (Song.RecordingId, false)], _store.ApplyCalls);
        Assert.Equal("Done: 1 field set.", apply.Result);
        Assert.Equal(1, applied);
        Assert.False(apply.CanApply); // done: no second apply
        Assert.False(apply.IsBusy);
    }

    [Theory]
    [InlineData(2, 0, 0, "Done: 2 fields set.")]
    [InlineData(1, 0, 3, "Done: 1 field set, 3 skipped.")]
    [InlineData(0, 1, 1, "Done: the document is kept with the song, 1 skipped.")]
    public async Task The_summary_counts_what_happened(int sets, int keeps, int skips, string expected)
    {
        _store.OnApply = (_, _) =>
        [
            .. Enumerable.Repeat(new ApplyOutcome(ApplyKind.Set, "Set a."), sets),
            .. Enumerable.Repeat(new ApplyOutcome(ApplyKind.Keep, "Keep it."), keeps),
            .. Enumerable.Repeat(new ApplyOutcome(ApplyKind.Skip, "Skipped b."), skips),
        ];
        var apply = new ApplyViewModel(_store, Song, PersonalDocument());
        await apply.LoadAsync();

        await apply.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(expected, apply.Result);
    }

    [Fact]
    public async Task A_document_the_store_refuses_shows_why_and_cannot_be_applied()
    {
        _store.ApplyFailure = new ArgumentException("The document does not follow the standard (1 error).");
        var apply = new ApplyViewModel(_store, Song, PersonalDocument());

        await apply.LoadAsync();

        Assert.Equal("The document does not follow the standard (1 error).", apply.Error);
        Assert.False(apply.CanApply);
        Assert.Empty(apply.Rows);
    }

    [Fact]
    public async Task A_failure_while_applying_is_shown_and_the_caller_is_not_told_it_worked()
    {
        var applied = 0;
        var apply = new ApplyViewModel(_store, Song, PersonalDocument(), () =>
        {
            applied++;
            return Task.CompletedTask;
        });
        await apply.LoadAsync();
        _store.ApplyFailure = new InvalidOperationException("That song is not in the library.");

        await apply.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("That song is not in the library.", apply.Error);
        Assert.Equal(0, applied);
        Assert.False(apply.IsBusy);
        Assert.Null(apply.Result);
    }

    [Fact]
    public void Close_tells_whoever_shows_the_panel()
    {
        var apply = new ApplyViewModel(_store, Song, PersonalDocument());
        var closed = false;
        apply.Closed += (_, _) => closed = true;

        apply.CloseCommand.Execute(null);

        Assert.True(closed);
    }
}

public class PropertiesDocumentsTests
{
    private static readonly TrackSummary Song = new(Guid.NewGuid(), "Example Song", "Artist", "Album", null, "file:///C:/music/a.flac");

    private readonly FakeMetadataStore _store = new();
    private readonly FakeFileTags _fileTags = new();
    private readonly FakeDocumentStore _documents = new();

    public PropertiesDocumentsTests() => SynchronizationContext.SetSynchronizationContext(null);

    private async Task<PropertiesViewModel> Open(bool withDocuments = true)
    {
        var dialog = new PropertiesViewModel(_store, _fileTags, Song, null, withDocuments ? _documents : null);
        await dialog.LoadAsync();
        return dialog;
    }

    private static StoredDocument Stored(string origin, string layer, string json, int errors = 0, int revision = 1) =>
        new(origin, layer, json, revision, errors, DateTime.UtcNow);

    private static string Json(string layer, string fields = "\"title\": \"A title\"") => $$"""
        { "format": "shirabe.metadata", "version": "0.1",
          "subject": { "type": "recording", "id": "0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55" },
          "layer": "{{layer}}", "source": { "kind": "{{(layer == "personal" ? "user" : "artist")}}", "name": "Someone", "created": "2026-03-02T09:00:00Z" },
          "revision": 4, "fields": { {{fields}} } }
        """;

    [Fact]
    public async Task Opening_reads_the_files_again_and_shows_what_the_song_has()
    {
        _documents.Documents.Add(Stored(DocumentOrigin.Sidecar, "canonical", Json("canonical", "\"title\": \"From the artist\", \"description\": \"Written on a train\""), revision: 4));
        _documents.Documents.Add(Stored(DocumentOrigin.Embedded, "community", Json("community")));

        var dialog = await Open();

        Assert.Equal(1, _documents.Refreshes); // read from the files now, not only what the last scan saw
        Assert.True(dialog.HasDocumentsSection);
        var first = dialog.Documents[0];
        Assert.Equal("Canonical document", first.Heading);
        Assert.Equal("In the sidecar file beside the audio", first.Where);
        Assert.Equal("artist, Someone · revision 4", first.Details);
        Assert.Equal("Follows the standard", first.Status);
        Assert.Equal(["title: From the artist", "description: Written on a train"], first.FieldLines);
        Assert.Equal("In the tag inside the audio file", dialog.Documents[1].Where);
    }

    [Fact]
    public async Task What_could_not_be_read_is_said_even_when_there_are_no_documents()
    {
        _documents.RefreshProblems = ["The sidecar file is not a metadata document: Not valid JSON (x) at line 1, column 2."];

        var dialog = await Open();

        Assert.True(dialog.HasDocumentsSection);
        Assert.False(dialog.HasDocuments);
        Assert.Contains("not a metadata document", dialog.DocumentsNotice);
    }

    [Fact]
    public async Task A_song_with_no_documents_and_no_problems_shows_no_section()
    {
        var dialog = await Open();

        Assert.False(dialog.HasDocumentsSection);
    }

    [Fact]
    public async Task Without_a_document_store_the_section_does_not_exist()
    {
        var dialog = await Open(withDocuments: false);

        Assert.False(dialog.HasDocumentsSection);
        Assert.Equal(0, _documents.Refreshes);
    }

    [Fact]
    public async Task A_store_that_fails_is_reported_and_the_rest_of_the_dialog_still_opens()
    {
        _documents.RefreshFailure = new InvalidOperationException("database is locked");
        _store.Definitions["local:mood"] = new FieldDefinition("local:mood", FieldType.Text);
        _store.Values[(Song.RecordingId, "local:mood")] = JsonDocument.Parse("\"calm\"").RootElement.Clone();

        var dialog = await Open();

        Assert.Contains("database is locked", dialog.DocumentsNotice);
        Assert.Single(dialog.Fields);
    }

    [Fact]
    public async Task A_document_that_breaks_the_standard_is_shown_as_such_and_cannot_be_copied()
    {
        _documents.Documents.Add(Stored(DocumentOrigin.Sidecar, "personal", Json("personal"), errors: 2));

        var dialog = await Open();

        var shown = dialog.Documents.Single();
        Assert.False(shown.IsValid);
        Assert.Contains("2 errors", shown.Status);
        Assert.Contains("not relied on", shown.Status);
        Assert.False(shown.CanCopy);
    }

    [Theory]
    [InlineData("personal", DocumentOrigin.Sidecar, true)]
    [InlineData("personal", DocumentOrigin.Applied, true)]
    [InlineData("canonical", DocumentOrigin.Sidecar, false)] // an artist's document is not copied into your own fields
    [InlineData("community", DocumentOrigin.Embedded, false)]
    public async Task Only_a_valid_personal_document_can_be_copied_into_your_fields(string layer, string origin, bool expected)
    {
        _documents.Documents.Add(Stored(origin, layer, Json(layer)));

        var dialog = await Open();

        Assert.Equal(expected, dialog.Documents.Single().CanCopy);
    }

    [Fact]
    public async Task Attached_documents_say_so()
    {
        _documents.Documents.Add(Stored(DocumentOrigin.Applied, "canonical", Json("canonical")));

        var dialog = await Open();

        Assert.Equal("Attached by you", dialog.Documents.Single().Where);
    }

    [Fact]
    public async Task Copying_shows_a_preview_first_and_applying_puts_the_new_fields_into_the_dialog()
    {
        _documents.Documents.Add(Stored(DocumentOrigin.Sidecar, "personal", Json("personal", "\"local:mood\": \"calm\"")));
        _documents.OnApply = (_, dryRun) =>
        {
            if (!dryRun) // what the real store would do
            {
                _store.Definitions["local:mood"] = new FieldDefinition("local:mood", FieldType.Text, Label: "Mood");
                _store.Values[(Song.RecordingId, "local:mood")] = JsonDocument.Parse("\"calm\"").RootElement.Clone();
            }

            return [new ApplyOutcome(ApplyKind.Set, "Set \"Mood\" to \"calm\" (a new field).")];
        };
        var dialog = await Open();
        Assert.Empty(dialog.Fields);

        await dialog.CopyDocumentCommand.ExecuteAsync(dialog.Documents[0]);

        Assert.NotNull(dialog.Apply);
        Assert.Equal([(Song.RecordingId, true)], _documents.ApplyCalls); // a preview, nothing written yet
        Assert.Empty(dialog.Fields);

        await dialog.Apply.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("Mood", Assert.Single(dialog.Fields).Name); // the dialog shows the copied field
        Assert.Equal("calm", dialog.Fields[0].Text);
    }

    [Fact]
    public async Task Copying_is_refused_while_there_are_unsaved_edits_so_they_are_not_lost()
    {
        _store.Definitions["local:mood"] = new FieldDefinition("local:mood", FieldType.Text);
        _store.Values[(Song.RecordingId, "local:mood")] = JsonDocument.Parse("\"calm\"").RootElement.Clone();
        _documents.Documents.Add(Stored(DocumentOrigin.Sidecar, "personal", Json("personal")));
        var dialog = await Open();
        dialog.Fields[0].Text = "typed but not saved";

        await dialog.CopyDocumentCommand.ExecuteAsync(dialog.Documents[0]);

        Assert.Null(dialog.Apply);
        Assert.Contains("Save or cancel your edits first", dialog.Error);
        Assert.Empty(_documents.ApplyCalls);
        Assert.Equal("typed but not saved", dialog.Fields[0].Text);
    }

    [Fact]
    public async Task Closing_the_preview_removes_it()
    {
        _documents.Documents.Add(Stored(DocumentOrigin.Sidecar, "personal", Json("personal")));
        var dialog = await Open();
        await dialog.CopyDocumentCommand.ExecuteAsync(dialog.Documents[0]);

        dialog.Apply!.CloseCommand.Execute(null);

        Assert.Null(dialog.Apply);
    }
}

internal sealed class FakeDocumentStore : IDocumentStore
{
    public List<StoredDocument> Documents { get; } = [];

    public IReadOnlyList<string> RefreshProblems { get; set; } = [];

    public Exception? RefreshFailure { get; set; }

    public int Refreshes { get; private set; }

    public Exception? ApplyFailure { get; set; }

    public List<(Guid Id, bool DryRun)> ApplyCalls { get; } = [];

    public Func<MetadataDocument, bool, IReadOnlyList<ApplyOutcome>> OnApply { get; set; } =
        (_, _) => [new ApplyOutcome(ApplyKind.Set, "Set \"Mood\" to \"calm\" (a new field).")];

    public Task<IReadOnlyList<StoredDocument>> GetDocumentsAsync(Guid recordingId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StoredDocument>>([.. Documents]);

    public Task<IReadOnlyList<string>> RefreshFromFileAsync(Guid recordingId, CancellationToken cancellationToken = default)
    {
        Refreshes++;
        if (RefreshFailure is not null) throw RefreshFailure;
        return Task.FromResult(RefreshProblems);
    }

    /// <summary>Every write to a song's files, in order: song, origin, the document's layer.</summary>
    public List<(Guid Id, string Origin, string? Layer)> Writes { get; } = [];

    public Exception? WriteFailure { get; set; }

    public string WriteMessage { get; set; } = "Written.";

    public Task<string> WriteToFileAsync(Guid recordingId, MetadataDocument document, string origin, CancellationToken cancellationToken = default)
    {
        if (WriteFailure is not null) throw WriteFailure;
        Writes.Add((recordingId, origin, document.Layer));
        return Task.FromResult(WriteMessage);
    }

    public Task<IReadOnlyList<ApplyOutcome>> ApplyAsync(
        Guid recordingId, MetadataDocument document, bool dryRun, CancellationToken cancellationToken = default)
    {
        ApplyCalls.Add((recordingId, dryRun));
        if (ApplyFailure is not null) throw ApplyFailure;
        return Task.FromResult(OnApply(document, dryRun));
    }
}
