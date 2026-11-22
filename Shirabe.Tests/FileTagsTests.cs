using Microsoft.Data.Sqlite;
using Shirabe.Core.Metadata;
using Shirabe.Library;
using Xunit;

namespace Shirabe.Tests;

/// <summary>Listing what an audio file itself contains (the "File tags" part of the properties dialog).</summary>
public class FileTagsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shirabe-tags-{Guid.NewGuid():N}");
    private readonly string _music;
    private readonly LocalLibrary _library;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public FileTagsTests()
    {
        _music = Path.Combine(_root, "music");
        Directory.CreateDirectory(_music);
        Fixture("cover-a1.flac");
        Fixture("c1.mp3");
        _library = LocalLibrary.Open(Path.Combine(_root, "db", "library.db"));
        _library.ScanFolderAsync(_music, cancellationToken: Ct).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    private async Task<IReadOnlyList<FileTag>> TagsOf(string title)
    {
        var track = (await _library.GetTracksAsync(Ct)).Single(t => t.Title == title);
        return await _library.ReadFileTagsAsync(track.RecordingId, Ct);
    }

    private static string? Value(IReadOnlyList<FileTag> tags, string group, string name) =>
        tags.SingleOrDefault(t => t.Group == group && t.Name == name)?.Value;

    [Fact]
    public async Task A_flac_file_lists_its_tags_the_picture_and_the_raw_tags_ATL_does_not_name()
    {
        var tags = await TagsOf("Covered One");

        Assert.Equal("Covered One", Value(tags, FileTag.TagsGroup, "Title"));
        Assert.Equal("Artist K", Value(tags, FileTag.TagsGroup, "Artist"));
        Assert.Equal("Album Z", Value(tags, FileTag.TagsGroup, "Album"));
        Assert.Equal("generic", Value(tags, FileTag.TagsGroup, "Pictures")); // the type the fixture's picture was written with
        Assert.Equal("Lavf60.17.100", Value(tags, FileTag.TagsGroup, "VORBIS-VENDOR")); // raw name, nothing hidden
        Assert.NotNull(Value(tags, FileTag.AudioGroup, "Format"));
        Assert.Equal("22,050 Hz", Value(tags, FileTag.AudioGroup, "Sample rate")?.Replace(' ', ','));
    }

    [Fact]
    public async Task An_mp3_lists_numbers_as_n_of_total_and_the_album_artist()
    {
        var tags = await TagsOf("Sunrise");

        Assert.Equal("3/12", Value(tags, FileTag.TagsGroup, "Track"));
        Assert.Equal("1/2", Value(tags, FileTag.TagsGroup, "Disc"));
        Assert.Equal("2019", Value(tags, FileTag.TagsGroup, "Year"));
        Assert.Equal("Various Artists", Value(tags, FileTag.TagsGroup, "Album artist"));
        Assert.Null(Value(tags, FileTag.TagsGroup, "Pictures")); // no picture, so no line
    }

    [Fact]
    public async Task Tags_without_a_value_are_left_out_and_tags_come_before_audio_facts()
    {
        var tags = await TagsOf("Covered One");

        Assert.DoesNotContain(tags, t => t.Group == FileTag.TagsGroup && t.Name is "Genre" or "Composer" or "Comment");
        Assert.DoesNotContain(tags, t => string.IsNullOrWhiteSpace(t.Value));
        var firstAudio = tags.ToList().FindIndex(t => t.Group == FileTag.AudioGroup);
        Assert.DoesNotContain(tags.Skip(firstAudio), t => t.Group == FileTag.TagsGroup);
    }

    [Fact]
    public async Task A_file_that_was_deleted_is_reported_as_gone()
    {
        var track = (await _library.GetTracksAsync(Ct)).Single(t => t.Title == "Covered One");
        File.Delete(Path.Combine(_music, "cover-a1.flac"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => _library.ReadFileTagsAsync(track.RecordingId, Ct));
    }

    [Fact]
    public async Task A_song_that_is_not_in_the_library_is_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _library.ReadFileTagsAsync(Guid.NewGuid(), Ct));
    }

    private void Fixture(string name) =>
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), Path.Combine(_music, name), overwrite: true);
}
