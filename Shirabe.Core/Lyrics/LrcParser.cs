using System.Globalization;
using System.Text.RegularExpressions;

namespace Shirabe.Core.Lyrics;

/// <summary>
/// Reads LRC text: lines like <c>[01:23.45]words</c>, optionally with several stamps for a repeated line, an
/// <c>[offset:ms]</c> tag, other tags (<c>[ar:..]</c>, <c>[ti:..]</c>) that are skipped, and word stamps
/// (<c>&lt;01:23.45&gt;</c>) that are dropped. Text without any time stamp is plain lyrics.
/// </summary>
public static partial class LrcParser
{
    // [mm:ss], [mm:ss.x], [mm:ss.xx], [mm:ss.xxx]; some writers use ':' before the fraction.
    [GeneratedRegex(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex Stamp();

    [GeneratedRegex(@"^\s*\[([A-Za-z#]+):(.*)\]\s*$")]
    private static partial Regex Tag();

    [GeneratedRegex(@"<\d{1,3}:\d{2}(?:[.:]\d{1,3})?>")]
    private static partial Regex WordStamp();

    /// <returns>The lines in time order, or in written order (no times) for plain lyrics. Empty for empty text.</returns>
    public static IReadOnlyList<LyricLine> Parse(string text)
    {
        var rows = text.TrimStart('﻿').Split('\n').Select(r => r.TrimEnd('\r')).ToList();

        var offset = TimeSpan.Zero;
        var timed = new List<LyricLine>();
        var plain = new List<LyricLine>();

        foreach (var row in rows)
        {
            if (Tag().Match(row) is { Success: true } tag && !Stamp().IsMatch(row))
            {
                if (tag.Groups[1].Value.Equals("offset", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(tag.Groups[2].Value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ms))
                    offset = TimeSpan.FromMilliseconds(ms);
                continue; // the other tags describe the song, not the lyrics
            }

            // Every stamp at the start of the row; the rest is the line.
            var starts = new List<TimeSpan>();
            var rest = row;
            while (Stamp().Match(rest) is { Success: true, Index: 0 } stamp)
            {
                starts.Add(ToTime(stamp));
                rest = rest[stamp.Length..];
            }

            var line = WordStamp().Replace(rest, "").Trim();
            if (starts.Count == 0) plain.Add(new LyricLine(null, null, line));
            else foreach (var start in starts) timed.Add(new LyricLine(start, null, line));
        }

        if (timed.Count > 0)
        {
            // A positive offset means the lyrics should show earlier (the LRC convention).
            return [.. timed.Select(l => l with { Start = Max(l.Start!.Value - offset, TimeSpan.Zero) })
                .OrderBy(l => l.Start)]; // OrderBy is stable: two lines at one time stay in written order
        }

        // Plain lyrics: no blank lines at the ends, and no more than one in a row.
        var lines = new List<LyricLine>();
        foreach (var line in plain)
        {
            if (line.Text.Length == 0 && (lines.Count == 0 || lines[^1].Text.Length == 0)) continue;
            lines.Add(line);
        }

        if (lines.Count > 0 && lines[^1].Text.Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static TimeSpan ToTime(Match stamp)
    {
        var fraction = stamp.Groups[3].Success ? double.Parse("0." + stamp.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
        return TimeSpan.FromMinutes(int.Parse(stamp.Groups[1].Value, CultureInfo.InvariantCulture))
               + TimeSpan.FromSeconds(int.Parse(stamp.Groups[2].Value, CultureInfo.InvariantCulture) + fraction);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
