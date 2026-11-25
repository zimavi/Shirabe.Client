namespace Shirabe.Core.Playback;

public enum ScrobbleStep
{
    None,

    /// <summary>The song began to play: it is "now playing".</summary>
    Started,

    /// <summary>Enough of it was heard to count as a listen. Once per play.</summary>
    Earned,
}

/// <summary>
/// Decides when a loaded song counts as listened to, by the rules scrobbling services share: it must be longer than
/// 30 seconds, and half of it or 4 minutes (whichever is less) must have been heard. Only time that actually played
/// counts, not what a seek passed over. Make one per load of a song and feed it the position about once a second.
/// </summary>
/// <param name="duration">Length of the song, or null when unknown (then 4 minutes must be heard).</param>
public sealed class ScrobbleTracker(TimeSpan? duration)
{
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LongestThreshold = TimeSpan.FromMinutes(4);

    // Playing moves the position by about the feeding interval (twice that at double speed); more is a jump.
    private static readonly TimeSpan LongestStep = TimeSpan.FromSeconds(5);

    // Rewinding to here after the song has counted begins a new play (repeat by hand, "previous", play after the end).
    private static readonly TimeSpan Beginning = TimeSpan.FromSeconds(5);

    private TimeSpan _position;
    private TimeSpan _heard;
    private bool _earned;

    /// <summary>When this play began, known from <see cref="ScrobbleStep.Started"/> on.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>The user moved the position: nothing in between was heard.</summary>
    public void Seek(TimeSpan position)
    {
        if (_earned && position < _position && position < Beginning)
        {
            _earned = false;
            _heard = TimeSpan.Zero;
            StartedAt = null;
        }

        _position = position;
    }

    /// <summary>The position now; call it regularly, paused or not.</summary>
    public ScrobbleStep Advance(TimeSpan position, DateTimeOffset now)
    {
        var step = position - _position;
        Seek(position); // also notices a rewind nobody announced
        if (step <= TimeSpan.Zero || step > LongestStep) return ScrobbleStep.None;

        _heard += step;
        if (StartedAt is null)
        {
            StartedAt = now - step;
            return ScrobbleStep.Started;
        }

        if (_earned || duration <= MinimumDuration) return ScrobbleStep.None;

        var needed = duration / 2 < LongestThreshold ? duration.Value / 2 : LongestThreshold;
        if (_heard < needed) return ScrobbleStep.None;

        _earned = true;
        return ScrobbleStep.Earned;
    }
}