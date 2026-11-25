using Shirabe.Core.Playback;

namespace Shirabe.Library;

/// <summary>ReplayGain from the tags of a local file, read when the song is about to play.</summary>
public sealed class ReplayGainReader : IReplayGainSource
{
    public async Task<ReplayGain?> ReadAsync(string mediaUri, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(mediaUri, UriKind.Absolute, out var location) || !location.IsFile) return null;

        try
        {
            return await Task.Run(() => TagReader.ReadReplayGain(location.LocalPath), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // playing the song reports what is wrong with the file
        }
    }
}
