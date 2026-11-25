using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;

using Xunit;

using static Shirabe.Tests.LastFmAccountTests;

namespace Shirabe.Tests;

public class LastFmViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void A_build_without_a_key_says_so_and_offers_nothing()
    {
        var page = new LastFmViewModel();

        Assert.Contains("without a Last.fm API key", page.Status);
        Assert.False(page.ShowConnect);
        Assert.False(page.IsSignedIn);
        Assert.False(page.ShowStorageWarning);
    }

    [Fact]
    public async Task Connecting_and_disconnecting_changes_what_is_offered()
    {
        var store = new FakeStore();
        var opened = new List<string>();
        var page = new LastFmViewModel(Account(store), opened.Add);
        var changed = new List<string?>();
        page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.True(page.ShowConnect);

        await page.ConnectCommand.ExecuteAsync(null);

        Assert.Single(opened);
        Assert.True(page.IsSignedIn);
        Assert.False(page.ShowConnect);
        Assert.False(page.IsWaiting);
        Assert.Equal("Signed in as listener", page.Status);
        Assert.Equal("https://www.last.fm/user/listener", page.ProfileUrl);
        Assert.False(page.ShowStorageWarning);
        Assert.Contains(nameof(LastFmViewModel.Status), changed);

        await page.DisconnectCommand.ExecuteAsync(null);

        Assert.True(page.ShowConnect);
        Assert.Null(page.ProfileUrl);
        Assert.Null(page.Error);
        Assert.Null(store.Secret);
    }

    [Fact]
    public void Missing_secure_storage_is_shown_before_signing_in() =>
        Assert.True(new LastFmViewModel(Account(null)).ShowStorageWarning);

    [Fact]
    public async Task A_failed_save_shows_the_storage_warning_after_signing_in()
    {
        var page = new LastFmViewModel(Account(new FakeStore { Broken = true }));
        Assert.False(page.ShowStorageWarning);

        await page.ConnectCommand.ExecuteAsync(null);

        Assert.True(page.IsSignedIn);
        Assert.True(page.ShowStorageWarning);
    }

    [Theory]
    [InlineData("""{"error":14,"message":"no"}""", "not allowed in time")]
    [InlineData("""{"error":10,"message":"Invalid API key"}""", "failed: Invalid API key")]
    [InlineData(null, "could not be reached")]
    public async Task A_failed_sign_in_says_why_and_can_be_tried_again(string? answer, string expected)
    {
        var page = new LastFmViewModel(Account(new FakeStore(), new FakeWeb(answer)));

        await page.ConnectCommand.ExecuteAsync(null);

        Assert.Contains(expected, page.Error);
        Assert.True(page.ShowConnect);
        Assert.False(page.IsWaiting);
    }

    [Fact]
    public async Task Cancelling_stops_the_wait_without_an_error()
    {
        // a long interval: we are still waiting for permission when we cancel
        var client = new LastFmClient("KEY", "SECRET", new HttpClient(new FakeWeb(Token, NotYet)));
        var page = new LastFmViewModel(new LastFmAccount(client, new FakeStore(), TimeSpan.FromMinutes(1)));

        var connecting = page.ConnectCommand.ExecuteAsync(null);
        Assert.True(page.IsWaiting);
        Assert.False(page.ShowConnect);
        Assert.Contains("Allow access", page.Status);

        page.ConnectCancelCommand.Execute(null);
        await connecting.WaitAsync(Ct);

        Assert.False(page.IsWaiting);
        Assert.Null(page.Error);
        Assert.True(page.ShowConnect);
    }

    [Fact]
    public async Task A_saved_sign_in_that_cannot_be_removed_is_reported()
    {
        var store = new FakeStore { Secret = Saved };
        var account = Account(store);
        var page = new LastFmViewModel(account);
        await account.LoadAsync();
        store.Broken = true;

        await page.DisconnectCommand.ExecuteAsync(null);

        Assert.Contains("could not be removed", page.Error);
        Assert.False(page.IsSignedIn);
    }
}