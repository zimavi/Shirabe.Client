namespace Shirabe.Library;

internal sealed record FileTags(
    string Title,
    string? Artist,
    string? AlbumArtist,
    string? Album,
    int? Year,
    int? DiscNumber,
    int? TrackNumber,
    TimeSpan? Duration);

internal static class TagReader
{
    static TagReader() => ATL.Settings.OutputStacktracesToConsole = false;

    /// <summary>Reads the embedded tags. A file without a title tag is titled after its file name.</summary>
    /// <exception cref="InvalidDataException">The file is not audio, or it is corrupt.</exception>
    public static FileTags Read(string path)
    {
        var track = new ATL.Track(path);

        // ATL does not throw for non-audio or corrupt files: it picks a reader by extension and returns an
        // empty track. A file without any audio duration cannot be played, so it does not belong in the library.
        if (track.DurationMs <= 0)
            throw new InvalidDataException("No playable audio found (unsupported format or corrupt file).");

        return new FileTags(
            Title: Clean(track.Title) ?? Path.GetFileNameWithoutExtension(path),
            Artist: Clean(track.Artist),
            AlbumArtist: Clean(track.AlbumArtist),
            Album: Clean(track.Album),
            Year: Positive(track.Year),
            DiscNumber: Positive(track.DiscNumber),
            TrackNumber: Positive(track.TrackNumber),
            Duration: track.DurationMs > 0 ? TimeSpan.FromMilliseconds(track.DurationMs) : null);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static int? Positive(int? value) => value is > 0 ? value : null;
}
