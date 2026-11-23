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

    /// <summary>How many lines back <see cref="Active"/> looks for a line that is still being sung (overlaps are short).</summary>
    private const int OverlapLookBack = 16;

    /// <summary>
    /// Every line that is being sung at <paramref name="position"/>: the current one (the last that has started), and earlier
    /// ones whose own end has not come yet, because a line can start while the one before it is still going (a lead and an echo,
    /// an overlapping repeat). The current line stays active after its own end until the next one starts, as before.
    /// Like <see cref="CurrentIndex"/> it depends on the position alone.
    /// </summary>
    /// <param name="into">Cleared and filled: the current line first, then the earlier ones, latest first.</param>
    public static void Active(IReadOnlyList<LyricLine> lines, TimeSpan position, List<int> into)
    {
        into.Clear();
        var current = CurrentIndex(lines, position);
        if (current < 0) return;

        into.Add(current);
        for (var i = current - 1; i >= Math.Max(0, current - OverlapLookBack); i--)
            if (lines[i].End is { } end && end > position)
                into.Add(i);
    }
}