using Shirabe.Core.Library;
using Shirabe.Core.Playback;

namespace Shirabe.Client.ViewModels;

/// <summary>
/// What the player brings back after a restart. The song is a snapshot rather than a library id, so it also works for
/// a file that was dropped on the window and is not in the library. The queue itself is not part of it (yet).
/// </summary>
/// <param name="LastAudibleVolume">What un-muting goes back to; differs from <paramref name="Volume"/> only while muted.</param>
public sealed record PlayerSession(
    double Volume,
    double LastAudibleVolume,
    bool Shuffle,
    RepeatMode Repeat,
    TrackSummary? Track,
    double PositionSeconds);
