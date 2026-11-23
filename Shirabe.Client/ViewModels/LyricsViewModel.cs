using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shirabe.Core.Lyrics;

namespace Shirabe.Client.ViewModels;

/// <summary>One word of a line that is sung word by word.</summary>
public sealed partial class LyricWordViewModel(LyricWord word) : ObservableObject
{
    public LyricWord Word { get; } = word;

    public string Text => Word.Text;

    public bool SpaceAfter => Word.SpaceAfter;

    /// <summary>The word has started (only kept up to date while its line is the current one).</summary>
    [ObservableProperty]
    public partial bool IsSung { get; set; }
}

/// <summary>One row of the lyrics panel.</summary>
public sealed partial class LyricLineViewModel : ObservableObject
{
    public LyricLineViewModel(LyricLine line)
    {
        Line = line;
        Words = line.Words is { Count: > 0 } words ? [.. words.Select(w => new LyricWordViewModel(w))] : [];
    }

    public LyricLine Line { get; }

    public string Text => Line.Text;

    /// <summary>The words, when the file times them; empty otherwise (the line is then shown as text).</summary>
    public IReadOnlyList<LyricWordViewModel> Words { get; }

    public bool HasWords => Words.Count > 0;

    public bool HasNoWords => Words.Count == 0;

    /// <summary>The background vocal, shown after the words.</summary>
    public string? Background => Line.Background;

    /// <summary>A pause between lines (an empty line) is shown as a gap and cannot be clicked.</summary>
    public bool IsGap => Line.Text.Length == 0;

    /// <summary>The line being sung now; only ever true in timed lyrics.</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>Marks the words that have started at <paramref name="position"/>.</summary>
    public void UpdateWords(TimeSpan position)
    {
        foreach (var word in Words) word.IsSung = word.Word.Start <= position;
    }

    public void ResetWords()
    {
        foreach (var word in Words) word.IsSung = false;
    }
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
            else if (e.PropertyName == nameof(PlayerViewModel.PositionSeconds))
            {
                // While frames drive the lines (playing), the 100 ms copy of the position would only fight them.
                if (!NeedsFrames) UpdateCurrent(TimeSpan.FromSeconds(_player.PositionSeconds));
            }

            if (e.PropertyName is nameof(PlayerViewModel.IsLyricsOpen) or nameof(PlayerViewModel.IsPlaying))
                OnPropertyChanged(nameof(NeedsFrames));
        };
    }

    partial void OnIsSyncedChanged(bool value) => OnPropertyChanged(nameof(NeedsFrames));

    /// <summary>
    /// The panel is open, the lyrics are timed and the song is playing: the view then calls <see cref="Tick"/> on every
    /// frame, so lines and words change at the right moment instead of at the next 100 ms step (a word can last less).
    /// Paused, nothing moves except by a seek, which <see cref="PlayerViewModel.PositionSeconds"/> reports.
    /// </summary>
    public bool NeedsFrames => IsSynced && _player.IsLyricsOpen && _player.IsPlaying;

    /// <summary>Brings the current line and words up to the engine's clock. Called by the view on every frame.</summary>
    public void Tick() => UpdateCurrent(_player.EnginePosition);

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
        UpdateCurrent(_player.EnginePosition);
    }

    // A pure function of the position (LyricsTimeline), so seeking, looping and a restored session all land on the right line.
    private void UpdateCurrent(TimeSpan position)
    {
        if (!IsSynced || Lines.Count == 0) return;

        var index = LyricsTimeline.CurrentIndex(_timeline, position);
        if (index != CurrentIndex)
        {
            if (CurrentIndex >= 0)
            {
                Lines[CurrentIndex].IsCurrent = false;
                Lines[CurrentIndex].ResetWords();
            }

            if (index >= 0) Lines[index].IsCurrent = true;
            CurrentIndex = index;
        }

        if (index >= 0) Lines[index].UpdateWords(position);
    }

    /// <summary>Moves playback to the start of a line. Plain lyrics have no times and gaps are not lines, so nothing happens.</summary>
    [RelayCommand]
    private void SeekToLine(LyricLineViewModel? line)
    {
        if (line is { IsGap: false, Line.Start: { } start } && IsSynced) _player.PositionSeconds = start.TotalSeconds;
    }
}
