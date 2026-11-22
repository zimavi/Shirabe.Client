using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Shirabe.Core.Media;
using Xunit;

namespace Shirabe.Tests;

public class SongsViewModelTests
{
    private readonly FakeLibrary _library = new();
    private readonly FakeScanner _scanner = new();
    private readonly FakeFolderPicker _picker = new();
    private readonly FakeAudioPlayer _audio = new();
    private readonly SongsViewModel _viewModel;

    public SongsViewModelTests()
    {
        SynchronizationContext.SetSynchronizationContext(null); // run UI-thread hops inline
        _viewModel = new SongsViewModel(_library, _scanner, _picker, new PlayerViewModel(_audio));
    }

    [Fact]
    public async Task LoadAsync_shows_the_tracks_and_a_summary()
    {
        _library.Tracks = [Track("One"), Track("Two")];

        await _viewModel.LoadAsync();

        Assert.Equal(2, _viewModel.Tracks.Count);
        Assert.True(_viewModel.HasTracks);
        Assert.Equal("2 songs", _viewModel.Summary);
        Assert.False(_viewModel.ShowEmptyState);
    }

    [Fact]
    public async Task A_single_track_is_summarised_in_the_singular()
    {
        _library.Tracks = [Track("One")];

        await _viewModel.LoadAsync();

        Assert.Equal("1 song", _viewModel.Summary);
    }

    [Fact]
    public async Task An_empty_library_shows_the_empty_state()
    {
        await _viewModel.LoadAsync();

        Assert.False(_viewModel.HasTracks);
        Assert.True(_viewModel.ShowEmptyState);
    }

    [Fact]
    public async Task A_library_that_fails_to_load_shows_a_message_and_keeps_the_old_list()
    {
        _library.Tracks = [Track("One")];
        await _viewModel.LoadAsync();
        _library.Failure = new InvalidOperationException("disk error");

        await _viewModel.LoadAsync();

        Assert.Contains("disk error", _viewModel.ErrorMessage);
        Assert.Single(_viewModel.Tracks);
    }

    [Fact]
    public async Task Adding_a_folder_scans_it_summarises_the_result_and_reloads_the_list()
    {
        _picker.Choice = Path.Combine(Path.GetTempPath(), "Music");
        _scanner.Result = new ScanResult(3, 1, 10, 2, [new ScanFailure("x.flac", "broken")]);
        _library.Tracks = [Track("One"), Track("Two"), Track("Three")];

        await _viewModel.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal(_picker.Choice, _scanner.ScannedFolder);
        Assert.Equal("Scan finished: 3 added, 1 updated, 10 unchanged, 2 no longer found, 1 could not be read", _viewModel.ScanText);
        Assert.Equal(3, _viewModel.Tracks.Count);
        Assert.False(_viewModel.IsScanning);
    }

    [Fact]
    public async Task Dismissing_the_folder_dialog_does_nothing()
    {
        _picker.Choice = null;

        await _viewModel.AddFolderCommand.ExecuteAsync(null);

        Assert.Null(_scanner.ScannedFolder);
        Assert.Null(_viewModel.ScanText);
        Assert.Equal(0, _library.LoadCount);
    }

    [Fact]
    public async Task Scan_progress_is_exposed_while_scanning()
    {
        _picker.Choice = "folder";
        (int, int, string?)? seenWhileScanning = null;
        _scanner.DuringScan = progress =>
        {
            progress!.Report(new ScanProgress(5, 10));
            seenWhileScanning = (_viewModel.ScanProcessed, _viewModel.ScanTotal, _viewModel.ScanText);
        };

        await _viewModel.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal((5, 10, "Scanning 5 of 10"), seenWhileScanning);
    }

    [Fact]
    public async Task A_folder_that_cannot_be_scanned_shows_an_error()
    {
        _picker.Choice = "missing-folder";
        _scanner.Failure = new DirectoryNotFoundException("not there");

        await _viewModel.AddFolderCommand.ExecuteAsync(null);

        Assert.Contains("missing-folder", _viewModel.ErrorMessage);
        Assert.Contains("not there", _viewModel.ErrorMessage);
        Assert.False(_viewModel.IsScanning);
    }

    [Fact]
    public async Task Cancelling_stops_the_scan_and_still_reloads_what_was_found()
    {
        _picker.Choice = "folder";
        _scanner.WaitForCancellation = true;

        var run = _viewModel.AddFolderCommand.ExecuteAsync(null);
        await _scanner.Started.Task;
        Assert.True(_viewModel.IsScanning);
        Assert.False(_viewModel.AddFolderCommand.CanExecute(null)); // no second scan at the same time

        _viewModel.CancelScanCommand.Execute(null);
        await run;

        Assert.Equal("Scan cancelled", _viewModel.ScanText);
        Assert.False(_viewModel.IsScanning);
        Assert.Equal(1, _library.LoadCount);
    }

    [Fact]
    public async Task Playing_a_row_plays_that_track()
    {
        var path = Path.Combine(Path.GetTempPath(), "one.flac");
        var track = Track("One") with { MediaUri = new Uri(path).AbsoluteUri };

        await _viewModel.PlayCommand.ExecuteAsync(track);

        Assert.Equal(path, Assert.IsType<LocalFileSource>(_audio.Loaded).Path);
    }

    private static TrackSummary Track(string title) =>
        new(Guid.NewGuid(), title, "Artist", "Album", TimeSpan.FromMinutes(3), $"file:///music/{title}.flac");
}
