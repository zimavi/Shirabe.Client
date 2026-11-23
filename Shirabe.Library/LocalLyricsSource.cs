using System.Text;
using Shirabe.Core.Lyrics;

namespace Shirabe.Library;

/// <summary>
/// Lyrics that are already on the computer: a <c>.ttml</c> or <c>.lrc</c> file beside the song (same name), or lyrics in the
/// audio file's own tags. Timed lyrics win over plain ones; among equals the files beside the song (a deliberate choice)
/// win over the tags. Nothing here touches the network.
/// </summary>
public sealed class LocalLyricsSource : ILyricsSource
{
    /// <summary>Lyrics are a few kilobytes; the same limit as for metadata documents.</summary>
    private const long MaxSize = 1024 * 1024;

    public Task<SongLyrics?> FindAsync(string mediaUri, CancellationToken cancellationToken = default) =>
        Task.Run(() => Find(mediaUri), cancellationToken);

    private static SongLyrics? Find(string mediaUri)
    {
        if (!Uri.TryCreate(mediaUri, UriKind.Absolute, out var uri) || !uri.IsFile) return null;

        var audio = uri.LocalPath;
        var found = new List<SongLyrics>(); // most preferred first
        foreach (var extension in new[] { ".ttml", ".lrc" })
        {
            var path = Path.ChangeExtension(audio, extension);
            if (ReadText(path) is { } text && Parse(text, $"beside the song: {Path.GetFileName(path)}") is { } lyrics) found.Add(lyrics);
        }

        // Opening the audio file is the costly part: skip it when a sidecar already has timed lyrics.
        if (!found.Any(l => l.IsSynced)) found.AddRange(FromTags(audio));

        return found.FirstOrDefault(l => l.IsSynced) ?? found.FirstOrDefault();
    }

    private static IEnumerable<SongLyrics> FromTags(string audio)
    {
        const string where = "in the file's tags";
        TagLyrics tags;
        try
        {
            tags = TagReader.ReadLyrics(audio);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            yield break; // a file that cannot be read has no lyrics to show
        }

        if (SongLyrics.Of(tags.Synced, where) is { } synced) yield return synced;
        foreach (var text in tags.Texts)
            if (text.Length <= MaxSize && Parse(text, where) is { } lyrics) yield return lyrics;
    }

    // Judged by content, not by extension: tags and files are often not what their name says.
    private static SongLyrics? Parse(string text, string source) =>
        text.TrimStart().StartsWith('<')
            ? SongLyrics.Of(TtmlParser.Parse(text), source)
            : SongLyrics.Of(LrcParser.Parse(text), source);

    private static string? ReadText(string path)
    {
        try
        {
            var info = new FileInfo(path);
            // shortcut: read as UTF-8 (a byte order mark is honoured); lyrics in a legacy code page come out garbled, detect when someone reports it
            return info is { Exists: true } && info.Length <= MaxSize ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
