using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shirabe.Client.Services;
using Shirabe.Core.Library;
using Shirabe.Core.Metadata;
using Shirabe.Metadata;

namespace Shirabe.Client.ViewModels;

/// <summary>
/// The properties of one song: its custom fields (editable) and what its file contains (read-only). Edits are staged and
/// written only by <see cref="SaveCommand"/>; <see cref="CancelCommand"/> and Escape write nothing.
/// </summary>
public partial class PropertiesViewModel : ViewModelBase
{
    /// <summary>The names of <see cref="FieldType"/> in order, as people say them.</summary>
    public static IReadOnlyList<string> TypeNames { get; } = ["Text", "Number", "Yes / no", "Date", "Link"];

    private const int BooleanTypeIndex = 2;
    private const int MaxLabelLength = 100;

    private readonly IMetadataStore _store;
    private readonly IFileTags _fileTags;
    private readonly IFilePicker? _filePicker;
    private readonly IDocumentStore? _documents;
    private IReadOnlyList<FieldDefinition> _definitions = [];

    /// <param name="filePicker">Where "Export" asks for a file. Null: no export (tests).</param>
    /// <param name="documents">The documents that came with the song's files. Null: the section is not shown (tests).</param>
    public PropertiesViewModel(
        IMetadataStore store, IFileTags fileTags, TrackSummary track, IFilePicker? filePicker = null, IDocumentStore? documents = null)
    {
        _store = store;
        _fileTags = fileTags;
        _filePicker = filePicker;
        _documents = documents;
        Track = track;
    }

    public bool HasExport => _filePicker is not null;

    public TrackSummary Track { get; }

    public string Title => Track.Title;

    public string Subtitle => string.Join(" · ", new[] { Track.Artist, Track.Album }.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>Raised when the dialog should close: after a save, and on cancel.</summary>
    public event EventHandler? Closed;

    public ObservableCollection<FieldEditorViewModel> Fields { get; } = [];

    [ObservableProperty]
    public partial bool HasFields { get; set; }

    /// <summary>Fields that exist in the library but not on this song, offered in the "add" list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvailableFields))]
    public partial IReadOnlyList<FieldDefinition> AvailableFields { get; set; } = [];

    public bool HasAvailableFields => AvailableFields.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddSelectedCommand))]
    public partial FieldDefinition? SelectedAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsAddingNew { get; set; }

    [ObservableProperty]
    public partial string NewLabel { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBeMultiple))]
    public partial int NewTypeIndex { get; set; }

    [ObservableProperty]
    public partial bool NewMultiple { get; set; }

    /// <summary>A yes/no field holds one value; the others can hold several.</summary>
    public bool CanBeMultiple => NewTypeIndex != BooleanTypeIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewFieldError))]
    public partial string? NewFieldError { get; set; }

    public bool HasNewFieldError => NewFieldError is not null;

    [ObservableProperty]
    public partial IReadOnlyList<FileTag> Tags { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<FileTag> Audio { get; set; } = [];

    /// <summary>Why the file could not be read, shown in place of its tags.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTagsError))]
    public partial string? TagsError { get; set; }

    public bool HasTagsError => TagsError is not null;

    /// <summary>Why the last save did not happen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    /// <summary>The documents of this song: found beside the audio, in its tag, or attached by the person.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocuments), nameof(HasDocumentsSection))]
    public partial IReadOnlyList<FileDocumentViewModel> Documents { get; set; } = [];

    public bool HasDocuments => Documents.Count > 0;

    /// <summary>What could not be read from the song's files, or why documents could not be loaded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocumentsNotice), nameof(HasDocumentsSection))]
    public partial string? DocumentsNotice { get; set; }

    public bool HasDocumentsNotice => DocumentsNotice is not null;

    public bool HasDocumentsSection => HasDocuments || HasDocumentsNotice;

    /// <summary>The panel that shows what copying a document into the person's fields would do, while one is open.</summary>
    [ObservableProperty]
    public partial ApplyViewModel? Apply { get; set; }

    /// <summary>What an export just did, in a sentence.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? Notice { get; set; }

    public bool HasNotice => Notice is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsBusy { get; set; }

    public async Task LoadAsync()
    {
        await LoadFieldsAsync();

        try
        {
            var rows = await _fileTags.ReadFileTagsAsync(Track.RecordingId);
            Tags = [.. rows.Where(r => r.Group == FileTag.TagsGroup)];
            Audio = [.. rows.Where(r => r.Group == FileTag.AudioGroup)];
        }
        catch (FileNotFoundException)
        {
            TagsError = "The file was moved or deleted. Add its folder again to update the library.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            TagsError = $"Could not read the file's tags: {ex.Message}";
        }

        await LoadDocumentsAsync();
    }

    private async Task LoadFieldsAsync()
    {
        Fields.Clear();
        try
        {
            _definitions = await _store.GetDefinitionsAsync();
            foreach (var field in (await _store.GetFieldsAsync(Track.RecordingId)).OrderBy(f => f.Definition.DisplayName, StringComparer.CurrentCultureIgnoreCase))
                Fields.Add(new FieldEditorViewModel(field.Definition, field.Value, isNewDefinition: false));
        }
        catch (Exception ex)
        {
            // A broken database must not take the app down: say what happened.
            Error = $"Could not load the custom fields: {ex.Message}";
        }

        Refresh();
    }

    /// <summary>Reads the song's sidecar and tag now (the person may have edited them since the last scan), then shows what is stored.</summary>
    private async Task LoadDocumentsAsync()
    {
        if (_documents is null) return;

        try
        {
            var problems = await _documents.RefreshFromFileAsync(Track.RecordingId);
            Documents = [.. (await _documents.GetDocumentsAsync(Track.RecordingId)).Select(d => new FileDocumentViewModel(d))];
            DocumentsNotice = problems.Count > 0 ? string.Join('\n', problems) : null;
        }
        catch (Exception ex)
        {
            DocumentsNotice = $"Could not load the documents of this song: {ex.Message}";
        }
    }

    /// <summary>Anything typed, added or removed in the dialog that has not been saved.</summary>
    private bool HasUnsavedChanges => Fields.Any(f => f.IsRemoved || f.IsNewDefinition || f.IsChanged);

    /// <summary>Shows what copying a personal document that came with the song into the person's own fields would do.</summary>
    [RelayCommand]
    private async Task CopyDocumentAsync(FileDocumentViewModel? document)
    {
        if (_documents is null || document?.Document is null) return;

        // Copying reloads the fields, which would throw away what is typed: ask first instead of losing it.
        if (HasUnsavedChanges)
        {
            Error = "Save or cancel your edits first, so the copied fields do not get mixed up with them.";
            return;
        }

        Error = null;
        var apply = new ApplyViewModel(_documents, Track, document.Document, LoadFieldsAsync);
        apply.Closed += (_, _) =>
        {
            if (ReferenceEquals(Apply, apply)) Apply = null;
        };
        Apply = apply;
        await apply.LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanAddSelected))]
    private void AddSelected()
    {
        if (SelectedAvailable is not { } definition) return;

        // A field removed a moment ago comes back as it was; otherwise it is a new, empty row.
        if (Fields.FirstOrDefault(f => f.Definition.Key == definition.Key) is { } removed) removed.IsRemoved = false;
        else Fields.Add(new FieldEditorViewModel(definition, null, isNewDefinition: false));
        Refresh();
    }

    private bool CanAddSelected() => SelectedAvailable is not null;

    [RelayCommand]
    private void StartNewField()
    {
        NewLabel = "";
        NewTypeIndex = 0;
        NewMultiple = false;
        NewFieldError = null;
        IsAddingNew = true;
    }

    [RelayCommand]
    private void CancelNewField() => IsAddingNew = false;

    [RelayCommand]
    private void CreateNewField()
    {
        var label = NewLabel.Trim();
        if (label.Length == 0)
        {
            NewFieldError = "Give the field a name.";
            return;
        }

        if (label.Length > MaxLabelLength)
        {
            NewFieldError = $"Keep the name under {MaxLabelLength} characters.";
            return;
        }

        var names = _definitions.Select(d => d.DisplayName).Concat(Fields.Select(f => f.Name));
        if (names.Any(n => string.Equals(n, label, StringComparison.CurrentCultureIgnoreCase)))
        {
            NewFieldError = $"A field called \"{label}\" already exists. Pick it from the list instead.";
            return;
        }

        var key = FieldKey.FromLabel(label, _definitions.Select(d => d.Key).Concat(Fields.Select(f => f.Definition.Key)));
        var definition = new FieldDefinition(key, (FieldType)NewTypeIndex, NewMultiple && CanBeMultiple, label);
        Fields.Add(new FieldEditorViewModel(definition, null, isNewDefinition: true));
        IsAddingNew = false;
        Refresh();
    }

    [RelayCommand]
    private void RemoveField(FieldEditorViewModel? field)
    {
        if (field is null) return;

        // A value that is only in this dialog just goes away; a stored one is deleted when saving.
        if (field.HadStoredValue) field.IsRemoved = true;
        else Fields.Remove(field);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        Error = null;

        // Check everything first, so a mistake in one field writes nothing at all.
        var plan = new List<(FieldEditorViewModel Field, JsonElement? Value)>();
        var valid = true;
        foreach (var field in Fields)
        {
            field.Error = null;
            if (field.IsRemoved)
            {
                plan.Add((field, null));
                continue;
            }

            if (!field.IsNewDefinition && !field.IsChanged) continue;

            if (!field.TryBuild(out var value, out var error))
            {
                field.Error = error;
                valid = false;
            }
            else if (!(field.IsNewDefinition && value is null)) // a new field nothing was typed into is simply not created
            {
                plan.Add((field, value));
            }
        }

        if (!valid) return;

        IsBusy = true;
        try
        {
            foreach (var (field, value) in plan)
            {
                if (field.IsNewDefinition) await _store.SaveDefinitionAsync(field.Definition);
                if (value is { } v) await _store.SetFieldAsync(Track.RecordingId, field.Definition.Key, v);
                else await _store.RemoveFieldAsync(Track.RecordingId, field.Definition.Key);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Error = ex.Message;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    private bool CanSave() => !IsBusy;

    /// <summary>Saves what is in this dialog as a metadata document, wherever the person chooses. Nothing leaves the computer.</summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_filePicker is null || BuildDocument() is not { } document) return;

        var path = await _filePicker.PickSaveAsync(
            "Export the custom fields of this song", $"{FileNameOf(Title)}{SidecarFile.Extension}", "Shirabe metadata", "*.json");
        if (path is null) return;

        try
        {
            await File.WriteAllTextAsync(path, document.ToJson());
            Notice = $"Exported to {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = $"Could not write {path}: {ex.Message}";
        }
    }

    /// <summary>
    /// Writes the custom fields into the sidecar file beside the audio file. Documents of other layers already in that file
    /// (an artist's own) are kept; only the personal one is replaced. Always an explicit act: folders get shared, and this
    /// puts the person's notes into the folder.
    /// </summary>
    [RelayCommand]
    private async Task SaveSidecarAsync()
    {
        if (BuildDocument() is not { } document) return;

        if (!Uri.TryCreate(Track.MediaUri, UriKind.Absolute, out var location) || !location.IsFile)
        {
            Error = "This song is not a file on this computer, so there is no place to put a sidecar.";
            return;
        }

        var sidecar = SidecarFile.PathFor(location.LocalPath);
        try
        {
            var existing = File.Exists(sidecar) ? await File.ReadAllTextAsync(sidecar) : null;
            if (!SidecarFile.TryMerge(existing, document, out var json, out var problem))
            {
                Error = problem;
                return;
            }

            var temp = sidecar + ".tmp"; // write beside, then swap, so a crash cannot leave half a file
            await File.WriteAllTextAsync(temp, json);
            File.Move(temp, sidecar, overwrite: true);
            Notice = $"Saved next to the audio file as {Path.GetFileName(sidecar)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = $"Could not write {sidecar}: {ex.Message}";
        }
    }

    /// <summary>The personal document for what the dialog shows now, or null (with the reason shown) when it cannot be built.</summary>
    private MetadataDocument? BuildDocument()
    {
        Error = null;
        Notice = null;

        var definitions = new List<FieldDefinition>();
        var values = new List<KeyValuePair<string, JsonElement>>();
        var valid = true;
        foreach (var field in Fields.Where(f => !f.IsRemoved))
        {
            field.Error = null;
            if (!field.TryBuild(out var value, out var error))
            {
                field.Error = error;
                valid = false;
            }
            else if (value is { } v)
            {
                definitions.Add(field.Definition);
                values.Add(KeyValuePair.Create(field.Definition.Key, v));
            }
        }

        if (!valid) return null;
        if (values.Count == 0)
        {
            Error = "There are no custom fields to export.";
            return null;
        }

        return MetadataDocument.Create(Track.RecordingId, "personal", "user", DateTime.UtcNow, definitions, values);
    }

    private static string FileNameOf(string title)
    {
        var name = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        return name.Length == 0 ? "song" : name;
    }

    [RelayCommand]
    private void Cancel() => Closed?.Invoke(this, EventArgs.Empty);

    private void Refresh()
    {
        var shown = Fields.Where(f => !f.IsRemoved).ToList();
        HasFields = shown.Count > 0;
        AvailableFields = [.. _definitions
            .Where(d => shown.All(f => f.Definition.Key != d.Key))
            .OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
        SelectedAvailable = null;
    }
}
