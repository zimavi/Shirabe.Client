using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Shirabe.Client.Services;
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

    /// <summary>How much of the word has been sung, 0 to 1: the view fills the word in as this grows (karaoke).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStarted), nameof(IsComplete))]
    public partial double Progress { get; set; }

    /// <summary>The bright copy of the word is shown (whole when <see cref="IsComplete"/>, otherwise as far as <see cref="Progress"/>).</summary>
    public bool HasStarted => Progress > 0;

    /// <summary>The word has been sung to its end: only the bright copy is drawn.</summary>
    public bool IsComplete => Progress >= 1;

    /// <summary>Brings the word up to the song's position.</summary>
    public void Update(TimeSpan position)
    {
        IsSung = Word.Start <= position;
        var length = Word.End - Word.Start;
        Progress = length <= TimeSpan.Zero
            ? (IsSung ? 1 : 0)
            : Math.Clamp((position - Word.Start) / length, 0, 1);
    }

    public void Reset()
    {
        IsSung = false;
        Progress = 0;
    }
}

/// <summary>One row of the lyrics panel.</summary>
public sealed partial class LyricLineViewModel : ObservableObject
{
    /// <param name="until">When the line is over: its own end, or the start of the next line. Only a pause needs it.</param>
    public LyricLineViewModel(LyricLine line, TimeSpan? until = null)
    {
        Line = line;
        _until = line.End ?? until;
        Words = line.Words is { Count: > 0 } words ? [.. words.Select(w => new LyricWordViewModel(w))] : [];
        BackgroundWords = line.BackgroundWords is { Count: > 0 } sung
            ? [.. sung.Select(w => new LyricWordViewModel(w))]
            : [];
    }

    private readonly TimeSpan? _until;

    public LyricLine Line { get; }

    /// <summary>The line's text, without its background vocal when that is switched off.</summary>
    public string Text => ShowBackground || Line.Background is null ||
                          !Line.Text.EndsWith(Line.Background, StringComparison.Ordinal)
        ? Line.Text
        : Line.Text[..^Line.Background.Length].TrimEnd();

    /// <summary>False hides the row (pause markers that are switched off).</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text), nameof(BackgroundTextVisible), nameof(BackgroundWordsVisible))]
    public partial bool ShowBackground { get; set; } = true;

    /// <summary>A timed line with no text is an instrumental break, shown as three dots that fill in as it passes.</summary>
    public bool IsPause => Line.Text.Length == 0 && Line.Start is not null;

    /// <summary>The row shows text (and not the dots of a pause) when the file does not time its words.</summary>
    public bool ShowsText => Words.Count == 0 && !IsPause;

    /// <summary>How far through the break we are, 0 to 1; only moves while the pause is the current line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Dot1), nameof(Dot2), nameof(Dot3))]
    public partial double PauseProgress { get; set; }

    // Each dot fills in over its own third of the break: dim at 0, full at 1.
    public double Dot1 => DotOpacity(0);

    public double Dot2 => DotOpacity(1);

    public double Dot3 => DotOpacity(2);

    private double DotOpacity(int index) => 0.35 + 0.65 * Math.Clamp(PauseProgress * 3 - index, 0, 1);

    /// <summary>Sung by the second singer of a duet: shown on the right.</summary>
    public bool IsSecondVoice => Line.Voice == LyricVoice.Second;

    /// <summary>Sung by everyone together (harmony): shown in the middle.</summary>
    public bool IsGroupVoice => Line.Voice == LyricVoice.Group;

    /// <summary>The words, when the file times them; empty otherwise (the line is then shown as text).</summary>
    public IReadOnlyList<LyricWordViewModel> Words { get; }

    public bool HasWords => Words.Count > 0;

    public bool HasNoWords => Words.Count == 0;

    /// <summary>The background vocal, shown after the words: as text, or as words of its own when the file times them.</summary>
    public string? Background => Line.Background;

    public IReadOnlyList<LyricWordViewModel> BackgroundWords { get; }

    public bool HasBackgroundWords => BackgroundWords.Count > 0;

    public bool HasBackgroundText => Background is not null && BackgroundWords.Count == 0;

    public bool BackgroundTextVisible => ShowBackground && HasBackgroundText;

    public bool BackgroundWordsVisible => ShowBackground && HasBackgroundWords;

    /// <summary>A pause between lines (an empty line) is shown as a gap and cannot be clicked.</summary>
    public bool IsGap => Line.Text.Length == 0;

    /// <summary>The line being sung now; only ever true in timed lyrics.</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>Marks the words that have started at <paramref name="position"/>.</summary>
    public void UpdateWords(TimeSpan position)
    {
        foreach (var word in Words.Concat(BackgroundWords)) word.Update(position);

        if (IsPause && _until is { } until && Line.Start is { } start && until > start)
            PauseProgress = Math.Clamp((position - start) / (until - start), 0, 1);
    }

    public void ResetWords()
    {
        foreach (var word in Words.Concat(BackgroundWords)) word.Reset();
        PauseProgress = 0;
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
    private readonly HashSet<int> _active = []; // rows being sung now (see LyricsTimeline.Active)
    private readonly List<int> _sungNow = [];
    private readonly AppSettings _settings;

    // The song the lyrics were looked up for, so a late answer for the previous song is recognised and dropped.
    private readonly record struct TrackKey(Guid RecordingId, string MediaUri);

    /// <param name="settings">Null: the defaults (tests).</param>
    public LyricsViewModel(PlayerViewModel player, ILyricsSource? source, AppSettings? settings = null)
    {
        _player = player;
        _source = source;
        _settings = settings ?? new AppSettings();
        _settings.PropertyChanged += (_, e) => OnSettingChanged(e.PropertyName);
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

    /// <summary>The size of the lyrics text; the rows inherit it. Kept in a range that stays readable whatever the settings file says.</summary>
    public double FontSize => Math.Clamp(_settings.LyricsTextSize, 12, 32);

    /// <summary>Background vocals and the like are a little smaller than the lead.</summary>
    public double AsideFontSize => FontSize * 0.82;

    // Later than the player's clock by this much (the sound leaves the device after the position BASS reports).
    private TimeSpan Delay => TimeSpan.FromMilliseconds(Math.Clamp(_settings.LyricsDelayMs, -1000, 1000));

    private void OnSettingChanged(string? name)
    {
        switch (name)
        {
            case nameof(AppSettings.LyricsTextSize):
                OnPropertyChanged(nameof(FontSize));
                OnPropertyChanged(nameof(AsideFontSize));
                break;
            case nameof(AppSettings.ShowBackgroundVocals) or nameof(AppSettings.ShowPauseMarkers):
                ApplyDisplaySettings();
                break;
            case nameof(AppSettings.LyricsDelayMs):
                UpdateCurrent(_player.EnginePosition);
                break;
        }
    }

    private void ApplyDisplaySettings()
    {
        foreach (var line in Lines)
        {
            line.ShowBackground = _settings.ShowBackgroundVocals;
            line.IsShown = _settings.ShowPauseMarkers || !line.IsPause;
        }
    }

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
        _active.Clear();
        Lines = lyrics is null
            ? []
            :
            [
                .. lyrics.Lines.Select((l, i) =>
                    new LyricLineViewModel(l, i + 1 < lyrics.Lines.Count ? lyrics.Lines[i + 1].Start : null))
            ];
        ApplyDisplaySettings();
        OnPropertyChanged(nameof(NoLyricsText));
        OnPropertyChanged(nameof(NoLyricsHint));
        UpdateCurrent(_player.EnginePosition);
    }

    // A pure function of the position (LyricsTimeline), so seeking, looping and a restored session all land on the right line.
    private void UpdateCurrent(TimeSpan clock)
    {
        if (!IsSynced || Lines.Count == 0) return;

        var position = clock - Delay;
        LyricsTimeline.Active(_timeline, position, _sungNow);

        // A line that is no longer sung starts over; every line sung now (more than one when lines overlap) is brought up to date.
        _active.RemoveWhere(row =>
        {
            if (_sungNow.Contains(row)) return false;
            Lines[row].IsCurrent = false;
            Lines[row].ResetWords();
            return true;
        });

        foreach (var row in _sungNow)
        {
            if (_active.Add(row)) Lines[row].IsCurrent = true;
            Lines[row].UpdateWords(position);
        }

        // The view scrolls to the latest line that started.
        CurrentIndex = _sungNow.Count > 0 ? _sungNow[0] : -1;
    }

    /// <summary>Moves playback to the start of a line. Plain lyrics have no times and gaps are not lines, so nothing happens.</summary>
    [RelayCommand]
    private void SeekToLine(LyricLineViewModel? line)
    {
        if (line is { IsGap: false, Line.Start: { } start } && IsSynced) _player.PositionSeconds = start.TotalSeconds;
    }
}