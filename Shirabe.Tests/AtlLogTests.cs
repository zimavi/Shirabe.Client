using Microsoft.Data.Sqlite;
using Shirabe.Library;
using Xunit;

namespace Shirabe.Tests;

#if DEBUG
/// <summary>ATL logs instead of throwing for most damage in a file; a Debug build writes that log to the console.</summary>
[Collection("Console")] // redirects Console.Error
public sealed class AtlLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shirabe-atl-{Guid.NewGuid():N}");
    private readonly TextWriter _error = Console.Error;

    public AtlLogTests() => Directory.CreateDirectory(Path.Combine(_root, "music"));

    public void Dispose()
    {
        Console.SetError(_error);
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task What_atl_logs_about_a_damaged_file_reaches_the_console()
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "c1.mp3"), TestContext.Current.CancellationToken);
        bytes[6] = bytes[7] = bytes[8] = bytes[9] = 0x7F; // an ID3 tag that claims to be 256 MiB long
        await File.WriteAllBytesAsync(Path.Combine(_root, "music", "damaged.mp3"), bytes, TestContext.Current.CancellationToken);
        var library = LocalLibrary.Open(Path.Combine(_root, "db", "library.db"));
        var captured = new StringWriter();
        Console.SetError(captured);

        await library.ScanFolderAsync(Path.Combine(_root, "music"), cancellationToken: TestContext.Current.CancellationToken);

        Console.SetError(_error);
        Assert.Contains("[Shirabe] ATL error: ID3v2 size is zero", captured.ToString());
    }

    [Fact]
    public async Task A_healthy_file_and_a_tag_write_log_nothing_because_ATLs_debug_chatter_is_left_out()
    {
        var path = Path.Combine(_root, "music", "a1.flac");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "a1.flac"), path);
        var library = LocalLibrary.Open(Path.Combine(_root, "db", "library.db"));
        var captured = new StringWriter();
        Console.SetError(captured);

        await library.ScanFolderAsync(Path.Combine(_root, "music"), cancellationToken: TestContext.Current.CancellationToken);
        var id = Assert.Single(await library.GetTracksAsync(TestContext.Current.CancellationToken)).RecordingId;
        var document = Shirabe.Metadata.DocumentTemplates.New(id, "canonical", DateTime.UtcNow);
        await library.WriteToFileAsync(id, document, Shirabe.Core.Metadata.DocumentOrigin.Embedded, TestContext.Current.CancellationToken);

        Console.SetError(_error);
        Assert.DoesNotContain("[Shirabe] ATL", captured.ToString());
    }
}
#endif
