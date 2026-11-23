using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Shirabe.Core.Metadata;
using Shirabe.Metadata;

namespace Shirabe.Client.Studio;

/// <summary>
/// Edits one metadata document as JSON, checked against the standard on every keystroke. It can be saved as a file anywhere, as
/// the sidecar of a song, or into the audio file's own tag (after an explicit confirmation); the song is the one chosen as the
/// <see cref="Target"/>. Only a document that has no errors can be saved: a half-written one stays in the editor.
/// </summary>
public partial class StudioEditorViewModel : ViewModelBase
{
    private readonly IFilePicker _picker;
    private readonly IDocumentStore? _store;
    private readonly ILibrary? _library;

    /// <param name="store">Where documents are written for a song. Null: documents can only be saved as files.</param>
    /// <param name="library">The songs a target can be chosen from. Null: the target cannot be changed.</param>
    /// <param name="heading">What is being edited, in a few words.</param>
    public StudioEditorViewModel(
        IFilePicker picker, IDocumentStore? store, ILibrary? library, string heading, string json, TrackSummary? target)
    {
        _picker = picker;
        _store = store;
        _library = library;
        Heading = heading;
        Target = target;
        Text = json; // validated at once
    }

    public string Heading { get; }

    /// <summary>The document, as JSON. Every change is checked.</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = "";

    /// <summary>The song the document is saved to as a sidecar or into the file, and applied to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetText), nameof(CanChooseSong))]
    [NotifyCanExecuteChangedFor(nameof(SaveAsSidecarCommand), nameof(StartEmbedCommand), nameof(ApplyToLibraryCommand))]
    public partial TrackSummary? Target { get; set; }

    public string TargetText => Target is { } song ? $"Song: {song.Title}" : "No song chosen: it can still be saved as a file.";

    public bool CanChooseSong => _library is not null && _store is not null;

    [ObservableProperty]
    public partial IReadOnlyList<DocumentProblem> Problems { get; set; } = [];

    public bool HasProblems => Problems.Count > 0;

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    /// <summary>The text is one document and breaks no rule of the standard: it can be saved.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAsFileCommand), nameof(SaveAsSidecarCommand), nameof(StartEmbedCommand), nameof(ApplyToLibraryCommand))]
    public partial bool IsValid { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? Notice { get; set; }

    public bool HasNotice => Notice is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    /// <summary>Asking, before the audio file itself is changed.</summary>
    [ObservableProperty]
    public partial bool ConfirmingEmbed { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>The panel that shows what applying the document to the song would do, while one is open.</summary>
    [ObservableProperty]
    public partial ApplyViewModel? Apply { get; set; }

    /// <summary>The dialog for choosing the song, while it is open.</summary>
    [ObservableProperty]
    public partial SongPickerViewModel? Picking { get; set; }

    /// <summary>The audio file's name, for the confirmation sentence.</summary>
    public string TargetFileName => Target is { } song && Uri.TryCreate(song.MediaUri, UriKind.Absolute, out var uri) && uri.IsFile
        ? Path.GetFileName(uri.LocalPath)
        : "the audio file";

    public event EventHandler? Closed;

    partial void OnTextChanged(string value) => Validate();

    private void Validate()
    {
        Notice = null;
        Error = null;
        ConfirmingEmbed = false; // what was confirmed is no longer what is in the editor

        if (!MetadataDocument.TryParseFile(Text, out var documents, out var parseError))
        {
            Problems = [];
            Status = parseError!;
            IsValid = false;
        }
        else if (documents.Count != 1)
        {
            Problems = [];
            Status = "The editor works on one document at a time: remove the list around it.";
            IsValid = false;
        }
        else
        {
            Problems = documents[0].Validate();
            var errors = Problems.Count(p => p.IsError);
            Status = StandardStatus.Describe(errors, Problems.Count - errors);
            IsValid = errors == 0;
        }

        OnPropertyChanged(nameof(HasProblems));
    }

    /// <summary>A fresh copy of the document in the editor: writing changes the copy (a revision goes up), never the text.</summary>
    private bool TryGetDocument(out MetadataDocument document)
    {
        document = null!;
        if (!MetadataDocument.TryParseFile(Text, out var documents, out _) || documents.Count != 1) return false;

        document = documents[0];
        return true;
    }

    [RelayCommand]
    private void Format()
    {
        if (TryGetDocument(out var document)) Text = document.ToJson();
    }

    [RelayCommand(CanExecute = nameof(IsValid))]
    private async Task SaveAsFileAsync()
    {
        if (!TryGetDocument(out var document)) return;

        var title = document.Title is { Length: > 0 } t ? t : "metadata";
        var name = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        var path = await _picker.PickSaveAsync("Save this document", $"{(name.Length == 0 ? "metadata" : name)}{SidecarFile.Extension}", "Shirabe metadata", "*.json");
        if (path is null) return;

        Notice = null;
        Error = null;
        try
        {
            await File.WriteAllTextAsync(path, document.ToJson());
            Notice = $"Saved to {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = $"Could not write {path}: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanWriteToSong))]
    private Task SaveAsSidecarAsync() => WriteAsync(DocumentOrigin.Sidecar);

    /// <summary>The first step of writing into the audio file: ask, because the audio file itself is changed.</summary>
    [RelayCommand(CanExecute = nameof(CanWriteToSong))]
    private void StartEmbed()
    {
        OnPropertyChanged(nameof(TargetFileName));
        ConfirmingEmbed = true;
    }

    [RelayCommand]
    private async Task ConfirmEmbedAsync()
    {
        ConfirmingEmbed = false;
        if (CanWriteToSong()) await WriteAsync(DocumentOrigin.Embedded);
    }

    [RelayCommand]
    private void CancelEmbed() => ConfirmingEmbed = false;

    private bool CanWriteToSong() => IsValid && Target is not null && _store is not null;

    private async Task WriteAsync(string origin)
    {
        if (!TryGetDocument(out var document) || Target is null || _store is null) return;

        Notice = null;
        Error = null;
        IsBusy = true;
        try
        {
            Notice = await _store.WriteToFileAsync(Target.RecordingId, document, origin);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ChooseSongAsync()
    {
        if (_library is null || _store is null) return;

        IReadOnlyList<TrackSummary> songs;
        try
        {
            songs = await _library.GetTracksAsync();
        }
        catch (Exception ex)
        {
            Error = $"Could not load the library: {ex.Message}";
            return;
        }

        var picker = new SongPickerViewModel(songs, "Choose the song", "Choose", chooseLayer: false);
        picker.Picked += (_, _) =>
        {
            Target = picker.SelectedSong;
            Picking = null;
        };
        picker.Closed += (_, _) => Picking = null;
        Picking = picker;
    }

    /// <summary>Shows what applying the document to the song would do (personal: your own fields; others: kept as a layer).</summary>
    [RelayCommand(CanExecute = nameof(CanWriteToSong))]
    private async Task ApplyToLibraryAsync()
    {
        if (!TryGetDocument(out var document) || Target is null || _store is null) return;

        var apply = new ApplyViewModel(_store, Target, document);
        apply.Closed += (_, _) =>
        {
            if (ReferenceEquals(Apply, apply)) Apply = null;
        };
        Apply = apply;
        await apply.LoadAsync();
    }

    [RelayCommand]
    private void Close() => Closed?.Invoke(this, EventArgs.Empty);
}
