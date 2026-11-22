using System.Text.Json;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Shirabe.Core.Metadata;
using Shirabe.Metadata;
using Xunit;

namespace Shirabe.Tests;

public class PropertiesViewModelTests
{
    private static readonly TrackSummary Song = new(Guid.NewGuid(), "Example Song", "Example Artist", "Example Album", null, "file:///C:/music/a.flac");

    private readonly FakeMetadataStore _store = new();
    private readonly FakeFileTags _fileTags = new();

    public PropertiesViewModelTests() => SynchronizationContext.SetSynchronizationContext(null);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private async Task<PropertiesViewModel> Open()
    {
        var dialog = new PropertiesViewModel(_store, _fileTags, Song);
        await dialog.LoadAsync();
        return dialog;
    }

    [Fact]
    public async Task It_shows_the_songs_fields_and_what_the_file_contains()
    {
        _store.Definitions["local:mood"] = new FieldDefinition("local:mood", FieldType.Text, Label: "Mood");
        _store.Definitions["local:bpm"] = new FieldDefinition("local:bpm", FieldType.Number, Label: "Tempo");
        _store.Values[(Song.RecordingId, "local:mood")] = Json("\"calm\"");
        _fileTags.Rows = [new FileTag("Tags", "Title", "Example Song"), new FileTag("Tags", "Artist", "Example Artist"), new FileTag("Audio", "Format", "FLAC")];

        var dialog = await Open();

        Assert.Equal("Example Song", dialog.Title);
        Assert.Equal("Example Artist · Example Album", dialog.Subtitle);
        var field = Assert.Single(dialog.Fields);
        Assert.Equal(("Mood", "calm"), (field.Name, field.Text));
        Assert.True(dialog.HasFields);
        Assert.Equal(["Tempo"], dialog.AvailableFields.Select(d => d.DisplayName)); // defined elsewhere, not on this song
        Assert.Equal(["Title", "Artist"], dialog.Tags.Select(t => t.Name));
        Assert.Equal(["Format"], dialog.Audio.Select(t => t.Name));
        Assert.False(dialog.HasTagsError);
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_says_why_and_the_custom_fields_still_work()
    {
        _store.Definitions["local:mood"] = new FieldDefinition("local:mood", FieldType.Text);
        _store.Values[(Song.RecordingId, "local:mood")] = Json("\"calm\"");
        _fileTags.Failure = new FileNotFoundException();

        var dialog = await Open();

        Assert.Contains("moved or deleted", dialog.TagsError);
        Assert.Empty(dialog.Tags);
        Assert.Single(dialog.Fields);
    }

    [Fact]
    public async Task Other_read_failures_are_shown_instead_of_crashing()
    {
        _fileTags.Failure = new InvalidDataException("not audio");

        var dialog = await Open();

        Assert.Equal("Could not read the file's tags: not audio", dialog.TagsError);
    }

    [Fact]
    public async Task A_broken_store_is_reported_and_the_dialog_still_opens()
    {
        _store.Failure = new InvalidOperationException("database is locked");

        var dialog = await Open();

        Assert.Contains("database is locked", dialog.Error);
        Assert.Empty(dialog.Fields);
    }

    [Fact]
    public async Task Editing_a_value_and_saving_writes_it_and_closes()
    {
        _store.Definitions["local:mood"] = new FieldDefinition("local:mood", FieldType.Text);
        _store.Values[(Song.RecordingId, "local:mood")] = Json("\"calm\"");
        var dialog = await Open();
        var closed = false;
        dialog.Closed += (_, _) => closed = true;

        dialog.Fields[0].Text = "tense";
        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal("tense", _store.Values[(Song.RecordingId, "local:mood")].GetString());
        Assert.Equal(["set local:mood"], _store.Calls);
        Assert.True(closed);
    }

    [Fact]
    public async Task Fields_that_were_not_edited_are_not_written_at_all()
    {
        _store.Definitions["local:src"] = new FieldDefinition("local:src", FieldType.Link);
        _store.Values[(Song.RecordingId, "local:src")] = Json("""{"url":"https://example.com/a","sha256":"abc"}""");
        var dialog = await Open();

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Calls); // the stored link, with its extra members, is untouched
        Assert.Contains("sha256", _store.Values[(Song.RecordingId, "local:src")].GetRawText());
    }

    [Fact]
    public async Task Cancel_writes_nothing()
    {
        _store.Definitions["local:mood"] = new FieldDefinition("local:mood", FieldType.Text);
        _store.Values[(Song.RecordingId, "local:mood")] = Json("\"calm\"");
        var dialog = await Open();
        var closed = false;
        dialog.Closed += (_, _) => closed = true;

        dialog.Fields[0].Text = "tense";
        dialog.StartNewFieldCommand.Execute(null);
        dialog.NewLabel = "Other";
        dialog.CreateNewFieldCommand.Execute(null);
        dialog.CancelCommand.Execute(null);

        Assert.Empty(_store.Calls);
        Assert.Equal("calm", _store.Values[(Song.RecordingId, "local:mood")].GetString());
        Assert.True(closed);
    }

    [Fact]
    public async Task A_new_field_creates_its_definition_then_its_value()
    {
        var dialog = await Open();

        dialog.StartNewFieldCommand.Execute(null);
        dialog.NewLabel = " Mood ";
        dialog.NewTypeIndex = (int)FieldType.Text;
        dialog.NewMultiple = true;
        dialog.CreateNewFieldCommand.Execute(null);
        dialog.Fields[0].Text = "late night\ndriving";
        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["define local:mood", "set local:mood"], _store.Calls);
        Assert.Equal(new FieldDefinition("local:mood", FieldType.Text, true, "Mood"), _store.Definitions["local:mood"]);
        Assert.Equal("""["late night","driving"]""", _store.Values[(Song.RecordingId, "local:mood")].GetRawText());
    }

    [Fact]
    public async Task A_new_field_with_nothing_typed_is_not_created()
    {
        var dialog = await Open();
        dialog.StartNewFieldCommand.Execute(null);
        dialog.NewLabel = "Mood";
        dialog.CreateNewFieldCommand.Execute(null);

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Calls);
        Assert.Empty(_store.Definitions);
    }

    [Fact]
    public async Task A_yes_no_field_cannot_be_a_list()
    {
        var dialog = await Open();
        dialog.StartNewFieldCommand.Execute(null);
        dialog.NewLabel = "Live";
        dialog.NewTypeIndex = (int)FieldType.Boolean;
        dialog.NewMultiple = true;

        Assert.False(dialog.CanBeMultiple);
        dialog.CreateNewFieldCommand.Execute(null);

        Assert.False(dialog.Fields[0].Definition.Multiple);
        Assert.True(dialog.Fields[0].IsBoolean);
    }

    [Fact]
    public async Task A_name_that_is_already_used_is_refused_with_a_pointer_to_the_list()
    {
        _store.Definitions["local:mood"] = new FieldDefinition("local:mood", FieldType.Text, Label: "Mood");
        var dialog = await Open();

        dialog.StartNewFieldCommand.Execute(null);
        dialog.NewLabel = "mood"; // any case
        dialog.CreateNewFieldCommand.Execute(null);

        Assert.Contains("already exists", dialog.NewFieldError);
        Assert.Empty(dialog.Fields);
        Assert.True(dialog.IsAddingNew); // the form stays open to fix it
    }

    [Fact]
    public async Task An_empty_or_overlong_name_is_refused()
    {
        var dialog = await Open();
        dialog.StartNewFieldCommand.Execute(null);

        dialog.NewLabel = "   ";
        dialog.CreateNewFieldCommand.Execute(null);
        Assert.Equal("Give the field a name.", dialog.NewFieldError);

        dialog.NewLabel = new string('x', 101);
        dialog.CreateNewFieldCommand.Execute(null);
        Assert.Contains("under 100", dialog.NewFieldError);
        Assert.Empty(dialog.Fields);
    }

    [Fact]
    public async Task Two_new_fields_with_names_that_make_the_same_key_get_different_keys()
    {
        var dialog = await Open();
        foreach (var name in new[] { "Настроение", "Эмоция" }) // neither can be written as a-z
        {
            dialog.StartNewFieldCommand.Execute(null);
            dialog.NewLabel = name;
            dialog.CreateNewFieldCommand.Execute(null);
        }

        Assert.Equal(["local:field", "local:field-2"], dialog.Fields.Select(f => f.Definition.Key));
    }

    [Fact]
    public async Task An_existing_field_can_be_added_from_the_list_and_leaves_the_list()
    {
        _store.Definitions["local:bpm"] = new FieldDefinition("local:bpm", FieldType.Number, Label: "Tempo");
        var dialog = await Open();

        dialog.SelectedAvailable = dialog.AvailableFields.Single();
        Assert.True(dialog.AddSelectedCommand.CanExecute(null));
        dialog.AddSelectedCommand.Execute(null);
        dialog.Fields[0].Text = "128";
        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Empty(dialog.AvailableFields);
        Assert.Equal(["set local:bpm"], _store.Calls); // the definition existed already
        Assert.Equal("128", _store.Values[(Song.RecordingId, "local:bpm")].GetRawText());
    }

    [Fact]
    public async Task Removing_a_stored_field_deletes_it_on_save_and_not_before()
    {
        _store.Definitions["local:mood"] = new FieldDefinition("local:mood", FieldType.Text);
        _store.Values[(Song.RecordingId, "local:mood")] = Json("\"calm\"");
        var dialog = await Open();

        dialog.RemoveFieldCommand.Execute(dialog.Fields[0]);

        Assert.False(dialog.HasFields);
        Assert.Single(_store.Values); // still there until Save
        Assert.Single(dialog.AvailableFields); // and can be added back

        await dialog.SaveCommand.ExecuteAsync(null);
        Assert.Empty(_store.Values);
        Assert.Equal(["remove local:mood"], _store.Calls);
    }

    [Fact]
    public async Task A_removed_field_that_is_added_back_is_left_as_it_was()
    {
        _store.Definitions["local:mood"] = new FieldDefinition("local:mood", FieldType.Text);
        _store.Values[(Song.RecordingId, "local:mood")] = Json("\"calm\"");
        var dialog = await Open();
        dialog.RemoveFieldCommand.Execute(dialog.Fields[0]);

        dialog.SelectedAvailable = dialog.AvailableFields.Single();
        dialog.AddSelectedCommand.Execute(null);
        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.True(dialog.HasFields);
        Assert.Empty(_store.Calls);
        Assert.Equal("calm", _store.Values[(Song.RecordingId, "local:mood")].GetString());
    }

    [Fact]
    public async Task Clearing_the_text_of_a_stored_field_removes_it()
    {
        _store.Definitions["local:mood"] = new FieldDefinition("local:mood", FieldType.Text);
        _store.Values[(Song.RecordingId, "local:mood")] = Json("\"calm\"");
        var dialog = await Open();

        dialog.Fields[0].Text = "";
        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["remove local:mood"], _store.Calls);
    }

    [Fact]
    public async Task A_mistake_in_one_field_writes_nothing_and_keeps_the_dialog_open()
    {
        _store.Definitions["local:mood"] = new FieldDefinition("local:mood", FieldType.Text);
        _store.Definitions["local:bpm"] = new FieldDefinition("local:bpm", FieldType.Number);
        _store.Values[(Song.RecordingId, "local:mood")] = Json("\"calm\"");
        _store.Values[(Song.RecordingId, "local:bpm")] = Json("100");
        var dialog = await Open();
        var closed = false;
        dialog.Closed += (_, _) => closed = true;

        dialog.Fields.Single(f => f.Definition.Key == "local:mood").Text = "tense"; // fine
        dialog.Fields.Single(f => f.Definition.Key == "local:bpm").Text = "fast";   // not a number
        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Calls); // the good edit was not written either
        Assert.False(closed);
        Assert.Equal("Needs a number.", dialog.Fields.Single(f => f.Definition.Key == "local:bpm").Error);
        Assert.Null(dialog.Fields.Single(f => f.Definition.Key == "local:mood").Error);

        dialog.Fields.Single(f => f.Definition.Key == "local:bpm").Text = "128"; // fixed
        await dialog.SaveCommand.ExecuteAsync(null);
        Assert.True(closed);
        Assert.Equal(2, _store.Calls.Count);
    }

    [Fact]
    public async Task A_store_that_refuses_the_save_shows_its_message_and_keeps_the_dialog_open()
    {
        _store.Definitions["local:mood"] = new FieldDefinition("local:mood", FieldType.Text);
        _store.Values[(Song.RecordingId, "local:mood")] = Json("\"calm\"");
        var dialog = await Open();
        var closed = false;
        dialog.Closed += (_, _) => closed = true;
        _store.SetFailure = new InvalidOperationException("That song is not in the library.");

        dialog.Fields[0].Text = "tense";
        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal("That song is not in the library.", dialog.Error);
        Assert.False(closed);
        Assert.False(dialog.IsBusy);
    }

    [Fact]
    public async Task Songs_open_the_dialog_from_their_menu_and_it_closes_itself()
    {
        var player = new PlayerViewModel(new FakeAudioPlayer());
        var songs = new SongsViewModel(new FakeLibrary(), new FakeScanner(), new FakeFolderPicker(), player,
            track => new PropertiesViewModel(_store, _fileTags, track));
        Assert.Null(songs.Properties);

        await songs.ShowPropertiesCommand.ExecuteAsync(Song);

        Assert.NotNull(songs.Properties);
        Assert.Equal("Example Song", songs.Properties.Title);

        songs.Properties.CancelCommand.Execute(null);
        Assert.Null(songs.Properties);
    }

    [Fact]
    public void Without_a_properties_factory_the_menu_entry_is_unavailable()
    {
        var songs = new SongsViewModel(new FakeLibrary(), new FakeScanner(), new FakeFolderPicker(), new PlayerViewModel(new FakeAudioPlayer()));

        Assert.False(songs.ShowPropertiesCommand.CanExecute(Song));
    }
}

internal sealed class FakeMetadataStore : IMetadataStore
{
    public Dictionary<string, FieldDefinition> Definitions { get; } = [];

    public Dictionary<(Guid, string), JsonElement> Values { get; } = [];

    /// <summary>Every write, in order: "define key", "set key", "remove key".</summary>
    public List<string> Calls { get; } = [];

    public Exception? Failure { get; set; }

    public Exception? SetFailure { get; set; }

    public Task<IReadOnlyList<FieldDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        if (Failure is not null) throw Failure;
        return Task.FromResult<IReadOnlyList<FieldDefinition>>([.. Definitions.Values]);
    }

    public Task SaveDefinitionAsync(FieldDefinition definition, CancellationToken cancellationToken = default)
    {
        Calls.Add($"define {definition.Key}");
        Definitions[definition.Key] = definition;
        return Task.CompletedTask;
    }

    public Task RemoveDefinitionAsync(string key, CancellationToken cancellationToken = default)
    {
        Calls.Add($"undefine {key}");
        Definitions.Remove(key);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FieldValue>> GetFieldsAsync(Guid recordingId, CancellationToken cancellationToken = default)
    {
        if (Failure is not null) throw Failure;
        return Task.FromResult<IReadOnlyList<FieldValue>>([.. Values
            .Where(v => v.Key.Item1 == recordingId)
            .Select(v => new FieldValue(Definitions[v.Key.Item2], v.Value))]);
    }

    public Task SetFieldAsync(Guid recordingId, string key, JsonElement value, CancellationToken cancellationToken = default)
    {
        if (SetFailure is not null) throw SetFailure;
        Calls.Add($"set {key}");
        Values[(recordingId, key)] = value;
        return Task.CompletedTask;
    }

    public Task RemoveFieldAsync(Guid recordingId, string key, CancellationToken cancellationToken = default)
    {
        Calls.Add($"remove {key}");
        Values.Remove((recordingId, key));
        return Task.CompletedTask;
    }
}

internal sealed class FakeFileTags : IFileTags
{
    public IReadOnlyList<FileTag> Rows { get; set; } = [];

    public Exception? Failure { get; set; }

    public Task<IReadOnlyList<FileTag>> ReadFileTagsAsync(Guid recordingId, CancellationToken cancellationToken = default)
    {
        if (Failure is not null) throw Failure;
        return Task.FromResult(Rows);
    }
}
