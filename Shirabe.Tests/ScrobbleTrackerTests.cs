using Shirabe.Core.Playback;

using Xunit;

namespace Shirabe.Tests;

public class ScrobbleTrackerTests
{
    private static readonly DateTimeOffset Noon = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static TimeSpan Seconds(double value) => TimeSpan.FromSeconds(value);

    /// <summary>Plays second by second from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static List<(int At, ScrobbleStep Step)> Play(ScrobbleTracker tracker, int from, int to)
    {
        var steps = new List<(int, ScrobbleStep)>();
        for (var second = from + 1; second <= to; second++)
        {
            var step = tracker.Advance(Seconds(second), Noon.AddSeconds(second));
            if (step != ScrobbleStep.None) steps.Add((second, step));
        }

        return steps;
    }

    [Theory]
    [InlineData(31, 16)] // half, rounded up to the next second we hear about
    [InlineData(200, 100)]
    [InlineData(480, 240)]
    [InlineData(3600, 240)] // never more than 4 minutes
    [InlineData(null, 240)] // unknown length
    public void A_song_counts_after_half_of_it_or_four_minutes(int? length, int earnedAt)
    {
        var tracker = new ScrobbleTracker(length is { } value ? Seconds(value) : null);

        var steps = Play(tracker, 0, length ?? 600);

        Assert.Equal([(1, ScrobbleStep.Started), (earnedAt, ScrobbleStep.Earned)], steps);
        Assert.Equal(Noon, tracker.StartedAt);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(5)]
    [InlineData(0)]
    public void A_song_of_30_seconds_or_less_never_counts(int length)
    {
        var steps = Play(new ScrobbleTracker(Seconds(length)), 0, 60);

        Assert.Equal([(1, ScrobbleStep.Started)], steps);
    }

    [Fact]
    public void A_pause_counts_nothing()
    {
        var tracker = new ScrobbleTracker(Seconds(100));
        Play(tracker, 0, 40);

        for (var i = 0; i < 100; i++)
            Assert.Equal(ScrobbleStep.None, tracker.Advance(Seconds(40), Noon.AddSeconds(41 + i)));

        Assert.Equal([(50, ScrobbleStep.Earned)], Play(tracker, 40, 100));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // a jump nobody announced is recognised by its size
    public void What_a_seek_passes_over_is_not_heard(bool announced)
    {
        var tracker = new ScrobbleTracker(Seconds(100));
        Play(tracker, 0, 10);
        if (announced) tracker.Seek(Seconds(80));

        // 10 s heard before; 40 more are needed, and only 20 are left
        Assert.Empty(Play(tracker, announced ? 80 : 79, 100));
        Assert.Equal([(20, ScrobbleStep.Earned)], Play(Rewound(tracker), 0, 100));

        static ScrobbleTracker Rewound(ScrobbleTracker tracker)
        {
            tracker.Seek(TimeSpan.Zero);
            return tracker;
        }
    }

    [Fact]
    public void Short_announced_seeks_do_not_add_up_to_listening()
    {
        var tracker = new ScrobbleTracker(Seconds(100));
        Play(tracker, 0, 2);

        for (var second = 6; second <= 98; second += 4) tracker.Seek(Seconds(second));

        Assert.Empty(Play(tracker, 98, 100));
    }

    [Fact]
    public void Playing_at_double_speed_counts_the_song_that_was_heard()
    {
        var tracker = new ScrobbleTracker(Seconds(100));

        var steps = Enumerable.Range(1, 30)
            .Select(i => tracker.Advance(Seconds(i * 2), Noon.AddSeconds(i)))
            .ToList();

        Assert.Equal(24, steps.IndexOf(ScrobbleStep.Earned)); // the 25th second of listening: 50 s of the song
    }

    [Fact]
    public void A_song_loaded_part_way_through_starts_when_it_plays_and_counts_only_what_follows()
    {
        var tracker = new ScrobbleTracker(Seconds(100));

        Assert.Equal(ScrobbleStep.None, tracker.Advance(Seconds(60), Noon)); // a restored session
        Assert.Null(tracker.StartedAt);

        Assert.Equal([(61, ScrobbleStep.Started)], Play(tracker, 60, 100));
        Assert.Equal(Noon.AddSeconds(60), tracker.StartedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Starting_over_after_a_counted_play_is_a_new_play(bool announced)
    {
        var tracker = new ScrobbleTracker(Seconds(100));
        Play(tracker, 0, 100);
        if (announced) tracker.Seek(TimeSpan.Zero);
        else tracker.Advance(TimeSpan.Zero, Noon.AddSeconds(100)); // the player stopped and rewound

        var steps = Play(tracker, 0, 100);

        Assert.Equal([(1, ScrobbleStep.Started), (50, ScrobbleStep.Earned)], steps);
    }

    [Fact]
    public void Seeking_back_into_the_middle_after_a_counted_play_does_not_count_it_again()
    {
        var tracker = new ScrobbleTracker(Seconds(100));
        Play(tracker, 0, 80);

        tracker.Seek(Seconds(10));

        Assert.Empty(Play(tracker, 10, 100));
    }
}