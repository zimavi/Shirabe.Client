using Shirabe.Core.Lyrics;
using Shirabe.Library;
using Xunit;

namespace Shirabe.Tests;

/// <summary>Lyrics beside the song and in its tags, found from real files.</summary>
public sealed class LocalLyricsSourceTests : IDisposable
{
    private const string Ttml = """
        <tt xmlns="http://www.w3.org/ns/ttml"><body><div>
          <p begin="1s" end="3s"><span begin="1s" end="2s">Sung</span> <span begin="2s" end="3s">words</span></p>
        </div></body></tt>
        """;

    private const string Lrc = "[00:01.00]from lrc\n[00:05.00]second";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"shirabe-lyrics-{Guid.NewGuid():N}");
    private readonly LocalLyricsSource _source = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public LocalLyricsSourceTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Song(string fixture, string name = "song")
    {
        var path = Path.Combine(_folder, name + Path.GetExtension(fixture));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), path);
        return path;
    }

    private static string[] Texts(SongLyrics? lyrics) => [.. lyrics!.Lines.Select(l => l.Text)];

    private Task<SongLyrics?> Find(string audio) => _source.FindAsync(new Uri(audio).AbsoluteUri, Ct);

    private static void WriteTag(string audio, Action<ATL.Track> change)
    {
        var track = new ATL.Track(audio);
        change(track);
        Assert.True(track.Save());
    }

    [Fact]
    public async Task A_ttml_file_beside_the_song_is_found_with_its_word_times()
    {
        var audio = Song("a1.flac");
        File.WriteAllText(Path.ChangeExtension(audio, ".ttml"), Ttml);

        var lyrics = await Find(audio);

        Assert.True(lyrics!.IsSynced);
        Assert.Equal("beside the song: song.ttml", lyrics.Source);
        Assert.Equal(["Sung words"], Texts(lyrics));
        Assert.Equal(2, lyrics.Lines[0].Words!.Count);
    }

    [Fact]
    public async Task An_lrc_file_beside_the_song_is_found_and_ttml_beats_it()
    {
        var audio = Song("a1.flac");
        File.WriteAllText(Path.ChangeExtension(audio, ".lrc"), Lrc);
        Assert.Equal("beside the song: song.lrc", (await Find(audio))!.Source);

        File.WriteAllText(Path.ChangeExtension(audio, ".ttml"), Ttml);

        Assert.Equal("beside the song: song.ttml", (await Find(audio))!.Source);
    }

    [Fact]
    public async Task Timed_lyrics_beat_plain_ones_wherever_they_are()
    {
        var audio = Song("a1.flac");
        File.WriteAllText(Path.ChangeExtension(audio, ".lrc"), "just words\nno times");
        WriteTag(audio, t => t.Lyrics.Add(new ATL.LyricsInfo { UnsynchronizedLyrics = Lrc }));

        var lyrics = await Find(audio);

        Assert.True(lyrics!.IsSynced); // the tag holds LRC text, the sidecar only plain words
        Assert.Equal("in the file's tags", lyrics.Source);
        Assert.Equal(["from lrc", "second"], Texts(lyrics));
    }

    [Fact]
    public async Task A_sidecar_beats_the_tags_when_both_are_timed()
    {
        var audio = Song("a1.flac");
        File.WriteAllText(Path.ChangeExtension(audio, ".lrc"), "[00:01.00]sidecar");
        WriteTag(audio, t => t.Lyrics.Add(new ATL.LyricsInfo { UnsynchronizedLyrics = Lrc }));

        Assert.Equal(["sidecar"], Texts(await Find(audio)));
    }

    [Theory]
    [InlineData("a1.flac")]
    [InlineData("c1.mp3")]
    public async Task Plain_lyrics_in_the_tags_are_found(string fixture)
    {
        var audio = Song(fixture);
        WriteTag(audio, t => t.Lyrics.Add(new ATL.LyricsInfo { UnsynchronizedLyrics = "first line\nsecond line" }));

        var lyrics = await Find(audio);

        Assert.False(lyrics!.IsSynced);
        Assert.Equal(["first line", "second line"], Texts(lyrics));
    }

    [Fact]
    public async Task Ttml_inside_a_tag_is_recognised_by_its_content()
    {
        var audio = Song("a1.flac");
        WriteTag(audio, t => t.Lyrics.Add(new ATL.LyricsInfo { UnsynchronizedLyrics = Ttml }));

        var lyrics = await Find(audio);

        Assert.True(lyrics!.IsSynced);
        Assert.Equal(["Sung words"], Texts(lyrics));
    }

    [Fact]
    public async Task Timed_lyrics_in_an_id3_tag_are_found()
    {
        var audio = Song("c1.mp3");
        WriteTag(audio, t => t.Lyrics.Add(new ATL.LyricsInfo
        {
            ContentType = ATL.LyricsInfo.LyricsType.LYRICS,
            Format = ATL.LyricsInfo.LyricsFormat.LRC,
            SynchronizedLyrics = [new(2000, "later"), new(1000, "earlier")],
        }));

        var lyrics = await Find(audio);

        Assert.True(lyrics!.IsSynced);
        Assert.Equal(["earlier", "later"], Texts(lyrics));
        Assert.Equal(1, lyrics.Lines[0].Start!.Value.TotalSeconds);
    }

    [Fact]
    public async Task No_lyrics_a_missing_song_a_non_file_address_and_junk_files_all_give_null()
    {
        var audio = Song("a1.flac");
        Assert.Null(await Find(audio));

        File.WriteAllText(Path.ChangeExtension(audio, ".ttml"), "<tt><broken");
        File.WriteAllText(Path.ChangeExtension(audio, ".lrc"), "[ar:only a tag]");
        Assert.Null(await Find(audio));

        Assert.Null(await Find(Path.Combine(_folder, "gone.flac")));
        Assert.Null(await _source.FindAsync("https://example.com/song.mp3", Ct));
    }

    [Fact]
    public async Task A_file_far_larger_than_lyrics_is_not_read()
    {
        var audio = Song("a1.flac");
        File.WriteAllText(Path.ChangeExtension(audio, ".lrc"), "[00:01.00]" + new string('x', 1024 * 1024 + 10));

        Assert.Null(await Find(audio));
    }
}
