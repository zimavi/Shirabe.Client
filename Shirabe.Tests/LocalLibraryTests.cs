using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shirabe.Library;
using Xunit;

namespace Shirabe.Tests;

public class LocalLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shirabe-test-{Guid.NewGuid():N}");
    private readonly string _music;
    private readonly string _dbPath;

    public LocalLibraryTests()
    {
        _music = Path.Combine(_root, "music");
        Directory.CreateDirectory(_music);
        _dbPath = Path.Combine(_root, "db", "library.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); // SQLite keeps the file open otherwise
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Scan_adds_recordings_artists_and_a_release_from_the_tags()
    {
        Fixture("a1.flac", "Artist A/a1.flac");
        Fixture("a2.flac", "Artist A/a2.flac");
        Fixture("b1.flac", "b1.flac");
        var library = LocalLibrary.Open(_dbPath);

        var result = await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Added);
        Assert.Empty(result.Failed);
        var tracks = await library.GetTracksAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["First Song", "Lonely Single", "Second Song"], tracks.Select(t => t.Title));

        var first = tracks[0];
        Assert.Equal("Artist A", first.Artist);
        Assert.Equal("Album X", first.Album);
        Assert.Equal(0.5, first.Duration!.Value.TotalSeconds, precision: 1);
        Assert.StartsWith("file:///", first.MediaUri);
        Assert.EndsWith("/a1.flac", first.MediaUri);
        Assert.Null(tracks[1].Album); // the single has no album tag

        await using var db = OpenDb();
        Assert.Equal(["Artist A", "Artist B"], db.Artists.Select(a => a.Name).OrderBy(n => n));
        var release = await db.Releases.Include(r => r.Tracks).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Album X", release.Title);
        Assert.Equal(2020, release.Year);
        Assert.Equal([1, 2], release.Tracks.Select(t => t.TrackNumber).Order());
        Assert.All(release.Tracks, t => Assert.Equal(1, t.DiscNumber));
    }

    [Fact]
    public async Task Id3_tags_with_n_of_m_track_and_disc_numbers_are_read()
    {
        Fixture("c1.mp3", "c1.mp3"); // track "3/12", disc "1/2", album artist differs from the track artist
        var library = LocalLibrary.Open(_dbPath);

        await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);

        var track = Assert.Single(await library.GetTracksAsync(TestContext.Current.CancellationToken));
        Assert.Equal(("Sunrise", "Artist C", "Album Y"), (track.Title, track.Artist, track.Album));

        await using var db = OpenDb();
        var releaseTrack = await db.ReleaseTracks.Include(t => t.Release).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal((1, 3), (releaseTrack.DiscNumber, releaseTrack.TrackNumber));
        Assert.Equal(2019, releaseTrack.Release.Year);
        var albumArtist = await db.ArtistCredits.Where(c => c.Role == "album-artist").Select(c => c.Artist.Name)
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Various Artists", albumArtist);
    }

    [Fact]
    public async Task A_file_without_a_title_tag_is_titled_after_its_file_name()
    {
        Fixture("untagged.flac", "untagged.flac");
        var library = LocalLibrary.Open(_dbPath);

        await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);

        var track = Assert.Single(await library.GetTracksAsync(TestContext.Current.CancellationToken));
        Assert.Equal("untagged", track.Title);
        Assert.Null(track.Artist);
    }

    [Fact]
    public async Task Rescanning_unchanged_files_changes_nothing()
    {
        Fixture("a1.flac", "a1.flac");
        Fixture("b1.flac", "b1.flac");
        var library = LocalLibrary.Open(_dbPath);
        await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);
        var before = await library.GetTracksAsync(TestContext.Current.CancellationToken);

        var result = await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((0, 0, 2, 0), (result.Added, result.Updated, result.Unchanged, result.MarkedMissing));
        Assert.Empty(result.Failed);
        Assert.Equal(before, await library.GetTracksAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_changed_file_updates_its_recording_in_place()
    {
        Fixture("a1.flac", "a1.flac");
        var library = LocalLibrary.Open(_dbPath);
        await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);
        var original = Assert.Single(await library.GetTracksAsync(TestContext.Current.CancellationToken));

        Fixture("a1-retagged.flac", "a1.flac"); // same path, new content
        var result = await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Updated);
        var updated = Assert.Single(await library.GetTracksAsync(TestContext.Current.CancellationToken));
        Assert.Equal("First Song (Remastered)", updated.Title);
        Assert.Equal(original.RecordingId, updated.RecordingId); // identity survives, so ratings and playlists will too

        await using var db = OpenDb();
        Assert.Equal(1, await db.Releases.CountAsync(TestContext.Current.CancellationToken)); // the release was reused
    }

    [Fact]
    public async Task A_deleted_file_is_marked_missing_not_deleted_and_returns_with_the_same_id()
    {
        Fixture("a1.flac", "a1.flac");
        Fixture("a2.flac", "a2.flac");
        var library = LocalLibrary.Open(_dbPath);
        await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);
        var second = (await library.GetTracksAsync(TestContext.Current.CancellationToken)).Single(t => t.Title == "Second Song");

        File.Delete(Path.Combine(_music, "a2.flac"));
        var result = await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.MarkedMissing);
        Assert.Equal(["First Song"], (await library.GetTracksAsync(TestContext.Current.CancellationToken)).Select(t => t.Title));
        await using (var db = OpenDb())
            Assert.Equal(2, await db.Recordings.CountAsync(TestContext.Current.CancellationToken)); // nothing was deleted

        Fixture("a2.flac", "a2.flac");
        await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);

        var back = (await library.GetTracksAsync(TestContext.Current.CancellationToken)).Single(t => t.Title == "Second Song");
        Assert.Equal(second.RecordingId, back.RecordingId);
    }

    [Fact]
    public async Task An_unreadable_file_is_reported_and_the_rest_is_still_scanned()
    {
        Fixture("a1.flac", "a1.flac");
        await File.WriteAllBytesAsync(Path.Combine(_music, "broken.flac"), [1, 2, 3, 4, 5, 6, 7, 8], TestContext.Current.CancellationToken);
        var library = LocalLibrary.Open(_dbPath);

        var result = await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Added);
        var failure = Assert.Single(result.Failed);
        Assert.EndsWith("broken.flac", failure.Path);
        Assert.Equal(["First Song"], (await library.GetTracksAsync(TestContext.Current.CancellationToken)).Select(t => t.Title));
    }

    [Fact]
    public async Task Scanning_one_folder_does_not_mark_a_sibling_folder_with_the_same_prefix_missing()
    {
        var other = Path.Combine(_root, "music2");
        Directory.CreateDirectory(other);
        Fixture("a1.flac", "a1.flac");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "b1.flac"), Path.Combine(other, "b1.flac"));
        var library = LocalLibrary.Open(_dbPath);
        await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);
        await library.ScanFolderAsync(other, cancellationToken: TestContext.Current.CancellationToken);

        var result = await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.MarkedMissing);
        Assert.Equal(2, (await library.GetTracksAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task Reopening_the_database_keeps_the_data_and_makes_no_backup()
    {
        Fixture("a1.flac", "a1.flac");
        await LocalLibrary.Open(_dbPath).ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);

        var reopened = LocalLibrary.Open(_dbPath);

        Assert.Single(await reopened.GetTracksAsync(TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, "*.bak-*"));
    }

    [Fact]
    public async Task A_cancelled_scan_throws()
    {
        Fixture("a1.flac", "a1.flac");
        var library = LocalLibrary.Open(_dbPath);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.ScanFolderAsync(_music, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Embedded_covers_are_stored_once_per_image_and_attached_to_their_songs()
    {
        Fixture("cover-a1.flac", "cover-a1.flac");
        Fixture("cover-a2.mp3", "cover-a2.mp3"); // same picture as the FLAC, in a different container
        Fixture("cover-b1.flac", "cover-b1.flac");
        Fixture("a1.flac", "a1.flac"); // no cover
        var library = LocalLibrary.Open(_dbPath);

        await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);

        var tracks = (await library.GetTracksAsync(TestContext.Current.CancellationToken)).ToDictionary(t => t.Title);
        Assert.Null(tracks["First Song"].ArtworkPath);
        Assert.Equal(tracks["Covered One"].ArtworkPath, tracks["Covered Two"].ArtworkPath);
        Assert.NotEqual(tracks["Covered One"].ArtworkPath, tracks["Covered Three"].ArtworkPath);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(Path.GetDirectoryName(_dbPath)!, "artwork")).Length);

        var cover = tracks["Covered One"].ArtworkPath!;
        Assert.EndsWith(".jpg", cover);
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "coverA.jpg"), TestContext.Current.CancellationToken),
            await File.ReadAllBytesAsync(cover, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_song_whose_file_lost_its_cover_loses_it_in_the_library_too()
    {
        Fixture("cover-a1.flac", "song.flac");
        var library = LocalLibrary.Open(_dbPath);
        await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(Assert.Single(await library.GetTracksAsync(TestContext.Current.CancellationToken)).ArtworkPath);

        Fixture("a1-retagged.flac", "song.flac"); // same path, no picture
        await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(await library.GetTracksAsync(TestContext.Current.CancellationToken)).ArtworkPath);
    }

    [Fact]
    public async Task A_cover_that_cannot_be_stored_does_not_stop_the_scan()
    {
        Fixture("cover-a1.flac", "cover-a1.flac");
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(_dbPath)!, "artwork"), "a file where the folder should be",
            TestContext.Current.CancellationToken);
        var library = LocalLibrary.Open(_dbPath);

        var result = await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Added);
        var track = Assert.Single(await library.GetTracksAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Covered One", track.Title);
        Assert.Null(track.ArtworkPath);
    }

    [Fact]
    public async Task Upgrading_an_older_library_keeps_its_data_makes_a_backup_and_the_next_scan_adds_covers()
    {
        // A library as the first version created it (the Initial migration only), with one song in it.
        var file = Fixture("cover-a1.flac", "cover-a1.flac");
        var info = new FileInfo(file);
        var recordingId = Guid.CreateVersion7();
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        await using (var old = OpenDb())
        {
            old.GetService<IMigrator>().Migrate("Initial");
            await old.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO Recordings (Id, Title, Duration) VALUES ({recordingId.ToString().ToUpperInvariant()}, 'Old Title', NULL)",
                TestContext.Current.CancellationToken);
            await old.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO MediaSources (Id, RecordingId, Uri, SizeBytes, ModifiedUtcTicks, IsMissing) VALUES ({Guid.CreateVersion7().ToString().ToUpperInvariant()}, {recordingId.ToString().ToUpperInvariant()}, {new Uri(file).AbsoluteUri}, {info.Length}, {info.LastWriteTimeUtc.Ticks}, 0)",
                TestContext.Current.CancellationToken);
        }

        var library = LocalLibrary.Open(_dbPath); // applies AddArtwork

        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, "*.bak-*")); // a copy of the old database
        var before = Assert.Single(await library.GetTracksAsync(TestContext.Current.CancellationToken));
        Assert.Equal(("Old Title", recordingId), (before.Title, before.RecordingId)); // nothing lost
        Assert.Null(before.ArtworkPath);

        var result = await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Updated); // the file did not change, but the old scanner never looked for covers
        var after = Assert.Single(await library.GetTracksAsync(TestContext.Current.CancellationToken));
        Assert.Equal(recordingId, after.RecordingId);
        Assert.Equal("Covered One", after.Title);
        Assert.NotNull(after.ArtworkPath);

        var again = await library.ScanFolderAsync(_music, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal((0, 1), (again.Updated, again.Unchanged)); // and only once
    }

    // Copies a fixture file to a relative path inside the music folder and returns the new path.
    private string Fixture(string name, string? target = null)
    {
        var destination = Path.Combine(_music, target ?? name);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), destination, overwrite: true);
        return destination;
    }

    private LibraryDbContext OpenDb() =>
        new(new DbContextOptionsBuilder<LibraryDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString()).Options);
}
