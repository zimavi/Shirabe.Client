using System.Globalization;

using Shirabe.Core.Lyrics;
using Shirabe.Core.Metadata;
using Shirabe.Core.Playback;

namespace Shirabe.Library;

internal sealed record FileTags(
    string Title,
    string? Artist,
    string? AlbumArtist,
    string? Album,
    int? Year,
    int? DiscNumber,
    int? TrackNumber,
    TimeSpan? Duration,
    byte[]? Cover,
    string? EmbeddedDocument);

/// <summary>Lyrics found in a file's tags: timed lines (ID3 SYLT) and texts (USLT, Vorbis LYRICS...), which may themselves be LRC or TTML.</summary>
internal sealed record TagLyrics(IReadOnlyList<LyricLine> Synced, IReadOnlyList<string> Texts);

internal static class TagReader
{
    private const int MaxListedValue = 300;

    static TagReader()
    {
        ATL.Settings.OutputStacktracesToConsole = false;
        // Otherwise ATL reports the file name as the title of a file without one, and saves it into the file on a write.
        ATL.Settings.UseFileNameWhenNoTitle = false;
#if DEBUG
        // ATL does not throw for most of what goes wrong (a corrupt tag, an unsupported frame); it only logs. Debug builds show that log.
        var log = new ATL.Logging.Log();
        log.Register(new AtlConsoleLog());
        log.SwitchSync(); // written when it happens, not from a background thread later
        ATL.Logging.LogDelegator.SetLog(ref log);
#endif
    }

#if DEBUG
    /// <summary>ATL's warnings and errors, on the console like the rest of the Debug log (Services/DebugLog.cs).</summary>
    private sealed class AtlConsoleLog : ATL.Logging.ILogDevice
    {
        public void DoLog(ATL.Logging.Log.LogItem item)
        {
            // ATL's levels count down: error 1, warning 2, info 4, debug 8.
            if (item.Level > ATL.Logging.Log.LV_WARNING) return;

            var level = item.Level <= ATL.Logging.Log.LV_ERROR ? "error" : "warning";
            Console.Error.WriteLine($"[Shirabe] ATL {level}: {item.Message}{(string.IsNullOrEmpty(item.Location) ? "" : $" ({item.Location})")}");
        }
    }
#endif

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
            Duration: track.DurationMs > 0 ? TimeSpan.FromMilliseconds(track.DurationMs) : null,
            Cover: FrontCover(track),
            EmbeddedDocument: EmbeddedDocument(track));
    }

    /// <summary>Writes the <c>SHIRABE_METADATA</c> tag into the audio file, leaving its other tags as ATL read them.</summary>
    /// <exception cref="IOException">The format cannot be written, or the write failed.</exception>
    public static void WriteEmbeddedDocument(string path, string json)
    {
        // ATL hides a sharing violation behind "could not save"; say what is wrong instead of blaming the format.
        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }
        catch (IOException ex) when (ex is not (FileNotFoundException or DirectoryNotFoundException))
        {
            throw new IOException("The audio file is open in another program (a player, for example), so it cannot be written to.", ex);
        }
        catch (UnauthorizedAccessException)
        {
            // read-only: Save below reports it the same way as before
        }

        var track = new ATL.Track(path);
        track.AdditionalFields["SHIRABE_METADATA"] = json;
        if (!track.Save()) throw new IOException("The tag could not be written (this format may not support writing tags).");
    }

    /// <exception cref="InvalidDataException">The file is not audio, or it is corrupt.</exception>
    public static TagLyrics ReadLyrics(string path)
    {
        var track = new ATL.Track(path);
        IReadOnlyList<LyricLine> synced = [];
        var texts = new List<string>();

        foreach (var info in track.Lyrics)
        {
            if (synced.Count == 0 && info.SynchronizedLyrics is { Count: > 0 } phrases)
                synced = [.. phrases
                    .Select(p => new LyricLine(
                        TimeSpan.FromMilliseconds(p.TimestampStart),
                        p.TimestampEnd > p.TimestampStart ? TimeSpan.FromMilliseconds(p.TimestampEnd) : null,
                        (p.Text ?? "").Trim()))
                    .OrderBy(l => l.Start)];

            if (!string.IsNullOrWhiteSpace(info.UnsynchronizedLyrics)) texts.Add(info.UnsynchronizedLyrics);
        }

        return new TagLyrics(synced, texts);
    }

    /// <summary>Null when the file has neither a track nor an album gain. ATL lists these tags under their raw names.</summary>
    // shortcut: Opus files that only carry R128_*_GAIN are not normalised, add them when someone has such files
    public static ReplayGain? ReadReplayGain(string path)
    {
        var track = new ATL.Track(path);

        double? Value(string name)
        {
            var text = track.AdditionalFields
                .FirstOrDefault(f => f.Key.EndsWith(name, StringComparison.OrdinalIgnoreCase)).Value?.Trim();
            if (text is null) return null;
            if (text.EndsWith("dB", StringComparison.OrdinalIgnoreCase)) text = text[..^2];

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                   && double.IsFinite(number) ? number : null;
        }

        var gain = new ReplayGain(Value("REPLAYGAIN_TRACK_GAIN"), Value("REPLAYGAIN_TRACK_PEAK"),
            Value("REPLAYGAIN_ALBUM_GAIN"), Value("REPLAYGAIN_ALBUM_PEAK"));
        return gain.TrackGainDb is null && gain.AlbumGainDb is null ? null : gain;
    }

    /// <summary>The <c>SHIRABE_METADATA</c> tag (standard, section 13.3). ATL files it under that name for ID3v2 and Vorbis comments; MP4 freeform atoms may carry a prefix.</summary>
    private static string? EmbeddedDocument(ATL.Track track) =>
        track.AdditionalFields
            .Where(f => f.Key.EndsWith("SHIRABE_METADATA", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(f.Value))
            .Select(f => f.Value)
            .FirstOrDefault();

    /// <summary>
    /// Everything the file's tags hold that has a value, in a readable order, then the audio facts. Tags ATL does not
    /// know by name (ReplayGain, a custom <c>SHIRABE_METADATA</c>, vendor strings) come last under their raw names.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not audio, or it is corrupt.</exception>
    public static IReadOnlyList<FileTag> ReadAll(string path)
    {
        var track = new ATL.Track(path);
        if (track.DurationMs <= 0)
            throw new InvalidDataException("No playable audio found (unsupported format or corrupt file).");

        var rows = new List<FileTag>();
        void Tag(string name, string? value) => Add(FileTag.TagsGroup, name, value);
        void Audio(string name, string? value) => Add(FileTag.AudioGroup, name, value);
        void Add(string group, string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            // An embedded metadata document can be many kilobytes; the listing shows its start and size, Studio shows the whole.
            value = value.Trim();
            if (value.Length > MaxListedValue) value = $"{value[..MaxListedValue]}... ({value.Length:N0} characters)";
            rows.Add(new FileTag(group, name, value));
        }

        Tag("Title", track.Title);
        Tag("Artist", track.Artist);
        Tag("Album artist", track.AlbumArtist);
        Tag("Album", track.Album);
        Tag("Track", Pair(track.TrackNumber, track.TrackTotal));
        Tag("Disc", Pair(track.DiscNumber, track.DiscTotal));
        Tag("Year", Positive(track.Year)?.ToString());
        Tag("Date", track.Date is { Year: > 1 } date ? date.ToString("yyyy-MM-dd") : null);
        Tag("Original release year", Positive(track.OriginalReleaseYear)?.ToString());
        Tag("Genre", track.Genre);
        Tag("Composer", track.Composer);
        Tag("Lyricist", track.Lyricist);
        Tag("Conductor", track.Conductor);
        Tag("Publisher", track.Publisher);
        Tag("Copyright", track.Copyright);
        Tag("ISRC", track.ISRC);
        Tag("Catalogue number", track.CatalogNumber);
        Tag("BPM", track.BPM is > 0 ? Math.Round(track.BPM.Value).ToString("0") : null);
        Tag("Language", track.Language);
        Tag("Group", track.Group);
        Tag("Original artist", track.OriginalArtist);
        Tag("Original album", track.OriginalAlbum);
        Tag("Sort title", track.SortTitle);
        Tag("Sort artist", track.SortArtist);
        Tag("Sort album", track.SortAlbum);
        Tag("Sort album artist", track.SortAlbumArtist);
        Tag("Comment", track.Comment);
        Tag("Description", track.Description);
        Tag("Encoded by", track.EncodedBy);
        Tag("Encoder", track.Encoder);
        if (track.Lyrics.Count > 0) Tag("Lyrics", track.Lyrics.Count == 1 ? "1 embedded text" : $"{track.Lyrics.Count} embedded texts");
        if (track.EmbeddedPictures.Count > 0)
            Tag("Pictures", string.Join(", ", track.EmbeddedPictures.Select(p => p.PicType.ToString().ToLowerInvariant())));

        foreach (var (name, value) in track.AdditionalFields) Tag(name, value);

        Audio("Format", track.AudioFormat?.Name);
        Audio("Tag format", string.Join(", ", track.MetadataFormats.Select(f => f.Name)));
        Audio("Duration", TimeSpan.FromMilliseconds(track.DurationMs).ToString(track.DurationMs >= 3_600_000 ? @"h\:mm\:ss" : @"m\:ss"));
        Audio("Bit rate", track.Bitrate > 0 ? $"{track.Bitrate:N0} kbps{(track.IsVBR ? " (variable)" : "")}" : null);
        Audio("Sample rate", track.SampleRate > 0 ? $"{track.SampleRate:N0} Hz" : null);
        Audio("Bit depth", track.BitDepth > 0 ? $"{track.BitDepth} bit" : null);
        Audio("Channels", track.ChannelsArrangement?.Description);
        return rows;
    }

    private static string? Pair(int? number, int? total) =>
        number is > 0 ? (total is > 0 ? $"{number}/{total}" : number.ToString()) : null;

    private static byte[]? FrontCover(ATL.Track track)
    {
        var pictures = track.EmbeddedPictures;
        var picture = pictures.FirstOrDefault(p => p.PicType == ATL.PictureInfo.PIC_TYPE.Front) ?? pictures.FirstOrDefault();
        return picture?.PictureData is { Length: > 0 } data ? data : null;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static int? Positive(int? value) => value is > 0 ? value : null;
}
