namespace Shirabe.Core.Media;

/// <summary>
/// A playable piece of media. The player only ever sees a stream, so local files and
/// remote providers look the same to it.
/// </summary>
public interface IMediaSource
{
    /// <summary>Opens a new readable, seekable stream. The caller owns and disposes it.</summary>
    Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default);
}
