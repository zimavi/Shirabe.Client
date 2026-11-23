using Shirabe.Client.Localization;
using Shirabe.Client.ViewModels;

namespace Shirabe.Client.Services;

/// <summary>
/// Asks an <see cref="IReleaseSource"/> for the newest version and, when it is newer than the running one, shows a
/// notification pointing to the release page. It never downloads or installs anything: the person downloads the
/// release, or updates through the package manager they installed Shirabe with (the app cannot tell which that is).
/// </summary>
public sealed class UpdateNotifier(IReleaseSource source, NotificationCenter notifications, AppVersion current)
{
    // shortcut: asked again, and notified again, on every start until the person updates; remember the last version told if it nags
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        string? latest;
        try
        {
            latest = await source.GetLatestVersionAsync(cancellationToken);
        }
        catch (Exception)
        {
            return; // offline, rate limited, no feed...: not worth bothering anyone, and never a reason to crash the app
        }

        if (latest is null || !IsNewer(latest, current.Version)) return;

        notifications.Show(Loc.Get("Update_Title", latest), Loc.Get("Update_Message"),
            Loc.Get("Update_ViewRelease"), GitHubReleases.LatestReleasePage);
    }

    /// <summary>
    /// Whether <paramref name="latest"/> is a later version than <paramref name="running"/> ("1.2.3", "1.2.3-beta.1"). A version that
    /// cannot be read is never newer. A release beats its own pre-releases.
    /// </summary>
    public static bool IsNewer(string latest, string running)
    {
        if (!TryParse(latest, out var newVersion, out var newPre) ||
            !TryParse(running, out var oldVersion, out var oldPre)) return false;

        var byNumbers = newVersion.CompareTo(oldVersion);
        if (byNumbers != 0) return byNumbers > 0;
        if (newPre.Length == 0) return oldPre.Length > 0;
        // shortcut: pre-release names compare as text, so "beta.10" sorts before "beta.9"; upgrade if numbered pre-releases are used
        return oldPre.Length > 0 && string.CompareOrdinal(newPre, oldPre) > 0;
    }

    private static bool TryParse(string text, out Version version, out string preRelease)
    {
        text = text.Split('+')[0]; // build metadata such as the commit does not count
        var dash = text.IndexOf('-');
        preRelease = dash < 0 ? "" : text[(dash + 1)..];
        return Version.TryParse(dash < 0 ? text : text[..dash], out version!);
    }
}