using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Shirabe.Core.Lyrics;
using Xunit;

namespace Shirabe.Tests;

public class LyricsViewModelTests
{
    private readonly FakeAudioPlayer _audio = new();
    private readonly PlayerViewModel _player;
    private readonly FakeLyrics _source = new();
    private readonly LyricsViewModel _lyrics;

    public LyricsViewModelTests()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        _player = new PlayerViewModel(_audio);
        _lyrics = new LyricsViewModel(_player, _source);
    }

    private sealed class FakeLyrics : ILyricsSource
    {
        public List<string> Asked { get; } = [];

        public Dictionary<string, TaskCompletionSource<SongLyrics?>> Answers { get; } = [];

        public Task<SongLyrics?> FindAsync(string mediaUri, CancellationToken cancellationToken = default)
        {
            Asked.Add(mediaUri);
            if (!Answers.TryGetValue(mediaUri, out var answer)) Answers[mediaUri] = answer = new();
            return answer.Task;
        }

        public void Answer(TrackSummary track, SongLyrics? lyrics)
        {
            if (!Answers.TryGetValue(track.MediaUri, out var answer)) Answers[track.MediaUri] = answer = new();
            answer.SetResult(lyrics);
        }
    }

    private static TrackSummary Song(string title) =>
        new(Guid.NewGuid(), title, "Artist", null, null, new Uri(Path.Combine(Path.GetTempPath(), title + ".mp3")).AbsoluteUri);

    private static SongLyrics Timed(params (double Seconds, string Text)[] lines) =>
        SongLyrics.Of([.. lines.Select(l => new LyricLine(TimeSpan.FromSeconds(l.Seconds), null, l.Text))], "beside the song: a.lrc")!;

    private static SongLyrics Plain(params string[] lines) =>
        SongLyrics.Of([.. lines.Select(l => new LyricLine(null, null, l))], "in the file's tags")!;

    private static async Task Settle() => await Task.Yield();

    // While a song plays the view calls Tick on every frame, which reads the engine's clock.
    private void At(double seconds)
    {
        _audio.Position = TimeSpan.FromSeconds(seconds);
        _lyrics.Tick();
    }

    [Fact]
    public async Task Nothing_is_looked_up_while_the_panel_is_closed_and_opening_it_looks_up_the_song_that_is_loaded()
    {
        var a = Song("A");
        await _player.PlayAsync(a);
        Assert.Empty(_source.Asked);

        _player.IsLyricsOpen = true;
        _source.Answer(a, Timed((1, "one")));
        await Settle();

        Assert.Equal([a.MediaUri], _source.Asked);
        Assert.Equal(["one"], _lyrics.Lines.Select(l => l.Text));
        Assert.Equal("Lyrics beside the song: a.lrc", _lyrics.SourceText);
        Assert.True(_lyrics.HasLyrics);
    }

    [Fact]
    public async Task While_open_a_new_song_is_looked_up_and_a_late_answer_for_the_old_one_is_dropped()
    {
        var a = Song("A");
        var b = Song("B");
        _player.IsLyricsOpen = true;
        await _player.PlayAsync(a); // asks for A, no answer yet
        await _player.PlayAsync(b); // asks for B

        _source.Answer(b, Timed((1, "B line")));
        await Settle();
        _source.Answer(a, Timed((1, "A line"))); // arrives late
        await Settle();

        Assert.Equal(["B line"], _lyrics.Lines.Select(l => l.Text));
    }

    [Fact]
    public async Task The_current_line_follows_the_position_including_after_a_seek_back()
    {
        var a = Song("A");
        await _player.PlayAsync(a);
        _player.IsLyricsOpen = true;
        _source.Answer(a, Timed((5, "one"), (10, "two"), (20, "three")));
        await Settle();
        Assert.Equal(-1, _lyrics.CurrentIndex); // before the first line

        At(12);
        Assert.Equal(1, _lyrics.CurrentIndex);
        Assert.Equal([false, true, false], _lyrics.Lines.Select(l => l.IsCurrent));

        At(25);
        Assert.Equal([false, false, true], _lyrics.Lines.Select(l => l.IsCurrent));

        At(6); // seek back
        Assert.Equal(0, _lyrics.CurrentIndex);
        Assert.Equal([true, false, false], _lyrics.Lines.Select(l => l.IsCurrent));

        At(0);
        Assert.Equal(-1, _lyrics.CurrentIndex);
        Assert.All(_lyrics.Lines, l => Assert.False(l.IsCurrent));
    }

    private static SongLyrics Worded() => SongLyrics.Of(
    [
        new LyricLine(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6), "one two three",
        [
            new LyricWord(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "one", true),
            new LyricWord(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "two", true),
            new LyricWord(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), "three"),
        ]),
        new LyricLine(TimeSpan.FromSeconds(10), null, "next"),
    ], "beside the song: a.ttml")!;

    [Fact]
    public async Task Words_light_up_as_they_start_in_the_current_line_and_reset_when_it_is_left()
    {
        var a = Song("A");
        await _player.PlayAsync(a);
        _player.IsLyricsOpen = true;
        _source.Answer(a, Worded());
        await Settle();
        var words = _lyrics.Lines[0].Words;
        Assert.True(_lyrics.Lines[0].HasWords);
        Assert.True(_lyrics.Lines[1].HasNoWords);

        At(1);
        Assert.All(words, w => Assert.False(w.IsSung));

        At(3.5);
        Assert.Equal([true, true, false], words.Select(w => w.IsSung));

        At(5);
        Assert.Equal([true, true, true], words.Select(w => w.IsSung));

        At(3.2); // seek back inside the line
        Assert.Equal([true, true, false], words.Select(w => w.IsSung));

        At(11); // the next line is current: this one starts over
        Assert.All(words, w => Assert.False(w.IsSung));
    }

    [Fact]
    public async Task A_background_vocal_lights_up_by_its_own_word_times_inside_the_current_line()
    {
        var a = Song("A");
        await _player.PlayAsync(a);
        _player.IsLyricsOpen = true;
        _source.Answer(a, SongLyrics.Of(
        [
            new LyricLine(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), "lead (ooh aah)",
                [new LyricWord(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "lead")], "(ooh aah)",
                [new LyricWord(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), "(ooh", true), new LyricWord(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(7), "aah)")]),
        ], "x"));
        await Settle();
        var line = _lyrics.Lines[0];
        Assert.True(line.HasBackgroundWords);
        Assert.False(line.HasBackgroundText);

        At(5.5);
        Assert.Equal([true, false], line.BackgroundWords.Select(w => w.IsSung));

        At(6.5);
        Assert.Equal([true, true], line.BackgroundWords.Select(w => w.IsSung));

        At(0.5); // before the line: starts over
        Assert.All(line.BackgroundWords, w => Assert.False(w.IsSung));
    }

    [Fact]
    public async Task A_background_vocal_without_word_times_is_shown_as_text()
    {
        var a = Song("A");
        await _player.PlayAsync(a);
        _player.IsLyricsOpen = true;
        _source.Answer(a, SongLyrics.Of([new LyricLine(TimeSpan.FromSeconds(1), null, "lead (ooh)", null, "(ooh)")], "x"));
        await Settle();

        Assert.True(_lyrics.Lines[0].HasBackgroundText);
        Assert.False(_lyrics.Lines[0].HasBackgroundWords);
    }

    [Fact]
    public async Task A_timed_line_with_no_text_is_a_pause_marker_but_a_stanza_break_in_plain_lyrics_is_not()
    {
        var a = Song("A");
        await _player.PlayAsync(a);
        _player.IsLyricsOpen = true;
        _source.Answer(a, SongLyrics.Of(
            [new LyricLine(TimeSpan.Zero, TimeSpan.FromSeconds(9), ""), new LyricLine(TimeSpan.FromSeconds(9), null, "first line", null, null, null, true)], "x"));
        await Settle();

        Assert.True(_lyrics.Lines[0].IsPause);
        Assert.False(_lyrics.Lines[0].ShowsText);
        Assert.True(_lyrics.Lines[1].ShowsText);
        Assert.False(_lyrics.Lines[0].IsSecondVoice);
        Assert.True(_lyrics.Lines[1].IsSecondVoice);
        At(1);
        Assert.Equal(0, _lyrics.CurrentIndex); // the pause is the current line during the break

        var stanzaBreak = new LyricLineViewModel(new LyricLine(null, null, ""));
        Assert.False(stanzaBreak.IsPause);
    }

    [Fact]
    public async Task The_dots_of_a_pause_fill_in_over_its_length_and_start_over_when_it_is_left()
    {
        var a = Song("A");
        await _player.PlayAsync(a);
        _player.IsLyricsOpen = true;
        // The break has no end of its own (like an empty line in LRC): it lasts until the next line.
        _source.Answer(a, SongLyrics.Of(
            [new LyricLine(TimeSpan.FromSeconds(10), null, ""), new LyricLine(TimeSpan.FromSeconds(20), null, "next")], "x"));
        await Settle();
        var pause = _lyrics.Lines[0];

        At(10);
        Assert.Equal(0, pause.PauseProgress);
        Assert.Equal([0.35, 0.35, 0.35], [pause.Dot1, pause.Dot2, pause.Dot3], new DotComparer());

        At(15); // halfway: the first dot is full, the second half way, the third not started
        Assert.Equal(0.5, pause.PauseProgress, precision: 3);
        Assert.Equal([1.0, 0.675, 0.35], [pause.Dot1, pause.Dot2, pause.Dot3], new DotComparer());

        At(19.99);
        Assert.Equal([1.0, 1.0, 1.0], [pause.Dot1, pause.Dot2, pause.Dot3], new DotComparer(0.01));

        At(21); // the next line is current: the dots start over
        Assert.Equal(0, pause.PauseProgress);

        At(12); // a seek back into the break
        Assert.Equal(0.2, pause.PauseProgress, precision: 3);
    }

    private sealed class DotComparer(double tolerance = 0.001) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) <= tolerance;

        public int GetHashCode(double value) => 0;
    }

    [Fact]
    public async Task Clicking_a_timed_line_moves_playback_to_its_start_but_plain_lines_and_gaps_do_nothing()
    {
        var a = Song("A");
        await _player.PlayAsync(a);
        _player.IsLyricsOpen = true;
        _source.Answer(a, SongLyrics.Of(
            [new LyricLine(TimeSpan.FromSeconds(5), null, "one"), new LyricLine(TimeSpan.FromSeconds(10), null, ""), new LyricLine(TimeSpan.FromSeconds(20), null, "three")], "x"));
        await Settle();

        _lyrics.SeekToLineCommand.Execute(_lyrics.Lines[2]);
        Assert.Equal(20, _player.PositionSeconds);
        Assert.Equal(TimeSpan.FromSeconds(20), _audio.Seeks[^1]); // the engine really moved

        _lyrics.SeekToLineCommand.Execute(_lyrics.Lines[1]); // a pause between lines
        Assert.Equal(20, _player.PositionSeconds);

        _lyrics.SeekToLineCommand.Execute(null);
        Assert.Equal(20, _player.PositionSeconds);
    }

    [Fact]
    public async Task Clicking_a_plain_line_does_nothing()
    {
        var a = Song("A");
        await _player.PlayAsync(a);
        _player.IsLyricsOpen = true;
        _source.Answer(a, Plain("words", "more"));
        await Settle();
        _player.PositionSeconds = 7;

        _lyrics.SeekToLineCommand.Execute(_lyrics.Lines[1]);

        Assert.Equal(7, _player.PositionSeconds);
    }

    [Fact]
    public async Task Frames_are_needed_only_for_timed_lyrics_of_a_song_that_is_playing_with_the_panel_open()
    {
        var a = Song("A");
        await _player.PlayAsync(a);
        Assert.False(_lyrics.NeedsFrames); // panel closed

        _player.IsLyricsOpen = true;
        _source.Answer(a, Timed((1, "one")));
        await Settle();
        Assert.True(_lyrics.NeedsFrames);

        _audio.Pause();
        Assert.False(_lyrics.NeedsFrames);

        _audio.Play();
        _player.IsLyricsOpen = false;
        Assert.False(_lyrics.NeedsFrames);
    }

    [Fact]
    public async Task Paused_a_seek_still_moves_the_current_line_through_the_position_the_slider_reports()
    {
        var a = Song("A");
        await _player.PlayAsync(a);
        _audio.Pause();
        _player.IsLyricsOpen = true;
        _source.Answer(a, Timed((5, "one"), (10, "two")));
        await Settle();

        _player.PositionSeconds = 12;

        Assert.Equal(1, _lyrics.CurrentIndex);
    }

    [Fact]
    public async Task While_playing_the_engine_clock_decides_and_a_stale_copy_of_the_position_cannot_move_the_line_back()
    {
        var a = Song("A");
        await _player.PlayAsync(a);
        _player.IsLyricsOpen = true;
        _source.Answer(a, Timed((5, "one"), (10, "two")));
        await Settle();
        At(10.02);
        Assert.Equal(1, _lyrics.CurrentIndex);

        _audio.Position = TimeSpan.FromSeconds(9.95); // the 100 ms copy lags behind the clock the frames just read
        _player.RefreshPosition();

        Assert.Equal(1, _lyrics.CurrentIndex);
    }

    [Fact]
    public async Task Plain_lyrics_have_no_current_line()
    {
        var a = Song("A");
        await _player.PlayAsync(a);
        _player.IsLyricsOpen = true;
        _source.Answer(a, Plain("words", "more words"));
        await Settle();

        _player.PositionSeconds = 30;

        Assert.False(_lyrics.IsSynced);
        Assert.Equal(-1, _lyrics.CurrentIndex);
        Assert.Equal(2, _lyrics.Lines.Count);
    }

    [Fact]
    public async Task A_song_without_lyrics_and_no_song_at_all_say_so()
    {
        _player.IsLyricsOpen = true;
        await Settle();
        Assert.True(_lyrics.HasNoLyrics);
        Assert.Equal("Nothing is playing", _lyrics.NoLyricsText);

        var a = Song("A");
        await _player.PlayAsync(a);
        _source.Answer(a, null);
        await Settle();

        Assert.True(_lyrics.HasNoLyrics);
        Assert.Equal("No lyrics for this song", _lyrics.NoLyricsText);
        Assert.Contains(".lrc", _lyrics.NoLyricsHint);
        Assert.Null(_lyrics.SourceText);
    }

    [Fact]
    public async Task Without_a_source_the_panel_just_has_no_lyrics()
    {
        var lyrics = new LyricsViewModel(_player, null);
        _player.IsLyricsOpen = true;
        await _player.PlayAsync(Song("A"));
        await Settle();

        Assert.True(lyrics.HasNoLyrics);
    }

    [Fact]
    public async Task Closing_the_panel_drops_a_look_up_still_running()
    {
        var a = Song("A");
        await _player.PlayAsync(a);
        _player.IsLyricsOpen = true;
        _player.IsLyricsOpen = false;

        _source.Answer(a, Timed((1, "too late")));
        await Settle();

        Assert.True(_lyrics.HasNoLyrics);
    }

    [Fact]
    public void Opening_the_lyrics_closes_the_queue_and_the_other_way_round()
    {
        _player.IsQueueOpen = true;

        _player.ToggleLyricsCommand.Execute(null);
        Assert.True(_player.IsLyricsOpen);
        Assert.False(_player.IsQueueOpen);

        _player.ToggleQueueCommand.Execute(null);
        Assert.True(_player.IsQueueOpen);
        Assert.False(_player.IsLyricsOpen);
    }

    [Fact]
    public void The_shell_has_a_lyrics_panel_when_it_has_a_player()
    {
        Assert.NotNull(new MainViewModel(_player, null, null).Lyrics);
        Assert.Null(new MainViewModel(null, null, "no audio engine").Lyrics);
    }
}
