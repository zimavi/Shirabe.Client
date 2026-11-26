using System.Net;

using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;

using Xunit;

namespace Shirabe.Tests;

public class UpdateNotifierTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeSource(Func<string?> answer) : IReleaseSource
    {
        public Task<string?> GetLatestVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(answer());
    }

    private static NotificationCenter Check(Func<string?> answer, string running = "0.1.0")
    {
        var center = new NotificationCenter();
        new UpdateNotifier(new FakeSource(answer), center, new AppVersion(running, null)).CheckAsync(Ct).GetAwaiter()
            .GetResult();
        return center;
    }

    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("0.1.1", "0.1.0", true)]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("0.10.0", "0.9.0", true)] // numbers, not text
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.1.0", "0.2.0", false)]
    [InlineData("0.2.0", "0.2.0-beta.1", true)] // the release beats its pre-release
    [InlineData("0.2.0-beta.1", "0.2.0", false)]
    [InlineData("0.2.0-beta.2", "0.2.0-beta.1", true)]
    [InlineData("0.2.0-beta.1", "0.1.0", true)]
    [InlineData("0.2.0-beta.10", "0.2.0-beta.9", true)] // numbers inside a pre-release too
    [InlineData("0.2.0-beta.9", "0.2.0-beta.10", false)]
    [InlineData("0.2.0-rc.1", "0.2.0-beta.3", true)]
    [InlineData("0.2.0-beta.1", "0.2.0-beta", true)]
    [InlineData("0.2.0", "0.1.0+ce863a5e7", true)] // the commit is ignored
    [InlineData("0.1.0+abc", "0.1.0", false)]
    [InlineData("not a version", "0.1.0", false)]
    [InlineData("0.2.0", "unknown", false)]
    public void It_compares_versions(string latest, string running, bool newer) =>
        Assert.Equal(newer, UpdateNotifier.IsNewer(latest, running));

    [Fact]
    public void A_newer_release_shows_one_notification_with_the_release_page()
    {
        var center = Check(() => "0.2.0");

        var notification = Assert.Single(center.Items);
        Assert.Contains("0.2.0", notification.Title);
        Assert.Equal(GitHubReleases.RepositoryUrl + "/releases/tag/v0.2.0", notification.Link);
        Assert.True(notification.HasLink);
        Assert.False(string.IsNullOrWhiteSpace(notification.LinkLabel));
    }

    [Theory]
    [InlineData("0.1.0")]
    [InlineData(null)]
    public void Nothing_newer_shows_nothing(string? latest) => Assert.Empty(Check(() => latest).Items);

    [Fact]
    public void A_failed_check_shows_nothing_and_does_not_throw() =>
        Assert.Empty(Check(() => throw new HttpRequestException("offline")).Items);

    private sealed class FakeUpdater : ISelfUpdater
    {
        public TaskCompletionSource Download { get; set; } = new();

        public int Downloads { get; private set; }

        public int Restarts { get; private set; }

        public Task<string?> GetLatestVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("0.2.0");

        public Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken = default)
        {
            Downloads++;
            return Download.Task;
        }

        public void RestartAndInstall() => Restarts++;
    }

    private static async Task<NotificationViewModel> Offer(FakeUpdater updater)
    {
        var center = new NotificationCenter();
        await new UpdateNotifier(updater, center, new AppVersion("0.1.0", null)).CheckAsync(Ct);
        return Assert.Single(center.Items);
    }

    [Fact]
    public void A_source_that_cannot_install_offers_no_action() =>
        Assert.False(Assert.Single(Check(() => "0.2.0").Items).HasAction);

    [Fact]
    public async Task An_update_is_downloaded_when_asked_and_installed_when_asked_again()
    {
        var updater = new FakeUpdater();
        var notification = await Offer(updater);
        Assert.True(notification.HasAction);
        Assert.True(notification.HasLink); // the release notes stay reachable
        Assert.Equal(0, updater.Downloads);
        var install = notification.ActionLabel;

        var download = notification.Action!.ExecuteAsync(null);
        Assert.False(notification.Action.CanExecute(null)); // no second download meanwhile
        updater.Download.SetResult();
        await download;

        Assert.Equal(1, updater.Downloads);
        Assert.Equal(0, updater.Restarts);
        Assert.NotEqual(install, notification.ActionLabel);

        await notification.Action.ExecuteAsync(null);

        Assert.Equal(1, updater.Restarts);
        Assert.Equal(1, updater.Downloads);
    }

    [Fact]
    public async Task A_failed_download_can_be_tried_again()
    {
        var updater = new FakeUpdater();
        var notification = await Offer(updater);
        updater.Download.SetException(new HttpRequestException("offline"));

        await notification.Action!.ExecuteAsync(null);
        var afterFailure = notification.Message;

        updater.Download = new TaskCompletionSource();
        updater.Download.SetResult();
        await notification.Action.ExecuteAsync(null);

        Assert.Equal(2, updater.Downloads);
        Assert.Equal(0, updater.Restarts);
        Assert.NotEqual(afterFailure, notification.Message);
    }

    [Fact]
    public void A_notification_goes_away_when_dismissed()
    {
        var center = new NotificationCenter();
        var first = center.Show("One", "first");
        center.Show("Two", "second", "Open", "https://example.com/");

        first.DismissCommand.Execute(null);

        Assert.Equal("Two", Assert.Single(center.Items).Title);
    }

    [Fact]
    public void A_label_without_a_link_gives_no_button()
    {
        var notification = new NotificationCenter().Show("Title", "Text", linkLabel: "Open");

        Assert.False(notification.HasLink);
        Assert.Null(notification.LinkLabel);
    }

    private sealed class FakeWeb(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task GitHub_releases_reads_the_tag_without_its_v()
    {
        var web = new FakeWeb(HttpStatusCode.OK, """{"tag_name":"v0.2.0","html_url":"https://example.com"}""");

        var latest = await new GitHubReleases(http: new HttpClient(web)).GetLatestVersionAsync(Ct);

        Assert.Equal("0.2.0", latest);
        Assert.Equal("https", web.Request!.RequestUri!.Scheme);
        Assert.False(web.Request.Headers.UserAgent.Count == 0); // GitHub refuses requests without a user agent
    }

    [Fact]
    public async Task The_beta_channel_takes_the_highest_version_pre_releases_included()
    {
        var web = new FakeWeb(HttpStatusCode.OK,
            """[{"tag_name":"v0.1.1"},{"tag_name":"v0.2.0-beta.10"},{"tag_name":"nightly"},{"tag_name":"v0.2.0-beta.9"}]""");

        var latest = await new GitHubReleases(UpdateChannel.Beta, new HttpClient(web)).GetLatestVersionAsync(Ct);

        Assert.Equal("0.2.0-beta.10", latest);
        Assert.EndsWith("/releases?per_page=10", web.Request!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task The_beta_channel_says_none_for_an_empty_list() =>
        Assert.Null(await new GitHubReleases(UpdateChannel.Beta,
            new HttpClient(new FakeWeb(HttpStatusCode.OK, "[]"))).GetLatestVersionAsync(Ct));

    [Fact]
    public void The_update_channel_is_stable_by_default_and_saved_by_name()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shirabe-{Guid.NewGuid():N}.json");
        try
        {
            var settings = AppSettings.Load(path);
            Assert.Equal(UpdateChannel.Stable, settings.UpdateChannel);
            var page = new ConnectionsSettingsViewModel(settings, new LastFmViewModel(null, _ => { }, settings));

            page.SelectedUpdateChannel = page.UpdateChannelOptions[1];

            Assert.Contains("\"Beta\"", File.ReadAllText(path));
            Assert.Equal(UpdateChannel.Beta, AppSettings.Load(path).UpdateChannel);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GitHub_releases_says_none_before_the_first_release() =>
        Assert.Null(await new GitHubReleases(http: new HttpClient(new FakeWeb(HttpStatusCode.NotFound, "{}")))
            .GetLatestVersionAsync(Ct));

    [Fact]
    public async Task GitHub_releases_throws_on_other_failures() =>
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new GitHubReleases(http: new HttpClient(new FakeWeb(HttpStatusCode.Forbidden, "{}")))
                .GetLatestVersionAsync(Ct));
}