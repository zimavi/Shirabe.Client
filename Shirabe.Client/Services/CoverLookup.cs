using System.Text.Json;

namespace Shirabe.Client.Services;

/// <summary>
/// Finds a public cover image URL for an album in the iTunes Search API. Discord can only show images from public
/// URLs, so the embedded cover cannot be used. Only the artist and album text is sent, never any file.
/// </summary>
public sealed class CoverLookup
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly Dictionary<string, string?> _cache = new();

    /// <summary>The cached answer, or false if <see cref="FindAsync"/> has not finished for this album yet.</summary>
    public bool TryGetCached(string artist, string album, out string? url) => _cache.TryGetValue(Key(artist, album), out url);

    /// <summary>A cover URL, or null when nothing matches. A network failure is not cached, so it is tried again later.</summary>
    public async Task<string?> FindAsync(string artist, string album, CancellationToken cancellationToken = default)
    {
        var key = Key(artist, album);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        try
        {
            var uri = $"https://itunes.apple.com/search?entity=album&limit=5&term={Uri.EscapeDataString($"{artist} {album}")}";
            await using var stream = await Http.GetStreamAsync(uri, cancellationToken).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return _cache[key] = PickCover(json.RootElement, artist, album);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>First result whose album and artist both match, with the image enlarged to 512 px.</summary>
    public static string? PickCover(JsonElement response, string artist, string album)
    {
        if (!response.TryGetProperty("results", out var results)) return null;

        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("collectionName", out var name) || !result.TryGetProperty("artistName", out var by)
                || !result.TryGetProperty("artworkUrl100", out var art)) continue;

            if (Related(name.GetString(), album) && Related(by.GetString(), artist) && art.GetString() is { } url)
                return url.Replace("100x100bb", "512x512bb");
        }

        return null;
    }

    // Tags and the catalogue spell things differently ("Album - Single", "A and B"): accept one containing the other.
    private static bool Related(string? a, string b)
    {
        var x = Normalize(a);
        var y = Normalize(b);
        return x.Length > 0 && y.Length > 0 && (x.Contains(y) || y.Contains(x));
    }

    private static string Normalize(string? text) => new(text?.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string Key(string artist, string album) => $"{artist}\n{album}".ToLowerInvariant();
}
