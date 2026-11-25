using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shirabe.Client.Services;

/// <summary>A signed-in Last.fm account. The key does not expire (the user can revoke it on the website).</summary>
public sealed record LastFmSession(string User, string Key);

/// <summary>Last.fm refused a request, or answered with something unusable (<see cref="Code"/> 0).</summary>
public sealed class LastFmException(int code, string message) : Exception(message)
{
    /// <summary>In <see cref="LastFmClient.GetSessionAsync"/>: the user has not allowed access in the browser yet.</summary>
    public const int TokenNotAuthorized = 14;

    /// <summary>Last.fm's error number, see https://www.last.fm/api/errorcodes.</summary>
    public int Code { get; } = code;
}

/// <summary>
/// The Last.fm web API (https://www.last.fm/api). Signing in is the "desktop" flow: get a token, let the user allow
/// it on <see cref="AuthorizationPage"/> in their browser, then trade it for a session. The password is never seen.
/// Network failures surface as <see cref="HttpRequestException"/> or <see cref="TaskCanceledException"/>.
/// </summary>
public sealed class LastFmClient(string apiKey, string secret, HttpClient? http = null)
{
    private const string Endpoint = "https://ws.audioscrobbler.com/2.0/";

    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly HttpClient _http = http ?? Shared;

    /// <summary>
    /// A client with the key and secret the build put into the assembly (see Shirabe.Client.csproj), or null for a build
    /// made without them.
    /// </summary>
    public static LastFmClient? FromBuild(Assembly? assembly = null)
    {
        var metadata = (assembly ?? typeof(LastFmClient).Assembly).GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(a => a.Key, a => a.Value);
        return metadata.GetValueOrDefault("LastFmApiKey") is { Length: > 0 } key
               && metadata.GetValueOrDefault("LastFmApiSecret") is { Length: > 0 } buildSecret
            ? new LastFmClient(key, buildSecret)
            : null;
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        var response = await CallAsync("auth.getToken", [], cancellationToken).ConfigureAwait(false);
        return Text(response, "token");
    }

    /// <summary>The page where the user allows <paramref name="token"/>, which is valid for an hour.</summary>
    public string AuthorizationPage(string token) =>
        $"https://www.last.fm/api/auth/?api_key={Uri.EscapeDataString(apiKey)}&token={Uri.EscapeDataString(token)}";

    public async Task<LastFmSession> GetSessionAsync(string token, CancellationToken cancellationToken = default)
    {
        var response = await CallAsync("auth.getSession", new() { ["token"] = token }, cancellationToken)
            .ConfigureAwait(false);
        var session = Member(response, "session");
        return new LastFmSession(Text(session, "name"), Text(session, "key"));
    }

    private async Task<JsonElement> CallAsync(string method, Dictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        parameters["method"] = method;
        parameters["api_key"] = apiKey;
        parameters["api_sig"] = Sign(parameters, secret);
        parameters["format"] = "json"; // not part of the signature

        // Everything is posted, so tokens and session keys stay out of addresses.
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new FormUrlEncodedContent(parameters),
        };
        request.Headers.UserAgent.ParseAdd("Shirabe");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        JsonElement root;
        try
        {
            using var json = JsonDocument.Parse(body);
            root = json.RootElement.Clone();
        }
        catch (JsonException)
        {
            response.EnsureSuccessStatusCode(); // an error page from a server or proxy: a network failure
            throw new LastFmException(0, "Last.fm's answer is not JSON.");
        }

        // Errors come with various HTTP statuses, 200 included: the body decides.
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error)
                                                   && error.TryGetInt32(out var code))
            throw new LastFmException(code,
                root.TryGetProperty("message", out var message) ? message.ToString() : $"Last.fm error {code}.");

        response.EnsureSuccessStatusCode();
        return root;
    }

    /// <summary>MD5 of every parameter as name then value, ordered by name, followed by the secret.</summary>
    private static string Sign(Dictionary<string, string> parameters, string secret)
    {
        var text = string.Concat(parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + p.Value));
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(text + secret)));
    }

    private static JsonElement Member(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var member)
            ? member
            : throw new LastFmException(0, $"Last.fm's answer has no \"{name}\".");

    private static string Text(JsonElement parent, string name) =>
        Member(parent, name) is { ValueKind: JsonValueKind.String } value && value.GetString() is { Length: > 0 } text
            ? text
            : throw new LastFmException(0, $"Last.fm's answer has no \"{name}\".");
}