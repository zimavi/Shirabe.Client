using System.Threading;
using System.Threading.Tasks;

using Shirabe.Client.Services;

using Velopack;
using Velopack.Sources;

namespace Shirabe.Client.Desktop;

/// <summary>
/// Asks Velopack for the newest release when this copy was installed by a Velopack installer. Velopack cannot check any other
/// copy (a zip, a package manager, a build from source), so those ask GitHub's release list instead.
/// </summary>
sealed class VelopackUpdateSource(IReleaseSource gitHub) : IReleaseSource
{
    public async Task<string?> GetLatestVersionAsync(CancellationToken cancellationToken = default)
    {
        // Created per call: needs VelopackApp.Build().Run() to have run, which Program.Main does first.
        var manager = new UpdateManager(new GithubSource(GitHubReleases.RepositoryUrl, null, prerelease: true));
        if (!manager.IsInstalled) return await gitHub.GetLatestVersionAsync(cancellationToken);

        // shortcut: no cancellation token (Velopack takes none); a hung request is only bounded by its own timeouts
        var update = await manager.CheckForUpdatesAsync();
        return update?.TargetFullRelease.Version.ToString();
    }
}