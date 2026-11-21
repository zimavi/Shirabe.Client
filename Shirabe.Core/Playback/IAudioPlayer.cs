using Shirabe.Core.Media;

namespace Shirabe.Core.Playback;

public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>
/// Plays one source at a time. Independent of any UI framework and of the engine behind it.
/// </summary>
public interface IAudioPlayer : IDisposable
{
    PlaybackState State { get; }

    /// <summary>
    /// Current position read from the engine's playback clock, not from a UI timer. Dynamic
    /// presentation relies on this staying accurate across seeks.
    /// </summary>
    TimeSpan Position { get; }

    /// <summary>Length of the loaded source, or null if nothing is loaded or it is unknown.</summary>
    TimeSpan? Duration { get; }

    /// <summary>0.0 (silent) to 1.0 (full).</summary>
    float Volume { get; set; }

    /// <summary>Raised when <see cref="State"/> changes. May be raised on a non-UI thread.</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised when the loaded source plays to its end. May be raised on a non-UI thread.</summary>
    event EventHandler? Ended;

    /// <summary>Replaces the current source and leaves the player paused at the start.</summary>
    Task LoadAsync(IMediaSource source, CancellationToken cancellationToken = default);

    void Play();

    void Pause();

    void Stop();

    void Seek(TimeSpan position);
}
