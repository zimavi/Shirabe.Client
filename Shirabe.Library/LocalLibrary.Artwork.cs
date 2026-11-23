using System.Security.Cryptography;
using System.Text;
using Shirabe.Core.Library;

namespace Shirabe.Library;

// Pictures that metadata documents link to, downloaded into the artwork cache (standard, section 7).
public sealed partial class LocalLibrary : IArtworkLinks
{
    /// <summary>The standard allows a client its own, lower limit than 10 MiB.</summary>
    private const int MaxPictureSize = 10 * 1024 * 1024;

    // At most 3 redirects, and .NET never follows one from https to http. A response larger than the limit throws.
    private static HttpClient CreateHttp(HttpMessageHandler? handler) => new(handler ?? new HttpClientHandler { MaxAutomaticRedirections = 3 })
    {
        Timeout = TimeSpan.FromSeconds(15),
        MaxResponseContentBufferSize = MaxPictureSize,
    };

    /// <summary>The name a linked picture has in the cache: the hash its document promises, or else a hash of its address.</summary>
    private static string CoverKey(string url, string? sha256) =>
        sha256 ?? "url-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();

    public async Task<string?> FetchAsync(string url, string? sha256, CancellationToken cancellationToken = default)
    {
        var key = CoverKey(url, sha256);
        if (_artwork.Find(key) is { } cached) return _artwork.PathOf(cached);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0) return null;

        try
        {
            var bytes = await _http.GetByteArrayAsync(uri, cancellationToken).ConfigureAwait(false);

            // A picture that is not the one the document promised is thrown away (section 7.1).
            if (sha256 is not null && Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != sha256) return null;

            return _artwork.Save(bytes, key) is { } saved ? _artwork.PathOf(saved) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            // shortcut: a failed download is not remembered, so a dead link is tried again each time the song is shown; add a retry delay if that ever annoys a host
            return null;
        }
    }
}
