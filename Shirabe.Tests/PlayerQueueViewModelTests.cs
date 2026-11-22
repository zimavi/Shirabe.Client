using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Shirabe.Core.Media;
using Shirabe.Core.Playback;
using Xunit;

namespace Shirabe.Tests;

/// <summary>Playing through a queue: advancing, repeating, shuffling, skipping, and editing it.</summary>
public class PlayerQueueViewModelTests
{
    private readonly FakeAudioPlayer _audio = new();
    private readonly PlayerViewModel _player;

    public PlayerQueueViewModelTests()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        _player = new PlayerViewModel(_audio);
    }

    [Fact]
    public async Task Playing_a_song_from_a_list_queues_the_whole_list_and_starts_at_that_song()
    {
        await _player.PlayTracksAsync(Tracks("A", "B", "C"), startIndex: 1);

        Assert.Equal("B", _player.Title);
        Assert.Equal(["A", "B", "C"], QueueTitles());
        Assert.Equal(PlaybackState.Playing, _audio.State);
    }

    [Fact]
    public async Task When_a_song_ends_the_next_one_plays_and_the_queue_stops_after_the_last()
    {
        await _player.PlayTracksAsync(Tracks("A", "B"), 0);

        _audio.RaiseEnded();
        Assert.Equal("B", _player.Title);
        Assert.Equal(PlaybackState.Playing, _audio.State);

        _audio.Position = TimeSpan.FromSeconds(100);
        _audio.RaiseEnded();
        Assert.Equal(PlaybackState.Stopped, _audio.State);
        Assert.Equal(0, _player.PositionSeconds); // rewound, ready to play again
    }

    [Fact]
    public async Task Repeat_one_plays_the_same_song_again_when_it_ends()
    {
        _player.CycleRepeatCommand.Execute(null); // all
        _player.CycleRepeatCommand.Execute(null); // one
        await _player.PlayTracksAsync(Tracks("A", "B"), 0);

        _audio.RaiseEnded();

        Assert.Equal(RepeatMode.One, _player.Repeat);
        Assert.Equal("A", _player.Title);
        Assert.Equal(2, _audio.Loads.Count);
    }

    [Fact]
    public async Task Repeat_all_wraps_to_the_first_song_after_the_last()
    {
        _player.CycleRepeatCommand.Execute(null);
        await _player.PlayTracksAsync(Tracks("A", "B"), 1);

        _audio.RaiseEnded();

        Assert.Equal(RepeatMode.All, _player.Repeat);
        Assert.Equal("A", _player.Title);
    }

    [Fact]
    public void Repeat_cycles_through_off_all_one_and_back()
    {
        var seen = new List<RepeatMode>();
        for (var i = 0; i < 4; i++)
        {
            _player.CycleRepeatCommand.Execute(null);
            seen.Add(_player.Repeat);
        }

        Assert.Equal([RepeatMode.All, RepeatMode.One, RepeatMode.Off, RepeatMode.All], seen);
    }

    [Fact]
    public async Task A_song_that_cannot_be_loaded_is_skipped_when_playing_on_automatically()
    {
        _audio.FailWhen = source => source is LocalFileSource { Path: var path } && path.EndsWith("B.flac")
            ? new FileNotFoundException() : null;
        await _player.PlayTracksAsync(Tracks("A", "B", "C"), 0);

        _audio.RaiseEnded(); // B is broken: C plays instead

        Assert.Equal("C", _player.Title);
        Assert.Equal(PlaybackState.Playing, _audio.State);
    }

    [Fact]
    public async Task A_queue_where_nothing_can_be_loaded_stops_instead_of_looping_forever()
    {
        _player.CycleRepeatCommand.Execute(null); // repeat all: the worst case for an endless loop
        await _player.PlayTracksAsync(Tracks("A", "B"), 0);
        _audio.FailWhen = _ => new FileNotFoundException();

        _audio.RaiseEnded(); // returns at all = no infinite loop

        Assert.Equal(PlaybackState.Stopped, _audio.State);
        Assert.Contains("moved or deleted", _player.ErrorMessage);
    }

    [Fact]
    public async Task A_song_the_user_picked_that_cannot_be_loaded_is_reported_not_skipped()
    {
        _audio.FailWhen = _ => new FileNotFoundException();

        await _player.PlayTracksAsync(Tracks("A", "B"), 0);

        Assert.Contains("A", _player.ErrorMessage);
        Assert.Empty(_audio.Loads); // B was not tried behind the user's back
    }

    [Fact]
    public async Task Next_plays_the_following_song_and_does_nothing_at_the_end_of_the_queue()
    {
        await _player.PlayTracksAsync(Tracks("A", "B"), 0);

        await _player.NextCommand.ExecuteAsync(null);
        Assert.Equal("B", _player.Title);

        await _player.NextCommand.ExecuteAsync(null);
        Assert.Equal("B", _player.Title);
        Assert.Equal(2, _audio.Loads.Count);
    }

    [Fact]
    public async Task Previous_goes_back_within_the_first_seconds_and_restarts_the_song_after_that()
    {
        await _player.PlayTracksAsync(Tracks("A", "B"), 1);

        _audio.Position = TimeSpan.FromSeconds(10);
        await _player.PreviousCommand.ExecuteAsync(null);
        Assert.Equal("B", _player.Title); // restarted, not back to A
        Assert.Equal(TimeSpan.Zero, Assert.Single(_audio.Seeks));

        _audio.Position = TimeSpan.FromSeconds(1);
        await _player.PreviousCommand.ExecuteAsync(null);
        Assert.Equal("A", _player.Title);
    }

    [Fact]
    public async Task Previous_on_the_first_song_restarts_it()
    {
        await _player.PlayTracksAsync(Tracks("A", "B"), 0);
        _audio.Position = TimeSpan.FromSeconds(1);

        await _player.PreviousCommand.ExecuteAsync(null);

        Assert.Equal("A", _player.Title);
        Assert.Single(_audio.Loads); // not loaded again, just rewound
        Assert.Equal(TimeSpan.Zero, Assert.Single(_audio.Seeks));
    }

    [Fact]
    public async Task Shuffle_keeps_the_current_song_playing_and_can_be_turned_off_again()
    {
        await _player.PlayTracksAsync(Tracks("A", "B", "C", "D"), 2);

        _player.ToggleShuffleCommand.Execute(null);
        Assert.True(_player.IsShuffleOn);
        Assert.Equal("C", _player.QueueItems[0].Track.Title); // the current song leads the shuffled order
        Assert.Equal(4, _player.QueueItems.Count);

        _player.ToggleShuffleCommand.Execute(null);
        Assert.False(_player.IsShuffleOn);
        Assert.Equal(["A", "B", "C", "D"], QueueTitles());
        Assert.Equal("C", _player.Title);
    }

    [Fact]
    public async Task Adding_to_the_queue_starts_playback_when_nothing_is_playing_and_appends_otherwise()
    {
        await _player.EnqueueAsync(Track("A"));
        Assert.Equal("A", _player.Title);
        Assert.Equal(PlaybackState.Playing, _audio.State);

        await _player.EnqueueAsync(Track("B"));
        await _player.EnqueueAsync(Track("C"));

        Assert.Equal("A", _player.Title); // still A
        Assert.Equal(["A", "B", "C"], QueueTitles());
    }

    [Fact]
    public async Task Play_next_goes_right_after_the_current_song()
    {
        await _player.PlayTracksAsync(Tracks("A", "B"), 0);

        await _player.PlayNextAsync(Track("X"));

        Assert.Equal(["A", "X", "B"], QueueTitles());
        _audio.RaiseEnded();
        Assert.Equal("X", _player.Title);
    }

    [Fact]
    public async Task Queue_entries_can_be_played_removed_and_the_queue_cleared()
    {
        await _player.PlayTracksAsync(Tracks("A", "B", "C", "D"), 0);

        await _player.PlayQueueItemCommand.ExecuteAsync(_player.QueueItems[2]);
        Assert.Equal("C", _player.Title);

        _player.RemoveQueueItemCommand.Execute(_player.QueueItems[3]);
        Assert.Equal(["A", "B", "C"], QueueTitles());

        _player.RemoveQueueItemCommand.Execute(_player.QueueItems[2]); // the playing song cannot be removed
        Assert.Equal(["A", "B", "C"], QueueTitles());

        _player.ClearQueueCommand.Execute(null);
        Assert.Equal(["C"], QueueTitles());
        Assert.Equal("C", _player.Title);
    }

    [Fact]
    public async Task The_cover_of_the_playing_song_is_exposed_and_follows_the_queue()
    {
        var covered = Track("A") with { ArtworkPath = Path.Combine(Path.GetTempPath(), "cover.jpg") };
        await _player.PlayTracksAsync([covered, Track("B")], 0);
        Assert.Equal(covered.ArtworkPath, _player.ArtworkPath);

        await _player.NextCommand.ExecuteAsync(null);
        Assert.Null(_player.ArtworkPath);
    }

    private string[] QueueTitles() => _player.QueueItems.Select(i => i.Track.Title).ToArray();

    private static TrackSummary Track(string title) =>
        new(Guid.NewGuid(), title, "Artist", "Album", TimeSpan.FromMinutes(3),
            new Uri(Path.Combine(Path.GetTempPath(), title + ".flac")).AbsoluteUri);

    private static List<TrackSummary> Tracks(params string[] titles) => titles.Select(Track).ToList();
}
