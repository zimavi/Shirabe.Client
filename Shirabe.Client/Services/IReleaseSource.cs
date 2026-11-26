namespace Shirabe.Client.Services;

/// <summary>Which releases are offered. Beta takes pre-releases and stable releases, whichever is newest.</summary>
public enum UpdateChannel
{
    Stable,
    Beta,
}

/// <summary>Where the app asks which version is the newest release. The head supplies one.</summary>
public interface IReleaseSource
{
    /// <summary>The newest released version such as "0.2.0", or null when there is none. Throws when it cannot be reached.</summary>
    Task<string?> GetLatestVersionAsync(CancellationToken cancellationToken = default);
}

/// <summary>A release source for a copy of the app that can also install the release it found.</summary>
public interface ISelfUpdater : IReleaseSource
{
    /// <summary>
    /// Downloads the release that <see cref="IReleaseSource.GetLatestVersionAsync"/> found, reporting 0 to 100. Throws
    /// when it cannot.
    /// </summary>
    Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken = default);

    /// <summary>Closes the app, installs the downloaded release and starts the app again. Throws when it cannot.</summary>
    void RestartAndInstall();
}