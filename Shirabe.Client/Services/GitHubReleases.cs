using System.Net;
using System.Text.Json;

namespace Shirabe.Client.Services;

/// <summary>
/// The newest release of Shirabe on GitHub in the chosen <see cref="UpdateChannel"/>. Used for copies Velopack did not install (a zip, a package manager,
/// a build from source), which Velopack cannot check. Sends nothing but the request itself.
/// </summary>
public sealed class GitHubReleases(UpdateChannel channel = UpdateChannel.Stable, HttpClient? http = null)
    : IReleaseSource
{
    public const string RepositoryUrl = "https://github.com/zimavi/Shirabe.Client";

    private const string ReleasesApi = "https://api.github.com/repos/zimavi/Shirabe.Client/releases";

    /// <summary>Where a person goes to download the release of <paramref name="version"/>.</summary>
    public static string ReleasePage(string version) =>
        $"{RepositoryUrl}/releases/tag/v{Uri.EscapeDataString(version)}";

    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly HttpClient _http = http ?? Shared;

    public async Task<string?> GetLatestVersionAsync(CancellationToken cancellationToken = default)
    {
        // "latest" never names a pre-release, so the beta channel reads the list of the newest releases.
        var beta = channel == UpdateChannel.Beta;
        using var request =
            new HttpRequestMessage(HttpMethod.Get, ReleasesApi + (beta ? "?per_page=10" : "/latest"));
        request.Headers.UserAgent.ParseAdd("Shirabe"); // GitHub rejects requests without one
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null; // no release has been published yet
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!beta) return Version(json.RootElement);

        // The list is in order of creation, which need not be the order of the versions.
        string? newest = null;
        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (Version(release) is { } version && UpdateNotifier.IsNewer(version, newest ?? "0.0.0"))
                newest = version;
        }

        return newest;
    }

    private static string? Version(JsonElement release) =>
        release.TryGetProperty("tag_name", out var tag) ? tag.GetString()?.TrimStart('v') : null;
}