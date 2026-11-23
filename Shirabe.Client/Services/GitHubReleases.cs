using System.Net;
using System.Text.Json;

namespace Shirabe.Client.Services;

/// <summary>
/// The newest stable release of Shirabe on GitHub. Used for copies Velopack did not install (a zip, a package manager,
/// a build from source), which Velopack cannot check. Sends nothing but the request itself.
/// </summary>
public sealed class GitHubReleases(HttpClient? http = null) : IReleaseSource
{
    public const string RepositoryUrl = "https://github.com/zimavi/Shirabe.Client";

    /// <summary>Where a person goes to download the newest release.</summary>
    public const string LatestReleasePage = RepositoryUrl + "/releases/latest";

    private const string LatestReleaseApi = "https://api.github.com/repos/zimavi/Shirabe.Client/releases/latest";

    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly HttpClient _http = http ?? Shared;

    public async Task<string?> GetLatestVersionAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.ParseAdd("Shirabe"); // GitHub rejects requests without one
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null; // no release has been published yet
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return json.RootElement.TryGetProperty("tag_name", out var tag) ? tag.GetString()?.TrimStart('v') : null;
    }
}