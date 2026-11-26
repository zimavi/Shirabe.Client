using CommunityToolkit.Mvvm.Input;

using Shirabe.Client.Localization;
using Shirabe.Client.ViewModels;

namespace Shirabe.Client.Services;

/// <summary>
/// Asks an <see cref="IReleaseSource"/> for the newest version and, when it is newer than the running one, shows a
/// notification pointing to the release page. With an <see cref="ISelfUpdater"/> the notification also has a button that
/// downloads the release and then restarts into it; nothing is downloaded before the person presses it. Otherwise the
/// person downloads the release, or updates through the package manager they installed Shirabe with.
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

        var updater = source as ISelfUpdater;
        var notification = notifications.Show(Loc.Get("Update_Title", latest),
            Loc.Get(updater is null ? "Update_Message" : "Update_SelfMessage"),
            Loc.Get("Update_ViewRelease"), GitHubReleases.ReleasePage(latest));
        if (updater is not null) OfferInstall(notification, updater);
    }

    private static void OfferInstall(NotificationViewModel notification, ISelfUpdater updater)
    {
        var downloaded = false;

        // Runs on the UI thread. The command disables its button until it is done, so it cannot run twice at once.
        async Task InstallAsync()
        {
            try
            {
                if (downloaded)
                {
                    updater.RestartAndInstall();
                    return;
                }

                var downloading = true;

                void Report(int percent)
                {
                    // a report can arrive after the download has ended, and must not replace what is said then
                    if (downloading) notification.Message = Loc.Get("Update_Downloading", percent);
                }

                Report(0);
                try
                {
                    await updater.DownloadAsync(new Progress<int>(Report));
                }
                finally
                {
                    downloading = false;
                }

                downloaded = true;
                notification.Message = Loc.Get("Update_Ready");
                notification.ActionLabel = Loc.Get("Update_Restart");
            }
            catch (Exception) // no network, disk full, permission refused...: say so and let the person try again
            {
                notification.Message = Loc.Get("Update_Failed");
                if (!downloaded) notification.ActionLabel = Loc.Get("Update_TryAgain");
            }
        }

        notification.ActionLabel = Loc.Get("Update_Install");
        notification.Action = new AsyncRelayCommand(InstallAsync);
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
        return oldPre.Length > 0 && ComparePreReleases(newPre, oldPre) > 0;
    }

    // SemVer: part by part between the dots, numbers as numbers ("beta.10" is after "beta.9") and before words.
    private static int ComparePreReleases(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aIsNumber = ulong.TryParse(a[i], out var aNumber);
            var bIsNumber = ulong.TryParse(b[i], out var bNumber);
            var order = aIsNumber && bIsNumber ? aNumber.CompareTo(bNumber)
                : aIsNumber != bIsNumber ? (aIsNumber ? -1 : 1)
                : string.CompareOrdinal(a[i], b[i]);
            if (order != 0) return order;
        }

        return a.Length.CompareTo(b.Length);
    }

    private static bool TryParse(string text, out Version version, out string preRelease)
    {
        text = text.Split('+')[0]; // build metadata such as the commit does not count
        var dash = text.IndexOf('-');
        preRelease = dash < 0 ? "" : text[(dash + 1)..];
        return Version.TryParse(dash < 0 ? text : text[..dash], out version!);
    }
}