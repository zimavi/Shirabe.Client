namespace Shirabe.Core.Library;

/// <summary>What the UI needs to know about a library. The local SQLite library is the only implementation so far.</summary>
public interface ILibrary
{
    /// <summary>All playable recordings, sorted by title. Recordings whose files are missing are left out.</summary>
    Task<IReadOnlyList<TrackSummary>> GetTracksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Albums with at least one playable recording, in no particular order: the releases of the tags, plus what metadata
    /// documents name for songs that have none. Songs with neither are not in it.
    /// </summary>
    Task<IReadOnlyList<AlbumSummary>> GetAlbumsAsync(CancellationToken cancellationToken = default);
}

/// <param name="Id">
/// The release's id; for an album only documents name, one derived from its name and artist, which changes with them and
/// so must not be stored.
/// </param>
/// <param name="Title">The metadata documents' album name when every song resolves to the same one, otherwise the tags'.</param>
/// <param name="Artist">The album artist: from the tags, or from the documents for an album only they name.</param>
/// <param name="Tracks">The playable songs in disc and track order; never empty.</param>
public sealed record AlbumSummary(
    Guid Id,
    string Title,
    string? Artist,
    int? Year,
    IReadOnlyList<AlbumTrack> Tracks)
{
    /// <summary>The cover of the first song that has one.</summary>
    public string? ArtworkPath => Tracks.Select(t => t.Track.ArtworkPath).FirstOrDefault(path => path is not null);
}

public sealed record AlbumTrack(TrackSummary Track, int? DiscNumber, int? TrackNumber);

/// <param name="MediaUri">Where to open the audio from, e.g. <c>file:///C:/Music/a.flac</c>.</param>
/// <param name="ArtworkPath">Full path of the cover image, or null when the file has no embedded cover.</param>
/// <param name="CoverUrl">https address of the front cover named by a metadata document, or null.</param>
/// <param name="LinkUrl">https address of a page about the recording named by a metadata document, with its <paramref name="LinkLabel"/>.</param>
public sealed record TrackSummary(
    Guid RecordingId,
    string Title,
    string? Artist,
    string? Album,
    TimeSpan? Duration,
    string MediaUri,
    string? ArtworkPath = null,
    string? CoverUrl = null,
    string? LinkUrl = null,
    string? LinkLabel = null,
    string? CoverSha256 = null);

/// <summary>Pictures that metadata documents link to (standard, section 7). Fetching is always the caller's decision.</summary>
public interface IArtworkLinks
{
    /// <summary>
    /// The path of the picture at <paramref name="url"/>, downloading it first unless it was downloaded before; null when it
    /// cannot be had (no https, a failed or oversized download, a hash that does not match, not an image).
    /// </summary>
    Task<string?> FetchAsync(string url, string? sha256, CancellationToken cancellationToken = default);
}