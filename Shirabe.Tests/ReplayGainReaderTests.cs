using Shirabe.Core.Playback;
using Shirabe.Library;
using Xunit;

namespace Shirabe.Tests;

/// <summary>ReplayGain tags read back from real files.</summary>
public class ReplayGainReaderTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("shirabe-gain-").FullName;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("cover-a1.flac")]
    [InlineData("c1.mp3")]
    public async Task The_tags_of_a_file_are_read(string fixture)
    {
        var path = Copy(fixture);
        var track = new ATL.Track(path);
        track.AdditionalFields["REPLAYGAIN_TRACK_GAIN"] = "-6.20 dB";
        track.AdditionalFields["REPLAYGAIN_TRACK_PEAK"] = "0.988";
        track.AdditionalFields["replaygain_album_gain"] = "+1.5 dB";
        track.AdditionalFields["REPLAYGAIN_ALBUM_PEAK"] = "not a number";
        Assert.True(track.Save());

        var gain = await new ReplayGainReader().ReadAsync(new Uri(path).AbsoluteUri, Ct);

        Assert.Equal(new ReplayGain(-6.2, 0.988, 1.5, null), gain);
    }

    [Fact]
    public async Task No_tags_a_missing_file_or_something_that_is_not_a_file_give_nothing()
    {
        var reader = new ReplayGainReader();

        Assert.Null(await reader.ReadAsync(new Uri(Copy("c1.mp3")).AbsoluteUri, Ct));
        Assert.Null(await reader.ReadAsync(new Uri(Path.Combine(_folder, "gone.flac")).AbsoluteUri, Ct));
        Assert.Null(await reader.ReadAsync("https://example.invalid/a.mp3", Ct));
    }

    private string Copy(string fixture)
    {
        var path = Path.Combine(_folder, fixture);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), path, overwrite: true);
        return path;
    }
}
