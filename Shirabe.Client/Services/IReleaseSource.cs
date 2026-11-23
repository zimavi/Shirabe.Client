namespace Shirabe.Client.Services;

/// <summary>Where the app asks which version is the newest release. The desktop head supplies one that uses Velopack.</summary>
public interface IReleaseSource
{
    /// <summary>The newest released version such as "0.2.0", or null when there is none. Throws when it cannot be reached.</summary>
    Task<string?> GetLatestVersionAsync(CancellationToken cancellationToken = default);
}