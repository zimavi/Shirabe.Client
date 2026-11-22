using Shirabe.Core.Library;
using Shirabe.Core.Playback;
using Xunit;

namespace Shirabe.Tests;

public class PlaybackQueueTests
{
    private readonly PlaybackQueue _queue = new(new Random(42));

    [Fact]
    public void A_new_queue_starts_at_the_chosen_song()
    {
        _queue.SetQueue(Tracks("A", "B", "C"), startIndex: 1);

        Assert.Equal("B", _queue.Current!.Track.Title);
        Assert.Equal(1, _queue.Position);
        Assert.Equal(["A", "B", "C"], Titles(_queue.Items));
    }

    [Fact]
    public void An_empty_queue_has_nothing_to_play()
    {
        Assert.Null(_queue.Current);
        Assert.Equal(-1, _queue.Position);
        Assert.Null(_queue.MoveNext(autoAdvance: true));
        Assert.Null(_queue.MovePrevious());
    }

    [Fact]
    public void Next_walks_through_the_queue_and_stops_at_the_end_when_not_repeating()
    {
        _queue.SetQueue(Tracks("A", "B"), 0);

        Assert.Equal("B", _queue.MoveNext(autoAdvance: true)!.Track.Title);
        Assert.Null(_queue.MoveNext(autoAdvance: true));
        Assert.Equal("B", _queue.Current!.Track.Title); // stays on the last song
    }

    [Fact]
    public void Repeat_all_wraps_around_in_both_directions()
    {
        _queue.Repeat = RepeatMode.All;
        _queue.SetQueue(Tracks("A", "B", "C"), 2);

        Assert.Equal("A", _queue.MoveNext(autoAdvance: false)!.Track.Title);
        Assert.Equal("C", _queue.MovePrevious()!.Track.Title);
    }

    [Fact]
    public void Repeat_one_replays_the_song_when_it_ends_but_a_manual_next_moves_on()
    {
        _queue.Repeat = RepeatMode.One;
        _queue.SetQueue(Tracks("A", "B"), 0);

        Assert.Equal("A", _queue.MoveNext(autoAdvance: true)!.Track.Title);
        Assert.Equal("A", _queue.MoveNext(autoAdvance: true)!.Track.Title);
        Assert.Equal("B", _queue.MoveNext(autoAdvance: false)!.Track.Title);
    }

    [Fact]
    public void Previous_at_the_start_stays_on_the_first_song_without_repeat()
    {
        _queue.SetQueue(Tracks("A", "B"), 0);

        Assert.Equal("A", _queue.MovePrevious()!.Track.Title);
        Assert.Equal(0, _queue.Position);
    }

    [Fact]
    public void Exactly_one_entry_is_flagged_as_current_through_every_kind_of_move()
    {
        _queue.Repeat = RepeatMode.All;
        _queue.SetQueue(Tracks("A", "B", "C", "D"), 1);

        void AssertOneCurrent()
        {
            var flagged = _queue.Items.Where(i => i.IsCurrent).ToList();
            Assert.Same(_queue.Current, Assert.Single(flagged));
        }

        AssertOneCurrent();
        _queue.MoveNext(false); AssertOneCurrent();
        _queue.MovePrevious(); AssertOneCurrent();
        _queue.Shuffle = true; AssertOneCurrent();
        for (var i = 0; i < 6; i++) { _queue.MoveNext(true); AssertOneCurrent(); } // crosses a new round
        _queue.Shuffle = false; AssertOneCurrent();
        _queue.JumpTo(_queue.Items[2]); AssertOneCurrent();
        _queue.SetQueue(Tracks("X", "Y"), 1); AssertOneCurrent();
    }

    [Fact]
    public void Shuffle_plays_the_chosen_song_first_and_every_song_exactly_once_per_round()
    {
        _queue.Shuffle = true;
        var tracks = Tracks(Enumerable.Range(0, 30).Select(i => $"T{i:D2}").ToArray());
        _queue.SetQueue(tracks, startIndex: 7);

        var played = new List<string> { _queue.Current!.Track.Title };
        while (_queue.MoveNext(autoAdvance: true) is { } next) played.Add(next.Track.Title);

        Assert.Equal("T07", played[0]);
        Assert.Equal(30, played.Count);
        Assert.Equal(30, played.Distinct().Count());
        Assert.NotEqual(played.Order(), played); // actually shuffled
    }

    [Fact]
    public void Turning_shuffle_on_keeps_the_current_song_and_turning_it_off_restores_the_order()
    {
        _queue.SetQueue(Tracks("A", "B", "C", "D", "E"), 2);

        _queue.Shuffle = true;
        Assert.Equal("C", _queue.Current!.Track.Title);
        Assert.Equal(0, _queue.Position);
        Assert.Equal(5, _queue.Items.Count);

        _queue.Shuffle = false;
        Assert.Equal(["A", "B", "C", "D", "E"], Titles(_queue.Items));
        Assert.Equal("C", _queue.Current!.Track.Title);
        Assert.Equal(2, _queue.Position);
    }

    [Fact]
    public void Repeat_all_with_shuffle_starts_a_new_round_that_does_not_open_with_the_song_just_played()
    {
        _queue.Shuffle = true;
        _queue.Repeat = RepeatMode.All;
        _queue.SetQueue(Tracks("A", "B", "C"), 0);
        for (var i = 0; i < 2; i++) _queue.MoveNext(true); // reach the last song of the round
        var last = _queue.Current!.Track.Title;

        var first = _queue.MoveNext(true)!;

        Assert.NotEqual(last, first.Track.Title);
        Assert.Equal(0, _queue.Position);
        Assert.Equal(3, _queue.Items.Count);
    }

    [Fact]
    public void Play_next_goes_right_after_the_current_song_in_either_order()
    {
        _queue.SetQueue(Tracks("A", "B", "C"), 0);
        _queue.PlayNext(Track("X"));
        Assert.Equal(["A", "X", "B", "C"], Titles(_queue.Items));

        _queue.Shuffle = true;
        _queue.PlayNext(Track("Y"));
        Assert.Equal("Y", _queue.Items[_queue.Position + 1].Track.Title);

        _queue.Shuffle = false; // and in the natural order too
        Assert.Equal(["A", "Y", "X", "B", "C"], Titles(_queue.Items));
    }

    [Fact]
    public void Enqueue_adds_at_the_end_and_the_same_song_can_be_queued_twice()
    {
        _queue.SetQueue(Tracks("A"), 0);
        var again = Track("B");

        _queue.Enqueue(again);
        _queue.Enqueue(again);

        Assert.Equal(["A", "B", "B"], Titles(_queue.Items));
        Assert.NotSame(_queue.Items[1], _queue.Items[2]); // separate entries, so one can be removed alone
    }

    [Fact]
    public void Removing_an_earlier_entry_keeps_the_current_song_current()
    {
        _queue.SetQueue(Tracks("A", "B", "C", "D"), 2);
        var current = _queue.Current;

        Assert.True(_queue.Remove(_queue.Items[0]));

        Assert.Same(current, _queue.Current);
        Assert.Equal(1, _queue.Position);
        Assert.Equal(["B", "C", "D"], Titles(_queue.Items));
    }

    [Fact]
    public void The_song_that_is_playing_cannot_be_removed()
    {
        _queue.SetQueue(Tracks("A", "B"), 0);

        Assert.False(_queue.Remove(_queue.Current!));
        Assert.Equal(2, _queue.Items.Count);
    }

    [Fact]
    public void Clearing_keeps_only_the_current_song()
    {
        _queue.SetQueue(Tracks("A", "B", "C"), 1);

        _queue.ClearUpcoming();

        Assert.Equal(["B"], Titles(_queue.Items));
        Assert.Equal("B", _queue.Current!.Track.Title);
        Assert.Null(_queue.MoveNext(autoAdvance: true));
    }

    [Fact]
    public void Jumping_to_an_entry_makes_it_current()
    {
        _queue.SetQueue(Tracks("A", "B", "C"), 0);

        Assert.True(_queue.JumpTo(_queue.Items[2]));

        Assert.Equal("C", _queue.Current!.Track.Title);
        Assert.False(_queue.JumpTo(new QueueItem(Track("elsewhere"))));
    }

    [Fact]
    public void Changed_is_raised_when_the_list_changes_but_not_when_only_the_position_moves()
    {
        var changes = 0;
        _queue.Changed += (_, _) => changes++;

        _queue.SetQueue(Tracks("A", "B", "C"), 0);
        Assert.Equal(1, changes);

        _queue.MoveNext(true);
        _queue.MovePrevious();
        _queue.JumpTo(_queue.Items[2]);
        Assert.Equal(1, changes);

        _queue.Shuffle = true;
        _queue.Enqueue(Track("D"));
        _queue.Remove(_queue.Items[^1]);
        Assert.Equal(4, changes);
    }

    private static TrackSummary Track(string title) =>
        new(Guid.NewGuid(), title, "Artist", "Album", TimeSpan.FromMinutes(3), $"file:///music/{title}.flac");

    private static List<TrackSummary> Tracks(params string[] titles) => titles.Select(Track).ToList();

    private static string[] Titles(IEnumerable<QueueItem> items) => items.Select(i => i.Track.Title).ToArray();
}
