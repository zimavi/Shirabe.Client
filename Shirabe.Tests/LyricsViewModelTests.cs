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

        _player.PositionSeconds = 12;
        Assert.Equal(1, _lyrics.CurrentIndex);
        Assert.Equal([false, true, false], _lyrics.Lines.Select(l => l.IsCurrent));

        _player.PositionSeconds = 25;
        Assert.Equal([false, false, true], _lyrics.Lines.Select(l => l.IsCurrent));

        _player.PositionSeconds = 6; // seek back
        Assert.Equal(0, _lyrics.CurrentIndex);
        Assert.Equal([true, false, false], _lyrics.Lines.Select(l => l.IsCurrent));

        _player.PositionSeconds = 0;
        Assert.Equal(-1, _lyrics.CurrentIndex);
        Assert.All(_lyrics.Lines, l => Assert.False(l.IsCurrent));
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
