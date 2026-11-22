using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shirabe.Client.Services;
using Shirabe.Core.Library;

namespace Shirabe.Client.ViewModels;

/// <summary>Every playable recording in the library, plus adding music by scanning a folder.</summary>
public partial class SongsViewModel(
    ILibrary library,
    IFolderScanner scanner,
    IFolderPicker folderPicker,
    PlayerViewModel player,
    Func<TrackSummary, PropertiesViewModel>? propertiesFactory = null) : ViewModelBase
{
    private CancellationTokenSource? _scanCancellation;
    private bool _loadFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTracks), nameof(Summary), nameof(ShowEmptyState))]
    public partial IReadOnlyList<TrackSummary> Tracks { get; set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFolderCommand))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial int ScanProcessed { get; set; }

    [ObservableProperty]
    public partial int ScanTotal { get; set; }

    /// <summary>Progress while scanning, then a summary of the last scan.</summary>
    [ObservableProperty]
    public partial string? ScanText { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>The open properties dialog, or null. It is shown over the page.</summary>
    [ObservableProperty]
    public partial PropertiesViewModel? Properties { get; set; }

    public bool HasTracks => Tracks.Count > 0;

    /// <summary>Not while scanning: the list is still empty then, and "No songs yet" would be wrong.</summary>
    public bool ShowEmptyState => !HasTracks && !IsScanning;

    public string Summary => Tracks.Count == 1 ? "1 song" : $"{Tracks.Count:N0} songs";

    public async Task LoadAsync()
    {
        try
        {
            Tracks = await library.GetTracksAsync();
            if (_loadFailed) ErrorMessage = null; // only clear a message that loading itself caused, not a scan error
            _loadFailed = false;
        }
        catch (Exception ex)
        {
            // A broken database must not take the app down: say what happened and keep playing.
            ErrorMessage = $"Could not load the library: {ex.Message}";
            _loadFailed = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddFolder))]
    private async Task AddFolderAsync()
    {
        var folder = await folderPicker.PickFolderAsync();
        if (folder is null) return;

        _scanCancellation = new CancellationTokenSource();
        IsScanning = true;
        ScanProcessed = 0;
        ScanTotal = 0;
        ScanText = "Looking for music...";
        ErrorMessage = null;

        // Reports come from the scanner's thread; marshal them to the UI thread (inline in tests).
        var progress = new CallbackProgress<ScanProgress>(p => OnUiThread(() =>
        {
            ScanProcessed = p.Processed;
            ScanTotal = p.Total;
            ScanText = $"Scanning {p.Processed:N0} of {p.Total:N0}";
        }));

        try
        {
            ScanText = Describe(await scanner.ScanFolderAsync(folder, progress, _scanCancellation.Token));
        }
        catch (OperationCanceledException)
        {
            ScanText = "Scan cancelled";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ScanText = null;
            ErrorMessage = $"Could not scan {folder}: {ex.Message}";
        }
        finally
        {
            _scanCancellation.Dispose();
            _scanCancellation = null;
            IsScanning = false;
        }

        await LoadAsync(); // also after a cancel: what was scanned so far is kept
    }

    private bool CanAddFolder() => !IsScanning;

    [RelayCommand]
    private void CancelScan() => _scanCancellation?.Cancel();

    /// <summary>Plays a song and queues the whole list after it, so playback carries on down the list.</summary>
    [RelayCommand]
    private async Task PlayAsync(TrackSummary? track)
    {
        if (track is null) return;

        var tracks = Tracks;
        var index = 0;
        while (index < tracks.Count && tracks[index].RecordingId != track.RecordingId) index++;

        // Not in the list (it changed under us): still play the song that was asked for.
        if (index == tracks.Count) await player.PlayTracksAsync([track], 0);
        else await player.PlayTracksAsync(tracks, index);
    }

    [RelayCommand]
    private async Task PlayNextAsync(TrackSummary? track)
    {
        if (track is not null) await player.PlayNextAsync(track);
    }

    [RelayCommand]
    private async Task AddToQueueAsync(TrackSummary? track)
    {
        if (track is not null) await player.EnqueueAsync(track);
    }

    /// <summary>Opens the properties dialog (custom fields and file tags) of a song, over this page.</summary>
    [RelayCommand(CanExecute = nameof(CanShowProperties))]
    private async Task ShowPropertiesAsync(TrackSummary? track)
    {
        if (track is null || propertiesFactory is null) return;

        var dialog = propertiesFactory(track);
        dialog.Closed += (_, _) => Properties = null;
        Properties = dialog;
        await dialog.LoadAsync();
    }

    private bool CanShowProperties() => propertiesFactory is not null;

    private static string Describe(ScanResult result)
    {
        var parts = new List<string> { $"{result.Added:N0} added", $"{result.Updated:N0} updated", $"{result.Unchanged:N0} unchanged" };
        if (result.MarkedMissing > 0) parts.Add($"{result.MarkedMissing:N0} no longer found");
        if (result.Failed.Count > 0) parts.Add($"{result.Failed.Count:N0} could not be read");
        if (result.DocumentProblems is { Count: > 0 } documents) parts.Add($"{documents.Count:N0} metadata document problems");
        return "Scan finished: " + string.Join(", ", parts);
    }

    private sealed class CallbackProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
