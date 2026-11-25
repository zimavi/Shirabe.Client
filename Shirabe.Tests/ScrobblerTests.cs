using System.Net;

using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Shirabe.Core.Metadata;

using Xunit;

namespace Shirabe.Tests;

public class ScrobblerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeLastFm : HttpMessageHandler
    {
        /// <summary>Every request's form fields.</summary>
        public List<Dictionary<string, string>> Sent { get; } = [];

        public bool Offline { get; set; }

        public string ScrobbleAnswer { get; set; } = """{"scrobbles":{}}""";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Offline) throw new HttpRequestException("offline");

            var form = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&')
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(p => Uri.UnescapeDataString(p[0]),
                    p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
            Sent.Add(form);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(form["method"] switch
                {
                    "auth.getToken" => LastFmAccountTests.Token,
                    "auth.getSession" => LastFmAccountTests.Granted,
                    "track.updateNowPlaying" => """{"nowplaying":{}}""",
                    _ => ScrobbleAnswer,
                }),
            };
        }

        public IEnumerable<string> Methods => Sent.Select(f => f["method"]);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Tags(params string[] names) : IFileTags
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyList<FileTag>> ReadFileTagsAsync(Guid recordingId,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<FileTag>>(
                [.. names.Select(name => new FileTag(FileTag.TagsGroup, name, "x"))]);
        }
    }

    /// <summary>A signed-in scrobbler over the fake player, whose songs are 120 s long.</summary>
    private sealed class Rig
    {
        public FakeAudioPlayer Engine { get; } = new();
        public FakeLastFm Web { get; } = new();
        public Clock Clock { get; } = new();
        public AppSettings Settings { get; } = new();
        public PlayerViewModel Player { get; }
        public LastFmAccount Account { get; }
        public Scrobbler Scrobbler { get; }
        public List<ScrobbleReport> Reports { get; } = [];

        public Rig(IFileTags? tags = null)
        {
            Player = new PlayerViewModel(Engine, Settings);
            var client = new LastFmClient("KEY", "SECRET", new HttpClient(Web));
            Account = new LastFmAccount(client, null, TimeSpan.Zero);
            Scrobbler = new Scrobbler(Player, Settings, Account, client, tags, Clock);
            Scrobbler.Reported += (_, report) => Reports.Add(report);
        }

        public async Task SignInAsync()
        {
            await Account.SignInAsync(_ => { }, Ct);
            Web.Sent.Clear();
        }

        /// <summary>Lets the song play on, second by second, to this position.</summary>
        public async Task PlayToAsync(int second)
        {
            while (Engine.Position < TimeSpan.FromSeconds(second))
            {
                Engine.Position += TimeSpan.FromSeconds(1);
                Clock.Now += TimeSpan.FromSeconds(1);
                await Scrobbler.TickAsync();
            }
        }
    }

    private static TrackSummary Track(string? artist = "Artist", string title = "Song", string file = "01.flac") =>
        new(Guid.NewGuid(), title, artist, "Album", TimeSpan.FromSeconds(120), "file:///C:/Music/" + file);

    [Fact]
    public async Task A_song_is_announced_when_it_starts_and_scrobbled_once_half_was_heard()
    {
        var rig = new Rig();
        await rig.SignInAsync();
        await rig.Player.PlayAsync(Track());

        await rig.PlayToAsync(1);
        var announced = Assert.Single(rig.Web.Sent);
        Assert.Equal("track.updateNowPlaying", announced["method"]);
        Assert.Equal(("Artist", "Song", "Album", "120", "sk"),
            (announced["artist"], announced["track"], announced["album"], announced["duration"], announced["sk"]));

        await rig.PlayToAsync(59);
        Assert.Single(rig.Web.Sent);

        await rig.PlayToAsync(120);
        Assert.Equal(["track.updateNowPlaying", "track.scrobble"], rig.Web.Methods);
        Assert.Equal("1700000000", rig.Web.Sent[1]["timestamp[0]"]); // when it started, not when it counted
        Assert.Equal("Song", rig.Web.Sent[1]["track[0]"]);
    }

    [Fact]
    public async Task Nothing_is_sent_while_paused_signed_out_or_switched_off()
    {
        var rig = new Rig();
        await rig.Player.PlayAsync(Track());
        await rig.PlayToAsync(120); // signed out

        await rig.SignInAsync();
        rig.Settings.ScrobblingEnabled = false;
        await rig.Player.PlayAsync(Track());
        await rig.PlayToAsync(120);

        rig.Settings.ScrobblingEnabled = true;
        await rig.Player.PlayAsync(Track());
        rig.Player.TogglePlayPauseCommand.Execute(null);
        await rig.PlayToAsync(120); // the fake's position moves, but the player says it is paused

        Assert.Empty(rig.Web.Sent);
    }

    [Fact]
    public async Task Seeking_ahead_does_not_count_and_the_next_song_starts_afresh()
    {
        var rig = new Rig();
        await rig.SignInAsync();
        await rig.Player.PlayAsync(Track());
        await rig.PlayToAsync(10);

        rig.Player.PositionSeconds = 100; // the user drags the slider
        await rig.PlayToAsync(120);
        Assert.Equal(["track.updateNowPlaying"], rig.Web.Methods);

        await rig.Player.PlayAsync(Track(title: "Next"));
        await rig.PlayToAsync(60);
        Assert.Equal(["track.updateNowPlaying", "track.updateNowPlaying", "track.scrobble"], rig.Web.Methods);
        Assert.Equal("Next", rig.Web.Sent[2]["track[0]"]);
    }

    [Fact]
    public async Task A_failed_send_does_not_disturb_anything()
    {
        var rig = new Rig();
        await rig.SignInAsync();
        rig.Web.Offline = true;
        await rig.Player.PlayAsync(Track());

        await rig.PlayToAsync(120);

        Assert.Empty(rig.Web.Sent);
        Assert.True(rig.Player.IsPlaying);
        Assert.Equal(ScrobbleResult.Failed, Assert.Single(rig.Reports).Result); // not for "now playing"
    }

    [Theory]
    [InlineData("""{"scrobbles":{"@attr":{"accepted":1,"ignored":0}}}""", ScrobbleResult.Scrobbled)]
    [InlineData("""{"scrobbles":{"@attr":{"accepted":0,"ignored":1}}}""", ScrobbleResult.Ignored)]
    [InlineData("""{"error":16,"message":"try again"}""", ScrobbleResult.Failed)]
    public async Task Each_scrobble_is_reported_once_with_what_became_of_it(string answer, ScrobbleResult result)
    {
        var rig = new Rig();
        await rig.SignInAsync();
        rig.Web.ScrobbleAnswer = answer;
        await rig.Player.PlayAsync(Track());

        await rig.PlayToAsync(59);
        Assert.Empty(rig.Reports);

        await rig.PlayToAsync(120);
        Assert.Equal(new ScrobbleReport(result, new LastFmTrack("Artist", "Song", "Album", TimeSpan.FromSeconds(120))),
            Assert.Single(rig.Reports));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData(PlayerViewModel.DroppedFileArtist)]
    public async Task A_song_without_a_real_artist_is_not_sent(string? artist)
    {
        var rig = new Rig(new Tags("Title"));
        await rig.SignInAsync();
        await rig.Player.PlayAsync(Track(artist));

        await rig.PlayToAsync(120);

        Assert.Empty(rig.Web.Sent);
    }

    [Theory]
    [InlineData("Title", 2)] // named after its title tag: fine
    [InlineData("Artist", 0)] // the title is only the file's name
    [InlineData(null, 0)] // no library to ask
    public async Task A_title_that_matches_the_file_name_is_sent_only_if_the_file_has_that_tag(string? tag, int sends)
    {
        var tags = tag is null ? null : new Tags(tag);
        var rig = new Rig(tags);
        await rig.SignInAsync();
        await rig.Player.PlayAsync(Track(title: "My Song", file: "My%20Song.flac"));

        await rig.PlayToAsync(120);

        Assert.Equal(sends, rig.Web.Sent.Count);
    }
}
