namespace Shirabe.Core.Playback;

/// <summary>Which ReplayGain value evens out the loudness. The names are saved in settings.json: do not rename them.</summary>
public enum NormalizationMode
{
    Off,
    Track,
    Album,
}

/// <summary>The ReplayGain values a file carries. Peaks are the loudest sample, 1 being full scale.</summary>
public sealed record ReplayGain(double? TrackGainDb, double? TrackPeak, double? AlbumGainDb, double? AlbumPeak)
{
    /// <summary>
    /// The factor to play the samples at. The other mode's value stands in for a missing one. A boost never goes past
    /// the peak, and without a known peak there is no boost, so normalising cannot clip.
    /// </summary>
    public float Factor(NormalizationMode mode)
    {
        if (mode == NormalizationMode.Off) return 1;

        var album = mode == NormalizationMode.Album ? AlbumGainDb is not null : TrackGainDb is null;
        if ((album ? AlbumGainDb : TrackGainDb) is not { } db) return 1;

        var peak = album ? AlbumPeak : TrackPeak;
        var limit = peak is > 0 ? 1 / peak.Value : 1;
        return (float)Math.Min(Math.Pow(10, Math.Clamp(db, -30, 30) / 20), limit);
    }
}

public interface IReplayGainSource
{
    /// <returns>Null when the media has no ReplayGain values or cannot be read.</returns>
    Task<ReplayGain?> ReadAsync(string mediaUri, CancellationToken cancellationToken = default);
}
