using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Shirabe.Core.Metadata;
using Shirabe.Metadata;

namespace Shirabe.Client.Studio;

/// <summary>Choosing the song an opened document is applied to, then seeing exactly what applying would do (see <see cref="ApplyViewModel"/>).</summary>
public partial class StudioApplyViewModel : ViewModelBase
{
    /// <summary>A long library would make the list slow to build for no benefit: typing narrows it.</summary>
    private const int MaxListed = 300;

    private readonly IDocumentStore _store;
    private readonly IReadOnlyList<TrackSummary> _all;
    private readonly MetadataDocument _document;

    public StudioApplyViewModel(IDocumentStore store, IReadOnlyList<TrackSummary> songs, MetadataDocument document, string heading)
    {
        _store = store;
        _all = songs;
        _document = document;
        Heading = heading;
        Songs = Filtered("");

        // A document exported from this library names the recording it is about by its id.
        if (Guid.TryParse(document.SubjectId, out var id) && songs.FirstOrDefault(t => t.RecordingId == id) is { } about)
        {
            SelectedSong = about;
            Hint = "This document says it is about this song.";
        }
    }

    public string Heading { get; }

    [ObservableProperty]
    public partial string? Hint { get; set; }

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<TrackSummary> Songs { get; set; }

    [ObservableProperty]
    public partial TrackSummary? SelectedSong { get; set; }

    /// <summary>What applying the document to the chosen song would do; null until a song is chosen.</summary>
    [ObservableProperty]
    public partial ApplyViewModel? Preview { get; set; }

    /// <summary>Completes when the preview of the current choice has been worked out.</summary>
    public Task PreviewLoading { get; private set; } = Task.CompletedTask;

    public event EventHandler? Closed;

    partial void OnFilterChanged(string value) => Songs = Filtered(value);

    partial void OnSelectedSongChanged(TrackSummary? value)
    {
        Hint = null;
        if (value is null)
        {
            Preview = null;
            return;
        }

        var preview = new ApplyViewModel(_store, value, _document);
        preview.Closed += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
        Preview = preview;
        PreviewLoading = preview.LoadAsync();
    }

    [RelayCommand]
    private void Close() => Closed?.Invoke(this, EventArgs.Empty);

    private IReadOnlyList<TrackSummary> Filtered(string filter)
    {
        var terms = filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return [.. _all
            .Where(t => terms.All(term =>
                $"{t.Title} {t.Artist} {t.Album}".Contains(term, StringComparison.CurrentCultureIgnoreCase)))
            .Take(MaxListed)];
    }
}
