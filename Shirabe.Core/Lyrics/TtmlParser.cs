using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Shirabe.Core.Lyrics;

/// <summary>
/// Reads TTML lyrics as Apple Music and Better Lyrics write them: every <c>&lt;p begin end&gt;</c> is a line, the timed
/// <c>&lt;span&gt;</c>s inside it are its words. Background vocals (<c>ttm:role="x-bg"</c>) are added to the line in
/// parentheses; translations and transliterations (<c>x-translation</c>, <c>x-roman</c>) are left out for now. The file is
/// untrusted: documents with a DOCTYPE (where entity-expansion attacks live) are refused, and nothing is fetched.
/// </summary>
public static partial class TtmlParser
{
    [GeneratedRegex(@"^(?:(\d+):)?(\d+):(\d+(?:\.\d+)?)$")]
    private static partial Regex Clock();

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)(h|m|s|ms)?$")]
    private static partial Regex Offset();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <returns>The lines in time order; null when the text is not well-formed TTML.</returns>
    public static IReadOnlyList<LyricLine>? Parse(string xml)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml.TrimStart('﻿')),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        if (document.Root is not { } root || root.Name.LocalName != "tt") return null;

        var lines = new List<LyricLine>();
        foreach (var p in root.Descendants().Where(e => e.Name.LocalName == "p"))
            if (ReadLine(p) is { } line) lines.Add(line);

        return [.. lines.OrderBy(l => l.Start)];
    }

    private static LyricLine? ReadLine(XElement p)
    {
        var text = new StringBuilder();
        var background = new List<string>();
        var words = new List<LyricWord>();

        foreach (var node in p.Nodes())
        {
            switch (node)
            {
                case XText t:
                    text.Append(t.Value);
                    break;
                case XElement { Name.LocalName: "br" }:
                    text.Append(' ');
                    break;
                case XElement span when span.Name.LocalName == "span":
                    switch (Attribute(span, "role"))
                    {
                        case "x-translation" or "x-roman":
                            break;
                        case "x-bg":
                            if (Clean(span.Value) is { Length: > 0 } sung) background.Add(sung);
                            break;
                        default:
                            // shortcut: spacing comes from the file's own whitespace between spans (Apple's rule); a file with
                            // no whitespace at all between word spans reads as one long word, check against a real file
                            text.Append(span.Value);
                            if (Time(span, "begin") is { } from && Time(span, "end") is { } to)
                                words.Add(new LyricWord(from, to, span.Value.Trim()));
                            break;
                    }

                    break;
            }
        }

        // A line without its own start takes its first timed word's; one with neither cannot be placed in time.
        var start = Time(p, "begin") ?? words.Cast<LyricWord?>().FirstOrDefault()?.Start;
        if (start is null) return null;

        var end = Time(p, "end") ?? words.Cast<LyricWord?>().LastOrDefault()?.End;
        var line = Clean(text.ToString());
        // Apple writes the parentheses into the background text itself; add them only where the file did not.
        foreach (var sung in background) line = Clean($"{line} {(sung.StartsWith('(') ? sung : $"({sung})")}");

        return new LyricLine(start, end, line, words.Count > 0 ? words : null);
    }

    private static string Clean(string text) => Spaces().Replace(text, " ").Trim();

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value;

    private static TimeSpan? Time(XElement element, string name) =>
        Attribute(element, name) is { } value && TryParseTime(value.Trim(), out var time) ? time : null;

    /// <summary>Clock time (<c>1:02:03.5</c>, <c>02:03.5</c>) or offset time (<c>12.5s</c>, <c>1500ms</c>, <c>2m</c>, <c>1h</c>). Frames and ticks are not guessed at.</summary>
    public static bool TryParseTime(string value, out TimeSpan time)
    {
        time = default;
        if (Clock().Match(value) is { Success: true } clock)
        {
            var hours = clock.Groups[1].Success ? int.Parse(clock.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            var minutes = int.Parse(clock.Groups[2].Value, CultureInfo.InvariantCulture);
            time = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes)
                   + TimeSpan.FromSeconds(double.Parse(clock.Groups[3].Value, CultureInfo.InvariantCulture));
            return true;
        }

        if (Offset().Match(value) is not { Success: true } offset) return false;

        var amount = double.Parse(offset.Groups[1].Value, CultureInfo.InvariantCulture);
        time = offset.Groups[2].Value switch
        {
            "h" => TimeSpan.FromHours(amount),
            "m" => TimeSpan.FromMinutes(amount),
            "ms" => TimeSpan.FromMilliseconds(amount),
            _ => TimeSpan.FromSeconds(amount), // "s", and a bare number (lenient: strictly it needs a unit)
        };
        return true;
    }
}
