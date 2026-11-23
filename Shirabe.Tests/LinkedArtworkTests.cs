using System.Net;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Shirabe.Library;
using Shirabe.Metadata;
using Xunit;

namespace Shirabe.Tests;

/// <summary>Covers that metadata documents link to: downloaded with checks, cached, and shown in place of the file's own.</summary>
public sealed class LinkedArtworkTests : IDisposable
{
    private const string Url = "https://example.com/front.png";

    // Enough of a PNG for the cache to recognise it as one; the cache judges by content.
    private static readonly byte[] Picture = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    private static readonly string PictureHash = Convert.ToHexString(SHA256.HashData(Picture)).ToLowerInvariant();

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shirabe-linked-{Guid.NewGuid():N}");
    private readonly FakeWeb _web = new();
    private readonly LocalLibrary _library;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public LinkedArtworkTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "music"));
        _library = LocalLibrary.Open(Path.Combine(_root, "db", "library.db"), _web);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeWeb : HttpMessageHandler
    {
        public int Requests { get; private set; }

        public Func<HttpResponseMessage> Respond { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Picture) };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(Respond());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_picture_is_downloaded_once_and_kept_under_its_hash_or_its_address(bool withHash)
    {
        var path = await _library.FetchAsync(Url, withHash ? PictureHash : null, Ct);

        Assert.Equal(Picture, await File.ReadAllBytesAsync(path!, Ct));
        if (withHash) Assert.Equal(PictureHash + ".png", Path.GetFileName(path));

        Assert.Equal(path, await _library.FetchAsync(Url, withHash ? PictureHash : null, Ct));
        Assert.Equal(1, _web.Requests);
    }

    [Fact]
    public async Task A_picture_that_is_not_the_one_promised_is_thrown_away()
    {
        Assert.Null(await _library.FetchAsync(Url, new string('0', 64), Ct));

        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "db"), "*.png", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Something_that_is_not_an_image_or_a_failed_download_gives_nothing()
    {
        _web.Respond = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>not a picture</html>") };
        Assert.Null(await _library.FetchAsync(Url, null, Ct));

        _web.Respond = () => new HttpResponseMessage(HttpStatusCode.NotFound);
        Assert.Null(await _library.FetchAsync(Url, null, Ct));

        _web.Respond = () => throw new HttpRequestException("no network");
        Assert.Null(await _library.FetchAsync(Url, null, Ct));
    }

    [Theory]
    [InlineData("http://example.com/front.png")]
    [InlineData("ftp://example.com/front.png")]
    [InlineData("file:///C:/cover.png")]
    [InlineData("https://user:secret@example.com/front.png")]
    [InlineData("not a url")]
    public async Task Only_plain_https_addresses_are_fetched(string url)
    {
        Assert.Null(await _library.FetchAsync(url, null, Ct));
        Assert.Equal(0, _web.Requests);
    }

    [Fact]
    public async Task A_song_shows_the_linked_cover_only_after_it_was_downloaded()
    {
        var audio = Path.Combine(_root, "music", "a1.flac");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "a1.flac"), audio);
        File.WriteAllText(SidecarFile.PathFor(audio), $$"""
            { "format": "shirabe.metadata", "version": "0.1",
              "subject": { "type": "recording", "id": "0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55" },
              "layer": "canonical", "source": { "kind": "artist", "created": "2026-03-02T09:00:00Z" }, "revision": 1,
              "fields": { "artwork": [ { "url": "{{Url}}", "role": "front", "sha256": "{{PictureHash}}" } ] } }
            """);
        await _library.ScanFolderAsync(Path.Combine(_root, "music"), cancellationToken: Ct);

        var before = Assert.Single(await _library.GetTracksAsync(Ct));
        Assert.Equal((Url, PictureHash), (before.CoverUrl, before.CoverSha256));
        Assert.Null(before.ArtworkPath); // listing never downloads
        Assert.Equal(0, _web.Requests);

        var fetched = await _library.FetchAsync(Url, PictureHash, Ct);

        Assert.Equal(fetched, Assert.Single(await _library.GetTracksAsync(Ct)).ArtworkPath);
    }

    // ---- the player downloads the cover of the song it loads ----

    private sealed class FakeLinks : IArtworkLinks
    {
        public List<(string Url, string? Sha)> Asked { get; } = [];

        public TaskCompletionSource<string?> Answer { get; } = new();

        public Task<string?> FetchAsync(string url, string? sha256, CancellationToken cancellationToken = default)
        {
            Asked.Add((url, sha256));
            return Answer.Task;
        }
    }

    private static TrackSummary Track(string title, string? coverUrl) =>
        new(Guid.NewGuid(), title, "Artist", null, null, new Uri(Path.Combine(Path.GetTempPath(), title + ".mp3")).AbsoluteUri,
            CoverUrl: coverUrl, CoverSha256: "abc");

    [Fact]
    public async Task The_player_shows_the_downloaded_cover_instead_of_the_files_own()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var player = new PlayerViewModel(new FakeAudioPlayer());
        var links = new FakeLinks();
        _ = new LinkedCovers(player, new AppSettings(), links);

        var playing = player.PlayAsync(Track("A", Url) with { ArtworkPath = "embedded.jpg" });
        Assert.Equal("embedded.jpg", player.ArtworkPath); // the song starts at once with what it has
        Assert.Equal([(Url, "abc")], links.Asked);

        links.Answer.SetResult("downloaded.png");
        await playing;
        await Task.Yield();

        Assert.Equal("downloaded.png", player.ArtworkPath);
    }

    [Fact]
    public async Task A_cover_that_arrives_after_the_song_changed_is_not_shown()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var player = new PlayerViewModel(new FakeAudioPlayer());
        var links = new FakeLinks();
        _ = new LinkedCovers(player, new AppSettings(), links);

        await player.PlayAsync(Track("A", Url));
        await player.PlayAsync(Track("B", null));
        links.Answer.SetResult("late.png");
        await Task.Yield();

        Assert.NotEqual("late.png", player.ArtworkPath);
    }

    [Fact]
    public async Task With_the_setting_off_nothing_is_downloaded()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var player = new PlayerViewModel(new FakeAudioPlayer());
        var links = new FakeLinks();
        _ = new LinkedCovers(player, new AppSettings { DownloadArtworkEnabled = false }, links);

        await player.PlayAsync(Track("A", Url));

        Assert.Empty(links.Asked);
    }
}
