using System.Net;

using Shirabe.Client.Services;

using Xunit;

namespace Shirabe.Tests;

public class LastFmClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeWeb(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string Address { get; private set; } = "";
        public string Sent { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Address = request.RequestUri!.ToString();
            Sent = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private static LastFmClient Client(FakeWeb web) => new("KEY", "SECRET", new HttpClient(web));

    [Fact]
    public async Task A_token_is_asked_for_with_a_signed_post()
    {
        var web = new FakeWeb(HttpStatusCode.OK, """{"token":"abc"}""");

        Assert.Equal("abc", await Client(web).GetTokenAsync(Ct));

        Assert.Equal(HttpMethod.Post, web.Method);
        Assert.Equal("https://ws.audioscrobbler.com/2.0/", web.Address);
        // md5("api_keyKEYmethodauth.getTokenSECRET"): sorted by name, "format" left out
        Assert.Equal("method=auth.getToken&api_key=KEY&api_sig=66ee63a18da3c919f987b342697d913c&format=json", web.Sent);
    }

    [Fact]
    public async Task A_token_becomes_a_session_and_the_signature_covers_the_unescaped_utf8_text()
    {
        var web = new FakeWeb(HttpStatusCode.OK, """{"session":{"name":"listener","key":"sk","subscriber":0}}""");

        var session = await Client(web).GetSessionAsync("ТОКЕН&+", Ct);

        Assert.Equal(new LastFmSession("listener", "sk"), session);
        Assert.Contains("api_sig=df19c3622a204d2dacd179c35bfa0571", web.Sent);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task An_error_answer_carries_the_code_whatever_the_status(HttpStatusCode status)
    {
        var web = new FakeWeb(status, """{"error":14,"message":"This token has not been authorized"}""");

        var error = await Assert.ThrowsAsync<LastFmException>(() => Client(web).GetSessionAsync("abc", Ct));

        Assert.Equal(LastFmException.TokenNotAuthorized, error.Code);
        Assert.Equal("This token has not been authorized", error.Message);
    }

    [Theory]
    [InlineData("<html>oops</html>")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"token":5}""")]
    public async Task An_unusable_answer_is_a_LastFm_error(string body)
    {
        var error = await Assert.ThrowsAsync<LastFmException>(() =>
            Client(new FakeWeb(HttpStatusCode.OK, body)).GetTokenAsync(Ct));

        Assert.Equal(0, error.Code);
    }

    [Theory]
    [InlineData("<html>Bad gateway</html>")]
    [InlineData("""{"token":"abc"}""")]
    public async Task A_failed_status_without_an_error_code_is_a_network_failure(string body) =>
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Client(new FakeWeb(HttpStatusCode.BadGateway, body)).GetTokenAsync(Ct));

    [Fact]
    public void An_assembly_built_without_a_key_gives_no_client() =>
        Assert.Null(LastFmClient.FromBuild(typeof(LastFmClientTests).Assembly));

    [Fact]
    public void The_authorization_page_names_the_key_and_the_token() =>
        Assert.Equal("https://www.last.fm/api/auth/?api_key=KEY&token=a%26b",
            Client(new FakeWeb(HttpStatusCode.OK, "")).AuthorizationPage("a&b"));
}