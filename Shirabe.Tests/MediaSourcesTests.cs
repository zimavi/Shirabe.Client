using Shirabe.Core.Media;
using Xunit;

namespace Shirabe.Tests;

public class MediaSourcesTests
{
    [Fact]
    public void A_file_address_becomes_a_local_file_source_even_with_spaces_and_non_ascii_characters()
    {
        var path = Path.Combine(Path.GetTempPath(), "Müsik Ünï", "Track 01.flac");

        var source = MediaSources.FromUri(new Uri(path).AbsoluteUri);

        Assert.Equal(path, Assert.IsType<LocalFileSource>(source).Path);
    }

    [Fact]
    public void An_address_no_provider_handles_is_rejected()
    {
        Assert.Throws<NotSupportedException>(() => MediaSources.FromUri("https://example.test/a.mp3"));
    }

    [Fact]
    public void Text_that_is_not_an_address_is_rejected()
    {
        Assert.Throws<UriFormatException>(() => MediaSources.FromUri("not an address"));
    }
}
