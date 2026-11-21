using Shirabe.Core.Media;
using Xunit;

namespace Shirabe.Tests;

public class LocalFileSourceTests
{
    [Fact]
    public async Task OpenReadAsync_returns_a_seekable_stream_of_the_file()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3, 4], TestContext.Current.CancellationToken);

            await using var stream = await new LocalFileSource(path).OpenReadAsync(TestContext.Current.CancellationToken);

            Assert.True(stream.CanSeek);
            Assert.Equal(4, stream.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenReadAsync_throws_when_the_file_is_missing()
    {
        var source = new LocalFileSource(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => source.OpenReadAsync(TestContext.Current.CancellationToken));
    }
}
