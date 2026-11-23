using Shirabe.Client.Studio;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Shirabe.Core.Metadata;
using Shirabe.Metadata;
using Xunit;

namespace Shirabe.Tests;

public sealed class StudioEditorTests : IDisposable
{
    private static readonly TrackSummary Song = new(Guid.NewGuid(), "Poker Face", "Lady Gaga", "The Fame", null, "file:///C:/music/Poker%20Face.flac");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"shirabe-editor-{Guid.NewGuid():N}");
    private readonly FakeFilePicker _picker = new();
    private readonly FakeDocumentStore _store = new();
    private readonly FakeLibrary _library = new() { Tracks = [Song] };

    public StudioEditorTests()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static string Template(string layer = "canonical") =>
        DocumentTemplates.New(Song.RecordingId, layer, new DateTime(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc), Song.Title, Song.Artist, Song.Album).ToJson();

    private StudioEditorViewModel Editor(string? json = null, TrackSummary? target = null, bool withStore = true, bool withLibrary = true) =>
        new(_picker, withStore ? _store : null, withLibrary ? _library : null, "Heading", json ?? Template(), target);

    // ---- live checking ----

    [Fact]
    public void A_template_follows_the_standard_from_the_start()
    {
        var editor = Editor();

        Assert.True(editor.IsValid);
        Assert.Equal("Follows the standard", editor.Status);
        Assert.False(editor.HasProblems);
        Assert.True(editor.SaveAsFileCommand.CanExecute(null));
    }

    [Fact]
    public void Typing_a_mistake_is_noticed_at_once_and_fixing_it_clears_it()
    {
        var editor = Editor();

        editor.Text = editor.Text.Replace("\"revision\": 1", "\"revision\": 0");

        Assert.False(editor.IsValid);
        Assert.Equal("Does not follow the standard: 1 error", editor.Status);
        Assert.True(editor.HasProblems);
        Assert.Equal("revision", Assert.Single(editor.Problems).Path);
        Assert.False(editor.SaveAsFileCommand.CanExecute(null)); // a document with errors cannot be saved

        editor.Text = editor.Text.Replace("\"revision\": 0", "\"revision\": 1");

        Assert.True(editor.IsValid);
        Assert.False(editor.HasProblems);
        Assert.True(editor.SaveAsFileCommand.CanExecute(null));
    }

    [Fact]
    public void Text_that_is_not_json_says_where_and_has_no_problem_list()
    {
        var editor = Editor();

        editor.Text = "{ \"format\": ";

        Assert.False(editor.IsValid);
        Assert.StartsWith("Not valid JSON", editor.Status);
        Assert.False(editor.HasProblems);
    }

    [Fact]
    public void A_list_of_documents_is_not_editable_here()
    {
        var editor = Editor();

        editor.Text = $"[{editor.Text}, {editor.Text}]";

        Assert.False(editor.IsValid);
        Assert.Contains("one document at a time", editor.Status);
    }

    [Fact]
    public void Warnings_do_not_stop_a_document_from_being_saved()
    {
        var editor = Editor();

        editor.Text = editor.Text.Replace("\"fields\": {", "\"fields\": { \"example.com:empty\": null,");

        Assert.True(editor.IsValid);
        Assert.Equal("Follows the standard, 1 warning", editor.Status);
        Assert.True(editor.SaveAsFileCommand.CanExecute(null));
    }

    [Fact]
    public void Format_tidies_a_valid_document_and_leaves_a_broken_one_alone()
    {
        var editor = Editor();
        var compact = System.Text.Json.Nodes.JsonNode.Parse(editor.Text)!.ToJsonString();
        editor.Text = compact;

        editor.FormatCommand.Execute(null);
        Assert.Contains("\n", editor.Text);
        Assert.Equal(compact, System.Text.Json.Nodes.JsonNode.Parse(editor.Text)!.ToJsonString());

        editor.Text = "{ broken";
        editor.FormatCommand.Execute(null);
        Assert.Equal("{ broken", editor.Text);
    }

    // ---- saving as a file ----

    [Fact]
    public async Task Saving_as_a_file_writes_the_document_where_asked_and_suggests_a_name_from_the_title()
    {
        var target = Path.Combine(_folder, "out.shirabe.json");
        _picker.SaveResult = target;
        var editor = Editor();

        await editor.SaveAsFileCommand.ExecuteAsync(null);

        Assert.Equal("Poker Face.shirabe.json", _picker.LastSuggestedName);
        Assert.True(MetadataDocument.TryParseFile(File.ReadAllText(target), out var documents, out _));
        Assert.Equal("Poker Face", documents.Single().Title);
        Assert.Equal($"Saved to {target}", editor.Notice);
    }

    [Fact]
    public async Task A_title_with_characters_a_file_cannot_have_gives_a_usable_name()
    {
        var editor = Editor(DocumentTemplates.New(Guid.NewGuid(), "canonical", DateTime.UtcNow, "AC/DC: Back In Black?").ToJson());

        await editor.SaveAsFileCommand.ExecuteAsync(null);

        Assert.Equal("AC_DC_ Back In Black_.shirabe.json", _picker.LastSuggestedName);
    }

    [Fact]
    public async Task A_cancelled_save_writes_nothing_and_says_nothing()
    {
        var editor = Editor();
        _picker.SaveResult = null;

        await editor.SaveAsFileCommand.ExecuteAsync(null);

        Assert.False(editor.HasNotice);
        Assert.False(editor.HasError);
    }

    [Fact]
    public async Task A_place_that_cannot_be_written_is_reported()
    {
        _picker.SaveResult = Path.Combine(_folder, "no-such-folder", "x.shirabe.json");
        var editor = Editor();

        await editor.SaveAsFileCommand.ExecuteAsync(null);

        Assert.Contains("Could not write", editor.Error);
    }

    // ---- saving to a song ----

    [Fact]
    public void Saving_to_a_song_needs_a_song_and_a_valid_document()
    {
        Assert.False(Editor().SaveAsSidecarCommand.CanExecute(null));          // no song chosen
        Assert.False(Editor().StartEmbedCommand.CanExecute(null));
        Assert.False(Editor(target: Song, withStore: false).SaveAsSidecarCommand.CanExecute(null)); // no store to write with
        Assert.True(Editor(target: Song).SaveAsSidecarCommand.CanExecute(null));

        var editor = Editor(target: Song);
        editor.Text = "{ broken";
        Assert.False(editor.SaveAsSidecarCommand.CanExecute(null));
        Assert.False(editor.StartEmbedCommand.CanExecute(null));
        Assert.False(editor.ApplyToLibraryCommand.CanExecute(null));
    }

    [Fact]
    public async Task Saving_as_a_sidecar_writes_through_the_store_for_the_chosen_song()
    {
        _store.WriteMessage = "Saved to Poker Face.flac.shirabe.json, beside the audio file.";
        var editor = Editor(target: Song);
        var before = editor.Text;

        await editor.SaveAsSidecarCommand.ExecuteAsync(null);

        Assert.Equal([(Song.RecordingId, DocumentOrigin.Sidecar, "canonical")], _store.Writes);
        Assert.Equal("Saved to Poker Face.flac.shirabe.json, beside the audio file.", editor.Notice);
        Assert.Equal(before, editor.Text); // writing bumps a revision in the written copy, never in what you are editing
        Assert.False(editor.IsBusy);
    }

    [Fact]
    public async Task Writing_into_the_audio_file_asks_first_and_does_nothing_until_confirmed()
    {
        var editor = Editor(target: Song);

        editor.StartEmbedCommand.Execute(null);

        Assert.True(editor.ConfirmingEmbed);
        Assert.Equal("Poker Face.flac", editor.TargetFileName);
        Assert.Empty(_store.Writes);

        editor.CancelEmbedCommand.Execute(null);
        Assert.False(editor.ConfirmingEmbed);
        Assert.Empty(_store.Writes);

        editor.StartEmbedCommand.Execute(null);
        await editor.ConfirmEmbedCommand.ExecuteAsync(null);

        Assert.Equal([(Song.RecordingId, DocumentOrigin.Embedded, "canonical")], _store.Writes);
        Assert.False(editor.ConfirmingEmbed);
    }

    [Fact]
    public void Changing_the_text_withdraws_a_pending_confirmation()
    {
        var editor = Editor(target: Song);
        editor.StartEmbedCommand.Execute(null);

        editor.Text += " ";

        Assert.False(editor.ConfirmingEmbed); // what was confirmed is no longer what is in the editor
    }

    [Fact]
    public async Task A_write_that_fails_shows_why_and_the_store_message_is_not_lost()
    {
        _store.WriteFailure = new InvalidOperationException("The audio file was left as it was. Writing failed: access denied");
        var editor = Editor(target: Song);

        await editor.SaveAsSidecarCommand.ExecuteAsync(null);

        Assert.Equal("The audio file was left as it was. Writing failed: access denied", editor.Error);
        Assert.False(editor.HasNotice);
        Assert.False(editor.IsBusy);
    }

    [Fact]
    public async Task A_file_that_was_moved_is_reported()
    {
        _store.WriteFailure = new FileNotFoundException("The file was moved or deleted.");
        var editor = Editor(target: Song);

        await editor.SaveAsSidecarCommand.ExecuteAsync(null);

        Assert.Equal("The file was moved or deleted.", editor.Error);
    }

    // ---- applying, choosing the song ----

    [Fact]
    public async Task Apply_to_the_library_shows_the_preview_for_the_chosen_song()
    {
        var editor = Editor(Template("personal"), target: Song);

        await editor.ApplyToLibraryCommand.ExecuteAsync(null);

        Assert.NotNull(editor.Apply);
        Assert.Equal([(Song.RecordingId, true)], _store.ApplyCalls); // a preview first
        editor.Apply.CloseCommand.Execute(null);
        Assert.Null(editor.Apply);
    }

    [Fact]
    public async Task Choosing_a_song_sets_the_target_and_enables_saving_to_it()
    {
        var editor = Editor();
        Assert.True(editor.CanChooseSong);

        await editor.ChooseSongCommand.ExecuteAsync(null);
        Assert.NotNull(editor.Picking);
        Assert.False(editor.Picking.ChooseLayer);
        editor.Picking.SelectedSong = Song;
        editor.Picking.ChooseCommand.Execute(null);

        Assert.Same(Song, editor.Target);
        Assert.Null(editor.Picking);
        Assert.Equal("Song: Poker Face", editor.TargetText);
        Assert.True(editor.SaveAsSidecarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Closing_the_picker_changes_nothing()
    {
        var editor = Editor(target: Song);
        await editor.ChooseSongCommand.ExecuteAsync(null);

        editor.Picking!.CloseCommand.Execute(null);

        Assert.Null(editor.Picking);
        Assert.Same(Song, editor.Target);
    }

    [Fact]
    public void Without_a_library_and_store_there_is_no_choosing_a_song()
    {
        Assert.False(Editor(withLibrary: false).CanChooseSong);
        Assert.False(Editor(withStore: false).CanChooseSong);
    }

    [Fact]
    public void Close_tells_whoever_shows_the_editor()
    {
        var editor = Editor();
        var closed = false;
        editor.Closed += (_, _) => closed = true;

        editor.CloseCommand.Execute(null);

        Assert.True(closed);
    }
}

public class SongPickerViewModelTests
{
    private static TrackSummary Track(string title, string? artist = "A") => new(Guid.NewGuid(), title, artist, null, null, $"file:///C:/{title}.flac");

    [Fact]
    public void Choosing_needs_a_song_and_then_announces_it()
    {
        var one = Track("One");
        var picker = new SongPickerViewModel([one, Track("Two")], "Heading", "Create", chooseLayer: true);
        var picked = 0;
        picker.Picked += (_, _) => picked++;
        Assert.False(picker.ChooseCommand.CanExecute(null));

        picker.SelectedSong = one;
        Assert.True(picker.ChooseCommand.CanExecute(null));
        picker.ChooseCommand.Execute(null);

        Assert.Equal(1, picked);
    }

    [Theory]
    [InlineData(0, "personal")]
    [InlineData(1, "canonical")]
    [InlineData(2, "community")]
    [InlineData(9, "community")]  // out of range is kept inside
    [InlineData(-1, "personal")]
    public void The_layer_follows_the_choice_in_the_list(int index, string expected)
    {
        var picker = new SongPickerViewModel([Track("One")], "H", "Create", chooseLayer: true) { SelectedLayerIndex = index };

        Assert.Equal(expected, picker.SelectedLayer);
        Assert.Equal(3, SongPickerViewModel.LayerNames.Count);
    }

    [Fact]
    public void Typing_narrows_the_list()
    {
        var picker = new SongPickerViewModel([Track("Poker Face", "Lady Gaga"), Track("Other", "Someone")], "H", "Choose", chooseLayer: false);

        picker.Filter = "gaga";

        Assert.Equal(["Poker Face"], picker.Songs.Select(t => t.Title));
        Assert.False(picker.ChooseLayer);
    }
}

public sealed class StudioNewAndEditTests : IDisposable
{
    private static readonly TrackSummary Song = new(Guid.NewGuid(), "Poker Face", "Lady Gaga", "The Fame", null, "file:///C:/music/a.flac");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"shirabe-studio-new-{Guid.NewGuid():N}");
    private readonly FakeFilePicker _picker = new();
    private readonly FakeDocumentStore _store = new();
    private readonly FakeLibrary _library = new() { Tracks = [Song] };

    public StudioNewAndEditTests()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task A_new_document_for_a_song_starts_from_its_tags_in_the_chosen_layer()
    {
        var studio = new StudioViewModel(_picker, _library, _store);

        await studio.NewDocumentCommand.ExecuteAsync(null);
        Assert.NotNull(studio.Picking);
        Assert.True(studio.Picking.ChooseLayer);
        studio.Picking.SelectedSong = Song;
        studio.Picking.SelectedLayerIndex = 1; // artist: canonical
        studio.Picking.ChooseCommand.Execute(null);

        Assert.Null(studio.Picking);
        var editor = Assert.IsType<StudioEditorViewModel>(studio.Editor);
        Assert.Same(Song, editor.Target);
        Assert.Contains("canonical", editor.Heading);
        Assert.True(editor.IsValid);
        Assert.Contains("Poker Face", editor.Text);
        Assert.Contains("Lady Gaga", editor.Text);
        Assert.Contains("The Fame", editor.Text);
        Assert.Contains(Song.RecordingId.ToString(), editor.Text);
        Assert.True(studio.IsEditing);
        Assert.False(studio.ShowDocuments);
        Assert.False(studio.ShowEmptyState);
    }

    [Fact]
    public async Task A_personal_document_starts_empty_for_the_chosen_song()
    {
        var studio = new StudioViewModel(_picker, _library, _store);
        await studio.NewDocumentCommand.ExecuteAsync(null);
        studio.Picking!.SelectedSong = Song;

        studio.Picking.ChooseCommand.Execute(null);

        Assert.DoesNotContain("Lady Gaga", studio.Editor!.Text);
        Assert.True(studio.Editor.IsValid);
    }

    [Fact]
    public async Task Without_a_library_a_new_document_is_a_blank_personal_one()
    {
        var studio = new StudioViewModel(_picker);

        await studio.NewDocumentCommand.ExecuteAsync(null);

        Assert.Null(studio.Picking);
        Assert.Null(studio.Editor!.Target);
        Assert.True(studio.Editor.IsValid);
        Assert.Contains("\"personal\"", studio.Editor.Text);
    }

    [Fact]
    public async Task Closing_the_picker_starts_nothing()
    {
        var studio = new StudioViewModel(_picker, _library, _store);
        await studio.NewDocumentCommand.ExecuteAsync(null);

        studio.Picking!.CloseCommand.Execute(null);

        Assert.Null(studio.Picking);
        Assert.Null(studio.Editor);
        Assert.True(studio.ShowEmptyState);
    }

    [Fact]
    public async Task A_library_that_cannot_be_read_is_reported_when_starting_a_new_document()
    {
        var studio = new StudioViewModel(_picker, new ThrowingLibrary(), _store);

        await studio.NewDocumentCommand.ExecuteAsync(null);

        Assert.Null(studio.Picking);
        Assert.Contains("Could not load the library", studio.Error);
    }

    [Fact]
    public async Task An_opened_document_can_be_edited_and_closing_the_editor_brings_the_documents_back()
    {
        var path = Path.Combine(_folder, "a.shirabe.json");
        File.WriteAllText(path, DocumentTemplates.New(Guid.NewGuid(), "personal", DateTime.UtcNow).ToJson());
        var studio = new StudioViewModel(_picker, _library, _store);
        await studio.OpenPathAsync(path);
        Assert.True(studio.ShowDocuments);

        studio.EditDocumentCommand.Execute(studio.Documents[0]);

        var editor = Assert.IsType<StudioEditorViewModel>(studio.Editor);
        Assert.Equal(studio.Documents[0].Json, editor.Text);
        Assert.Null(editor.Target); // a document from a file is not about a chosen song until one is chosen
        Assert.False(studio.ShowDocuments);

        editor.CloseCommand.Execute(null);

        Assert.Null(studio.Editor);
        Assert.True(studio.ShowDocuments);
    }

    // ---- from the properties dialog to Studio ----

    private static StoredDocument Stored(string layer = "canonical") =>
        new(DocumentOrigin.Sidecar, layer, DocumentTemplates.New(Song.RecordingId, layer, DateTime.UtcNow, "From the sidecar").ToJson(), 1, 0, DateTime.UtcNow);

    [Fact]
    public async Task A_stored_document_can_be_edited_only_where_studio_can_take_it()
    {
        _store.Documents.Add(Stored());
        var without = new PropertiesViewModel(new FakeMetadataStore(), new FakeFileTags(), Song, null, _store);
        await without.LoadAsync();
        var with = new PropertiesViewModel(new FakeMetadataStore(), new FakeFileTags(), Song, null, _store) { CanEditInStudio = true };
        await with.LoadAsync();

        Assert.False(without.Documents[0].CanEdit);
        Assert.True(with.Documents[0].CanEdit);
    }

    [Fact]
    public async Task Asking_to_edit_a_document_of_a_song_closes_the_dialog_and_tells_the_shell()
    {
        _store.Documents.Add(Stored());
        var songs = new SongsViewModel(_library, new FakeScanner(), new FakeFolderPicker(), new PlayerViewModel(new FakeAudioPlayer()),
            track => new PropertiesViewModel(new FakeMetadataStore(), new FakeFileTags(), track, null, _store));
        StudioEditRequest? request = null;
        songs.EditInStudioRequested += (_, r) => request = r;

        await songs.ShowPropertiesCommand.ExecuteAsync(Song);
        Assert.True(songs.Properties!.CanEditInStudio);
        songs.Properties.EditDocumentCommand.Execute(songs.Properties.Documents[0]);

        Assert.Null(songs.Properties);
        Assert.Same(Song, request!.Song);
        Assert.Equal("canonical", request.Document.Layer);
    }

    [Fact]
    public async Task Without_a_shell_listening_the_dialog_does_not_offer_to_edit_in_studio()
    {
        _store.Documents.Add(Stored());
        var songs = new SongsViewModel(_library, new FakeScanner(), new FakeFolderPicker(), new PlayerViewModel(new FakeAudioPlayer()),
            track => new PropertiesViewModel(new FakeMetadataStore(), new FakeFileTags(), track, null, _store));

        await songs.ShowPropertiesCommand.ExecuteAsync(Song);

        Assert.False(songs.Properties!.CanEditInStudio);
        Assert.False(songs.Properties.Documents[0].CanEdit);
    }

    [Fact]
    public async Task The_shell_opens_the_editor_on_the_document_with_its_song_as_the_target()
    {
        _store.Documents.Add(Stored("personal"));
        var songs = new SongsViewModel(_library, new FakeScanner(), new FakeFolderPicker(), new PlayerViewModel(new FakeAudioPlayer()),
            track => new PropertiesViewModel(new FakeMetadataStore(), new FakeFileTags(), track, null, _store));
        var main = new MainViewModel(new PlayerViewModel(new FakeAudioPlayer()), songs, null,
            studioFactory: () => new StudioViewModel(_picker, _library, _store));

        await songs.ShowPropertiesCommand.ExecuteAsync(Song);
        songs.Properties!.EditDocumentCommand.Execute(songs.Properties.Documents[0]);

        var studio = Assert.IsType<StudioViewModel>(main.CurrentPage);
        Assert.True(main.IsStudioSelected);
        var editor = Assert.IsType<StudioEditorViewModel>(studio.Editor);
        Assert.Same(Song, editor.Target);
        Assert.Contains("personal document of \"Poker Face\"", editor.Heading);
        Assert.Equal(_store.Documents[0].Json, editor.Text);
    }

    private sealed class ThrowingLibrary : ILibrary
    {
        public Task<IReadOnlyList<TrackSummary>> GetTracksAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("database is locked");
    }
}
