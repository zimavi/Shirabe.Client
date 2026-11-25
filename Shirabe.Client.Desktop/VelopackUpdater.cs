using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

using Shirabe.Client.Services;

using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace Shirabe.Client.Desktop;

/// <summary>
/// Self-update through Velopack, from the feed of the GitHub releases. Only for copies that may replace themselves, see
/// <see cref="TryCreate"/>; every other copy is only told about new releases (<see cref="GitHubReleases"/>).
/// </summary>
sealed class VelopackUpdater : ISelfUpdater
{
    private readonly UpdateManager _manager;
    private UpdateInfo? _update;

    private VelopackUpdater(UpdateManager manager) => _manager = manager;

    /// <summary>
    /// Null when this copy must not update itself: built with <c>-p:SelfUpdate=false</c>, not installed by Velopack
    /// (.deb, .rpm, .tar.gz, a build from source), or installed where it cannot be replaced.
    /// </summary>
    public static VelopackUpdater? TryCreate()
    {
#if NO_SELF_UPDATE
        return null;
#else
        if (!VelopackLocator.IsCurrentSet) return null; // the visual designer does not run VelopackApp
        var manager = new UpdateManager(new GithubSource(GitHubReleases.RepositoryUrl, null, false));
        return manager.IsInstalled && CanBeReplaced() ? new VelopackUpdater(manager) : null;
#endif
    }

    private static bool CanBeReplaced()
    {
        // macOS: started from the mounted .dmg, or translocated by Gatekeeper; both are read-only.
        // shortcut: this also excludes a copy kept on an external drive (/Volumes), which only gets the notification
        if (OperatingSystem.IsMacOS())
            return !AppContext.BaseDirectory.StartsWith("/Volumes/", StringComparison.Ordinal) &&
                   !AppContext.BaseDirectory.Contains("/AppTranslocation/", StringComparison.Ordinal);

        // Linux: an AppImage in a folder the user cannot write to was put there by a system package (/opt and the like).
        // On Windows such a folder is a per-machine install, which Velopack updates after asking for permission.
        if (OperatingSystem.IsLinux())
            return Path.GetDirectoryName(Environment.GetEnvironmentVariable("APPIMAGE")) is { } folder &&
                   CanWriteTo(folder);

        return true;
    }

    private static bool CanWriteTo(string folder)
    {
        try
        {
            using var _ = new FileStream(Path.Combine(folder, $".shirabe-{Guid.NewGuid():N}"), FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // shortcut: Velopack's check takes no cancellation token
    public async Task<string?> GetLatestVersionAsync(CancellationToken cancellationToken = default)
    {
        _update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return _update?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken = default) =>
        _manager.DownloadUpdatesAsync(
            _update ?? throw new InvalidOperationException("No update has been found."),
            progress.Report, cancellationToken);

    public void RestartAndInstall()
    {
        // The updater waits for this process to end, so the app closes the normal way and saves its session.
        _manager.WaitExitThenApplyUpdates(_update?.TargetFullRelease);
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}