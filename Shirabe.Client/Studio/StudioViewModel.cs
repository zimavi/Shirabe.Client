using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Shirabe.Core.Metadata;
using Shirabe.Metadata;

namespace Shirabe.Client.Studio;

/// <summary>
/// Studio: works with Shirabe Metadata documents. For now only on this computer: open a <c>.shirabe.json</c> file, see
/// whether it follows the standard and what it says, and apply it to a song of the library. Nothing is uploaded.
/// </summary>
/// <param name="library">The songs a document can be applied to. Null: documents can only be looked at.</param>
/// <param name="documents">Where applying puts the document. Null: documents can only be looked at.</param>
public partial class StudioViewModel(IFilePicker filePicker, ILibrary? library = null, IDocumentStore? documents = null) : ViewModelBase
{
    /// <summary>Larger files are refused: a document is a few kilobytes, and this keeps a wrong file from being read whole.</summary>
    private const long MaxFileBytes = 4 * 1024 * 1024;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocuments), nameof(ShowEmptyState))]
    public partial IReadOnlyList<StudioDocumentViewModel> Documents { get; set; } = [];

    /// <summary>The file the documents come from.</summary>
    [ObservableProperty]
    public partial string? FilePath { get; set; }

    /// <summary>Why the file could not be opened at all.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(ShowEmptyState))]
    public partial string? Error { get; set; }

    /// <summary>The dialog for choosing the song to apply a document to, while it is open.</summary>
    [ObservableProperty]
    public partial StudioApplyViewModel? Applying { get; set; }

    public bool HasDocuments => Documents.Count > 0;

    public bool HasError => Error is not null;

    public bool ShowEmptyState => !HasDocuments && !HasError;

    [RelayCommand]
    private async Task OpenAsync()
    {
        var path = await filePicker.PickOpenAsync("Open a metadata document", "Shirabe metadata", "*.json");
        if (path is not null) await OpenPathAsync(path);
    }

    /// <summary>Opens and checks the documents in a file. A file that cannot be read is reported, never thrown.</summary>
    public async Task OpenPathAsync(string path)
    {
        Error = null;
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes)
            {
                Fail(path, $"The file is larger than {MaxFileBytes / 1024 / 1024} MB, which is far more than a metadata document.");
                return;
            }

            var json = await File.ReadAllTextAsync(path);
            if (!MetadataDocument.TryParseFile(json, out var found, out var error))
            {
                Fail(path, error!);
                return;
            }

            FilePath = path;
            var canApply = library is not null && documents is not null;
            Documents = [.. found.Select(d => new StudioDocumentViewModel(d, canApply))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail(path, ex is FileNotFoundException ? "The file was not found." : ex.Message);
        }
    }

    /// <summary>Opens the dialog for choosing the song a document is applied to.</summary>
    [RelayCommand]
    private async Task ApplyToSongAsync(StudioDocumentViewModel? document)
    {
        if (document is not { CanApply: true } || library is null || documents is null) return;

        IReadOnlyList<TrackSummary> songs;
        try
        {
            songs = await library.GetTracksAsync();
        }
        catch (Exception ex)
        {
            Error = $"Could not load the library: {ex.Message}";
            return;
        }

        var dialog = new StudioApplyViewModel(documents, songs, document.Document, document.Heading);
        dialog.Closed += (_, _) => Applying = null;
        Applying = dialog;
    }

    private void Fail(string path, string message)
    {
        FilePath = path;
        Documents = [];
        Error = message;
    }
}
