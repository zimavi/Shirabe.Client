using Shirabe.Core.Library;

namespace Shirabe.Client.Studio;

/// <summary>Narrowing a list of songs by what a person types, shared by the song pickers.</summary>
internal static class SongFilter
{
    /// <summary>A long library would make the list slow to build for no benefit: typing narrows it.</summary>
    public const int MaxListed = 300;

    /// <summary>Songs where every word typed is found in the title, artist or album, in any case.</summary>
    public static IReadOnlyList<TrackSummary> Filter(IReadOnlyList<TrackSummary> all, string filter)
    {
        var terms = filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return [.. all
            .Where(t => terms.All(term => $"{t.Title} {t.Artist} {t.Album}".Contains(term, StringComparison.CurrentCultureIgnoreCase)))
            .Take(MaxListed)];
    }
}

/// <summary>The one-line verdict on a document, the same wherever Studio shows it.</summary>
internal static class StandardStatus
{
    public static string Describe(int errors, int warnings) =>
        (errors, warnings) switch
        {
            (0, 0) => "Follows the standard",
            (0, var w) => $"Follows the standard, {Count(w, "warning")}",
            (var e, 0) => $"Does not follow the standard: {Count(e, "error")}",
            (var e, var w) => $"Does not follow the standard: {Count(e, "error")}, {Count(w, "warning")}",
        };

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
