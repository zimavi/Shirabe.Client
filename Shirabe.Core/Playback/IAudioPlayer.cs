using Shirabe.Core.Media;

namespace Shirabe.Core.Playback;

public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>The bands of <see cref="IAudioPlayer.Equalizer"/>: ten, an octave apart.</summary>
public static class EqualizerBands
{
    public static IReadOnlyList<double> Frequencies { get; } = [31.25, 62.5, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    /// <summary>The furthest a band goes either way, in dB.</summary>
    public const double MaxGain = 12;
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

    /// <summary>
    /// How fast the source plays, 1 being as recorded; the pitch moves with it unless <see cref="KeepPitch"/> is on.
    /// <see cref="Position"/> and <see cref="Duration"/> stay in the source's own time.
    /// </summary>
    double Speed { get; set; }

    /// <summary>Whether <see cref="KeepPitch"/>, <see cref="Reverb"/> and <see cref="Equalizer"/> do anything.</summary>
    bool SupportsEffects { get; }

    /// <summary>A change of <see cref="Speed"/> leaves the pitch alone.</summary>
    bool KeepPitch { get; set; }

    /// <summary>Adds a room reverb.</summary>
    bool Reverb { get; set; }

    /// <summary>Gain in dB for each of <see cref="EqualizerBands.Frequencies"/>; empty or all zero is off.</summary>
    IReadOnlyList<double> Equalizer { get; set; }

    /// <summary>A second factor on the samples, for loudness normalisation. Unlike <see cref="Volume"/> it may exceed 1.</summary>
    float Gain { get; set; }

    /// <summary>Raised when <see cref="State"/> changes. May be raised on a non-UI thread.</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised when the loaded source plays to its end. May be raised on a non-UI thread.</summary>
    event EventHandler? Ended;

    /// <summary>Replaces the current source and leaves the player paused at the start.</summary>
    Task LoadAsync(IMediaSource source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lets go of the loaded source and closes its file, so that something else can write to it. The player is then
    /// <see cref="PlaybackState.Stopped"/> with nothing loaded; <see cref="LoadAsync"/> brings a source back.
    /// </summary>
    void Release();

    void Play();

    void Pause();

    void Stop();

    void Seek(TimeSpan position);
}
