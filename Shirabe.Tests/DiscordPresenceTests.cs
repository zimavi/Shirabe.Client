using DiscordRPC;
using Shirabe.Client.Services;
using System.Text.Json;
using Xunit;

namespace Shirabe.Tests;

public class DiscordPresenceTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Card_shows_title_artist_album_and_a_progress_bar_from_the_position()
    {
        var presence = DiscordPresence.Build("Силуети", "СТРУКТУРА ЩАСТЯ", "Неонова мрія", "https://example.com/c.jpg",
            TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(177), Now);

        Assert.Equal(ActivityType.Listening, presence.Type);
        Assert.Equal("Силуети", presence.Details);
        Assert.Equal("СТРУКТУРА ЩАСТЯ", presence.State);
        Assert.Equal("Неонова мрія", presence.Assets.LargeImageText);
        Assert.Equal("https://example.com/c.jpg", presence.Assets.LargeImageKey);
        Assert.Equal(Now.AddSeconds(-16), presence.Timestamps.Start);
        Assert.Equal(Now.AddSeconds(161), presence.Timestamps.End);
    }

    [Fact]
    public void Unknown_duration_has_no_end_and_no_cover_falls_back_to_the_main_image()
    {
        var presence = DiscordPresence.Build("Song", null, null, null, TimeSpan.Zero, TimeSpan.Zero, Now);

        Assert.Null(presence.Timestamps.End);
        Assert.Null(presence.State);
        Assert.Equal("main", presence.Assets.LargeImageKey);
        Assert.Null(presence.Assets.LargeImageText);
    }

    [Fact]
    public void Fields_discord_would_reject_are_fitted()
    {
        var presence = DiscordPresence.Build(new string('я', 200), null, "A", null, TimeSpan.Zero, TimeSpan.Zero, Now);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(presence.Details) <= 128);
        Assert.True(presence.Assets.LargeImageText.Length >= 2);
    }

    private const string Response = """
        {"results":[
          {"collectionName":"Discovery (Deluxe)","artistName":"Someone Else","artworkUrl100":"https://x/wrong/100x100bb.jpg"},
          {"collectionName":"Discovery - Single","artistName":"Daft Punk","artworkUrl100":"https://x/right/100x100bb.jpg"}]}
        """;

    [Fact]
    public void Cover_lookup_takes_the_first_result_matching_album_and_artist_at_512_px()
    {
        var json = JsonDocument.Parse(Response).RootElement;

        Assert.Equal("https://x/right/512x512bb.jpg", CoverLookup.PickCover(json, "Daft Punk", "Discovery"));
        Assert.Null(CoverLookup.PickCover(json, "Daft Punk", "Homework"));
    }
}
