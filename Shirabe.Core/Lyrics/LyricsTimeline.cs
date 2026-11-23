namespace Shirabe.Core.Lyrics;

public static class LyricsTimeline
{
    /// <summary>
    /// The line that is current at <paramref name="position"/>: the last one that has started. -1 before the first line, and
    /// for lines without times. It depends on the position alone, so seeking, looping and restoring a session need no state.
    /// </summary>
    /// <param name="lines">Sorted by start, as the parsers return them.</param>
    public static int CurrentIndex(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        int low = 0, high = lines.Count - 1, found = -1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (lines[middle].Start is { } start && start <= position)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }
}
