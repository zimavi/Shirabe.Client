namespace Shirabe.Core.Library;

/// <summary>What the UI needs to know about a library. The local SQLite library is the only implementation so far.</summary>
public interface ILibrary
{
    /// <summary>All playable recordings, sorted by title. Recordings whose files are missing are left out.</summary>
    Task<IReadOnlyList<TrackSummary>> GetTracksAsync(CancellationToken cancellationToken = default);
}

/// <param name="MediaUri">Where to open the audio from, e.g. <c>file:///C:/Music/a.flac</c>.</param>
public sealed record TrackSummary(
    Guid RecordingId,
    string Title,
    string? Artist,
    string? Album,
    TimeSpan? Duration,
    string MediaUri);
