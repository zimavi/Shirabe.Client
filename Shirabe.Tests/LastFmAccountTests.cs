using System.Net;

using Shirabe.Client.Services;

using Xunit;

namespace Shirabe.Tests;

public class LastFmAccountTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Token = """{"token":"tok"}""";
    private const string Granted = """{"session":{"name":"listener","key":"sk"}}""";
    private const string NotYet = """{"error":14,"message":"not authorized"}""";
    private const string Saved = """{"User":"listener","Key":"sk"}""";

    private static readonly LastFmSession Listener = new("listener", "sk");

    /// <summary>Gives the answers in order, then repeats the last one. A null answer is a network failure.</summary>
    private sealed class FakeWeb(params string?[] answers) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var answer = answers[Math.Min(Requests++, answers.Length - 1)];
            return answer is null
                ? throw new HttpRequestException("offline")
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer) });
        }
    }

    private sealed class FakeStore : ISecretStore
    {
        public string? Secret { get; set; }
        public bool Broken { get; set; }
        public ManualResetEventSlim? HoldWrite { get; init; }

        public string? Read(string name) => Check(name, Secret);

        public void Write(string name, string secret)
        {
            HoldWrite?.Wait(TimeSpan.FromSeconds(10));
            Secret = Check(name, secret);
        }

        public void Delete(string name) => Secret = Check<string?>(name, null);

        private T Check<T>(string name, T value)
        {
            Assert.Equal("lastfm", name);
            return Broken ? throw new SecretStoreException("broken") : value;
        }
    }

    private static LastFmAccount Account(ISecretStore? store, FakeWeb? web = null) =>
        new(new LastFmClient("KEY", "SECRET", new HttpClient(web ?? new FakeWeb(Token, Granted))), store,
            TimeSpan.Zero);

    [Fact]
    public async Task A_saved_session_comes_back()
    {
        var account = Account(new FakeStore { Secret = Saved });

        await account.LoadAsync();

        Assert.Equal(Listener, account.Session);
        Assert.False(account.CannotSaveSession);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"User":"listener","Key":""}""")]
    public async Task Nothing_or_something_damaged_leaves_the_user_signed_out(string? secret)
    {
        var account = Account(new FakeStore { Secret = secret });

        await account.LoadAsync();

        Assert.Null(account.Session);
        Assert.False(account.CannotSaveSession);
    }

    [Fact]
    public async Task Storage_that_cannot_be_read_is_reported_before_signing_in()
    {
        var account = Account(new FakeStore { Broken = true });

        await account.LoadAsync();

        Assert.Null(account.Session);
        Assert.True(account.CannotSaveSession);
    }

    [Fact]
    public async Task Signing_in_waits_for_permission_and_saves_the_session()
    {
        var web = new FakeWeb(Token, NotYet, null, """{"error":16,"message":"try again"}""", Granted);
        var store = new FakeStore();
        var account = Account(store, web);
        var pages = new List<string>();

        await account.SignInAsync(pages.Add, Ct);

        Assert.Equal("https://www.last.fm/api/auth/?api_key=KEY&token=tok", Assert.Single(pages));
        Assert.Equal(5, web.Requests);
        Assert.Equal(Listener, account.Session);
        Assert.False(account.CannotSaveSession);

        var next = Account(store); // the next start of the app
        await next.LoadAsync();
        Assert.Equal(Listener, next.Session);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Without_working_storage_the_session_is_kept_in_memory_and_that_is_reported(bool hasStore)
    {
        var account = Account(hasStore ? new FakeStore { Broken = true } : null);

        await account.SignInAsync(_ => { }, Ct);

        Assert.Equal(Listener, account.Session);
        Assert.True(account.CannotSaveSession);
        Assert.True(await account.SignOutAsync()); // nothing was saved, so nothing can come back
        Assert.Null(account.Session);
    }

    [Fact]
    public async Task A_refused_request_ends_the_sign_in()
    {
        var web = new FakeWeb(Token, """{"error":15,"message":"expired"}""");
        var account = Account(new FakeStore(), web);

        var error = await Assert.ThrowsAsync<LastFmException>(() => account.SignInAsync(_ => { }, Ct));

        Assert.Equal(15, error.Code);
        Assert.Equal(2, web.Requests);
        Assert.Null(account.Session);
    }

    [Fact]
    public async Task Waiting_for_permission_ends_after_a_while()
    {
        var web = new FakeWeb(Token, NotYet);

        var error = await Assert.ThrowsAsync<LastFmException>(() =>
            Account(new FakeStore(), web).SignInAsync(_ => { }, Ct));

        Assert.Equal(LastFmException.TokenNotAuthorized, error.Code);
        Assert.Equal(LastFmAccount.MaxSignInAttempts + 1, web.Requests);
    }

    [Fact]
    public async Task Waiting_for_permission_can_be_cancelled()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var store = new FakeStore();
        var account = Account(store, new FakeWeb(Token, NotYet));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            account.SignInAsync(_ => cancel.Cancel(), cancel.Token));

        Assert.Null(account.Session);
        Assert.Null(store.Secret);
    }

    [Fact]
    public async Task Signing_out_removes_the_saved_session()
    {
        var store = new FakeStore { Secret = Saved };
        var account = Account(store);
        await account.LoadAsync();

        Assert.True(await account.SignOutAsync());

        Assert.Null(account.Session);
        Assert.Null(store.Secret);
    }

    [Fact]
    public async Task A_saved_session_that_cannot_be_removed_is_reported()
    {
        var store = new FakeStore { Secret = Saved };
        var account = Account(store);
        await account.LoadAsync();
        store.Broken = true;

        Assert.False(await account.SignOutAsync());

        Assert.Null(account.Session);
    }

    [Fact]
    public async Task Signing_out_during_a_slow_save_still_removes_the_session()
    {
        using var hold = new ManualResetEventSlim();
        var store = new FakeStore { HoldWrite = hold };
        var account = Account(store);

        var signingIn = account.SignInAsync(_ => { }, Ct);
        while (account.Session is null) await Task.Delay(1, Ct);
        var signingOut = account.SignOutAsync();
        hold.Set();
        await signingIn;
        await signingOut;

        Assert.Null(account.Session);
        Assert.Null(store.Secret);
    }
}