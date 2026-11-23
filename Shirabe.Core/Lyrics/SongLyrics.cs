namespace Shirabe.Core.Lyrics;

/// <summary>One word (or syllable) of a line, with the time it is sung.</summary>
/// <param name="SpaceAfter">The file puts a space after it; false inside a word that is sung in several parts.</param>
public sealed record LyricWord(TimeSpan Start, TimeSpan End, string Text, bool SpaceAfter = false);

/// <summary>
/// One line of lyrics. <see cref="Start"/> is null for plain (unsynchronized) lyrics. <see cref="End"/> is only known
/// for some formats; a line is current from its start until the next line starts. An empty <see cref="Text"/> is a pause.
/// <see cref="Background"/> is the background vocal (already in <see cref="Text"/>), which is not part of <see cref="Words"/>.
/// </summary>
public sealed record LyricLine(TimeSpan? Start, TimeSpan? End, string Text, IReadOnlyList<LyricWord>? Words = null, string? Background = null);

/// <summary>The lyrics of one song, and where they came from.</summary>
/// <param name="Source">A sentence for the person: where these lyrics were found.</param>
public sealed record SongLyrics(IReadOnlyList<LyricLine> Lines, bool IsSynced, string Source)
{
    /// <returns>The lyrics, or null when there is no line with any text in them.</returns>
    public static SongLyrics? Of(IReadOnlyList<LyricLine>? lines, string source) =>
        lines is { Count: > 0 } && lines.Any(l => l.Text.Length > 0)
            ? new SongLyrics(lines, lines.All(l => l.Start is not null), source)
            : null;
}
