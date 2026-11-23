namespace Shirabe.Core.Lyrics;

/// <summary>Finds the lyrics of a song. Local files today; an online provider can be another implementation.</summary>
public interface ILyricsSource
{
    /// <param name="mediaUri">Where the song's audio is, e.g. <c>file:///C:/Music/a.flac</c>.</param>
    /// <returns>The lyrics, or null when there are none. Problems with a file mean "none", never an exception.</returns>
    Task<SongLyrics?> FindAsync(string mediaUri, CancellationToken cancellationToken = default);
}
