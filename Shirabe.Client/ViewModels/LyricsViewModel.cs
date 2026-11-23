using CommunityToolkit.Mvvm.ComponentModel;
using Shirabe.Core.Lyrics;

namespace Shirabe.Client.ViewModels;

/// <summary>One row of the lyrics panel.</summary>
public sealed partial class LyricLineViewModel(LyricLine line) : ObservableObject
{
    public LyricLine Line { get; } = line;

    /// <summary>A pause between lines (an empty line) is shown as a gap, not as text.</summary>
    public string Text => Line.Text;

    public bool IsGap => Line.Text.Length == 0;

    /// <summary>The line being sung now; only ever true in timed lyrics.</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}

/// <summary>
/// The content of the lyrics panel: the lyrics of the song that is loaded, and which line is current. Lyrics are looked up
/// only while the panel is open (and once more when it opens), so a listener who never opens it pays nothing.
/// </summary>
public partial class LyricsViewModel : ViewModelBase
{
    private readonly PlayerViewModel _player;
    private readonly ILyricsSource? _source;
    private CancellationTokenSource? _loading;
    private TrackKey? _loadedFor;
    private IReadOnlyList<LyricLine> _timeline = [];

    // The song the lyrics were looked up for, so a late answer for the previous song is recognised and dropped.
    private readonly record struct TrackKey(Guid RecordingId, string MediaUri);

    public LyricsViewModel(PlayerViewModel player, ILyricsSource? source)
    {
        _player = player;
        _source = source;
        _player.TrackLoaded += (_, _) => _ = RefreshAsync();
        _player.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlayerViewModel.IsLyricsOpen)) _ = RefreshAsync();
            else if (e.PropertyName == nameof(PlayerViewModel.PositionSeconds)) UpdateCurrent();
        };
    }

    /// <summary>The rows; empty when there are no lyrics.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLyrics), nameof(HasNoLyrics))]
    public partial IReadOnlyList<LyricLineViewModel> Lines { get; set; } = [];

    /// <summary>Index of the current row, -1 before the first line of timed lyrics and for plain ones. The view scrolls to it.</summary>
    [ObservableProperty]
    public partial int CurrentIndex { get; set; } = -1;

    /// <summary>Timed lyrics follow the song; plain ones are only text.</summary>
    [ObservableProperty]
    public partial bool IsSynced { get; set; }

    /// <summary>Where the lyrics were found, as a sentence ("beside the song: song.lrc").</summary>
    [ObservableProperty]
    public partial string? SourceText { get; set; }

    public bool HasLyrics => Lines.Count > 0;

    public bool HasNoLyrics => Lines.Count == 0;

    public string NoLyricsText => _player.CurrentTrack is null ? "Nothing is playing" : "No lyrics for this song";

    public string NoLyricsHint => _player.CurrentTrack is null
        ? "Play a song to see its lyrics here."
        : "Put a .lrc or .ttml file with the same name beside the song, or add lyrics to the file's tags.";

    private async Task RefreshAsync()
    {
        _loading?.Cancel();

        if (!_player.IsLyricsOpen) return; // looked up when the panel opens

        var track = _player.CurrentTrack;
        var key = track is null ? (TrackKey?)null : new TrackKey(track.RecordingId, track.MediaUri);
        if (key is not null && key == _loadedFor && HasLyrics) return; // opened again for the same song

        _loading = new CancellationTokenSource();
        var token = _loading.Token;
        SongLyrics? lyrics = null;
        if (track is not null && _source is not null)
        {
            try
            {
                lyrics = await _source.FindAsync(track.MediaUri, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        // The song may have changed, or the panel closed, while the files were being read.
        if (token.IsCancellationRequested) return;

        _loadedFor = key;
        Show(lyrics);
    }

    private void Show(SongLyrics? lyrics)
    {
        IsSynced = lyrics?.IsSynced ?? false;
        SourceText = lyrics is null ? null : $"Lyrics {lyrics.Source}";
        CurrentIndex = -1;
        _timeline = lyrics?.Lines ?? [];
        Lines = lyrics is null ? [] : [.. lyrics.Lines.Select(l => new LyricLineViewModel(l))];
        OnPropertyChanged(nameof(NoLyricsText));
        OnPropertyChanged(nameof(NoLyricsHint));
        UpdateCurrent();
    }

    // A pure function of the position (LyricsTimeline), so seeking, looping and a restored session all land on the right line.
    private void UpdateCurrent()
    {
        if (!IsSynced || Lines.Count == 0) return;

        var index = LyricsTimeline.CurrentIndex(_timeline, TimeSpan.FromSeconds(_player.PositionSeconds));
        if (index == CurrentIndex) return;

        if (CurrentIndex >= 0) Lines[CurrentIndex].IsCurrent = false;
        if (index >= 0) Lines[index].IsCurrent = true;
        CurrentIndex = index;
    }
}
