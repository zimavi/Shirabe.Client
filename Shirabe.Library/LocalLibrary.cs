using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shirabe.Core.Library;
using Shirabe.Core.Metadata;
using Shirabe.Library.Model;

namespace Shirabe.Library;

/// <summary>The user's local music: a SQLite database filled by scanning folders.</summary>
public sealed partial class LocalLibrary : ILibrary, IFolderScanner, IMetadataStore, IFileTags
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".ogg", ".oga", ".opus", ".wav", ".m4a", ".aac", ".wma", ".aiff", ".aif",
    };

    private const int SaveEvery = 200;

    /// <summary>
    /// Bump when the scanner starts reading something new from files: unchanged files that an older version
    /// scanned are then read once more (see <see cref="MediaSource.ScannerVersion"/>).
    /// 1 = tags and covers. 2 = also the metadata documents (sidecar file and embedded tag).
    /// </summary>
    private const int ScannerVersion = 2;

    private readonly DbContextOptions<LibraryDbContext> _options;
    private readonly ArtworkCache _artwork;

    private LocalLibrary(DbContextOptions<LibraryDbContext> options, ArtworkCache artwork)
    {
        _options = options;
        _artwork = artwork;
    }

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shirabe", "library.db");

    /// <summary>
    /// Opens the database at <paramref name="path"/>, creating or migrating it. Before migrating an existing
    /// database a copy is kept next to it (library.db.bak-yyyyMMddHHmmss), because the library holds user data.
    /// </summary>
    public static LocalLibrary Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        var options = new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite(connectionString).Options;

        using var db = new LibraryDbContext(options);
        if (db.Database.GetPendingMigrations().Any() && db.Database.GetAppliedMigrations().Any())
        {
            SqliteConnection.ClearAllPools(); // release the file so it can be copied
            File.Copy(path, $"{path}.bak-{DateTime.UtcNow:yyyyMMddHHmmss}");
        }

        db.Database.Migrate();

        // Covers live in a folder next to the database, so moving or testing with another database moves them too.
        return new LocalLibrary(options, new ArtworkCache(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "artwork")));
    }

    public async Task<IReadOnlyList<TrackSummary>> GetTracksAsync(CancellationToken cancellationToken = default)
    {
        await using var db = new LibraryDbContext(_options);
        return await db.Recordings
            .AsNoTracking()
            .Where(r => r.Sources.Any(s => !s.IsMissing))
            .OrderBy(r => EF.Functions.Collate(r.Title, "NOCASE"))
            .ThenBy(r => r.Id)
            .Select(r => new TrackSummary(
                r.Id,
                r.Title,
                r.Credits.Where(c => c.Role == CreditRoles.Artist).OrderBy(c => c.SortOrder).Select(c => c.Artist.Name).FirstOrDefault(),
                r.ReleaseTracks.Select(t => t.Release.Title).FirstOrDefault(),
                r.Duration,
                r.Sources.Where(s => !s.IsMissing).OrderBy(s => s.Uri).Select(s => s.Uri).First(),
                r.ArtworkFile == null ? null : Path.Combine(_artwork.Directory, r.ArtworkFile)))
            .ToListAsync(cancellationToken);
    }

    public Task<ScanResult> ScanFolderAsync(
        string folder, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => ScanAsync(folder, progress, cancellationToken), cancellationToken);

    private async Task<ScanResult> ScanAsync(string folder, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var files = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Where(file => AudioExtensions.Contains(Path.GetExtension(file)))
            .ToList();

        await using var db = new LibraryDbContext(_options);

        // Everything already in the database is read WITHOUT change tracking and referred to by id. EF re-checks
        // every tracked entity on each SaveChanges, so tracking the whole library made a scan quadratic in its
        // size (a rescan of 6000 unchanged files took 9 s). Rows are attached only when they actually change.
        var rootUri = new Uri(root + Path.DirectorySeparatorChar).AbsoluteUri;
        // Prefix match in memory with ordinal comparison: the SQLite LIKE ignores case, file systems may not.
        var known = (await db.MediaSources.AsNoTracking().Where(s => s.Uri.StartsWith(rootUri)).ToListAsync(ct))
            .Where(s => s.Uri.StartsWith(rootUri, StringComparison.Ordinal))
            .ToDictionary(s => s.Uri);

        var artistIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var artist in await db.Artists.AsNoTracking().Select(a => new { a.Name, a.Id }).ToListAsync(ct))
            artistIds.TryAdd(artist.Name, artist.Id);

        var releaseIds = new Dictionary<(string Title, string Artist), Guid>();
        var existingReleases = await db.Releases.AsNoTracking()
            .Select(r => new
            {
                r.Id,
                r.Title,
                AlbumArtist = r.Credits.Where(c => c.Role == CreditRoles.AlbumArtist).Select(c => c.Artist.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);
        foreach (var release in existingReleases)
            releaseIds.TryAdd(ReleaseKey(release.Title, release.AlbumArtist ?? ""), release.Id);

        Guid FindOrCreateArtist(string name)
        {
            if (artistIds.TryGetValue(name, out var id)) return id;
            var created = new Artist { Name = name };
            db.Artists.Add(created);
            return artistIds[name] = created.Id;
        }

        Guid FindOrCreateRelease(FileTags tags)
        {
            // Without an album-artist tag the track artist stands in. A compilation without that tag therefore
            // splits per artist; merging by title alone would glue unrelated "Greatest Hits" together.
            var albumArtist = tags.AlbumArtist ?? tags.Artist ?? "";
            var key = ReleaseKey(tags.Album!, albumArtist);
            if (releaseIds.TryGetValue(key, out var id)) return id;

            var created = new Release { Title = tags.Album!, Year = tags.Year };
            if (albumArtist.Length > 0)
                created.Credits.Add(new ArtistCredit { ArtistId = FindOrCreateArtist(albumArtist), Role = CreditRoles.AlbumArtist });
            db.Releases.Add(created);
            return releaseIds[key] = created.Id;
        }

        void Apply(Recording recording, FileTags tags)
        {
            recording.Title = tags.Title;
            recording.Duration = tags.Duration;
            recording.ArtworkFile = SaveCover(tags);

            db.ArtistCredits.RemoveRange(recording.Credits.Where(c => c.Role == CreditRoles.Artist));
            db.ReleaseTracks.RemoveRange(recording.ReleaseTracks);
            if (tags.Artist is not null)
                recording.Credits.Add(new ArtistCredit { ArtistId = FindOrCreateArtist(tags.Artist), Role = CreditRoles.Artist });
            if (tags.Album is not null)
                recording.ReleaseTracks.Add(new ReleaseTrack
                {
                    ReleaseId = FindOrCreateRelease(tags),
                    DiscNumber = tags.DiscNumber,
                    TrackNumber = tags.TrackNumber,
                });
        }

        // Writes the batch and forgets it, so the change tracker only ever holds one batch.
        async Task SaveBatchAsync(CancellationToken token)
        {
            await db.SaveChangesAsync(token);
            db.ChangeTracker.Clear();
        }

        // Recordings that already have stored documents from files, so a scan deletes only where there is something to delete.
        var withDocuments = (await db.MetadataDocuments.AsNoTracking()
            .Where(d => d.Origin != DocumentOrigin.Applied).Select(d => d.RecordingId).Distinct().ToListAsync(ct)).ToHashSet();
        var documentProblems = new List<ScanFailure>();

        int added = 0, updated = 0, unchanged = 0, processed = 0;
        var failed = new List<ScanFailure>();
        var seen = new HashSet<string>();

        try
        {
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var uri = new Uri(file).AbsoluteUri;
                seen.Add(uri);

                try
                {
                    var info = new FileInfo(file);
                    var modified = info.LastWriteTimeUtc.Ticks;
                    known.TryGetValue(uri, out var source);

                    if (source is not null && source.SizeBytes == info.Length && source.ModifiedUtcTicks == modified
                        && source.ScannerVersion == ScannerVersion)
                    {
                        if (source.IsMissing)
                        {
                            db.MediaSources.Attach(source);
                            source.IsMissing = false;
                        }

                        // The audio did not change, but its sidecar may have appeared, changed or gone.
                        var stamp = SidecarStampOf(file);
                        if (stamp == source.SidecarStamp)
                        {
                            unchanged++;
                        }
                        else
                        {
                            var (found, problems) = ReadFileDocuments(file, null, sidecar: true, embedded: false);
                            var rows = ToRows(source.RecordingId, found, problems);
                            await ReplaceStoredAsync(db, source.RecordingId, [DocumentOrigin.Sidecar], rows, withDocuments, ct);
                            db.MediaSources.Attach(source);
                            source.SidecarStamp = stamp;
                            documentProblems.AddRange(problems.Select(p => new ScanFailure(file, p)));
                            updated++;
                        }
                    }
                    else
                    {
                        var tags = TagReader.Read(file);
                        var stamp = SidecarStampOf(file);
                        var (found, problems) = ReadFileDocuments(file, tags.EmbeddedDocument, sidecar: true, embedded: true);
                        if (source is null)
                        {
                            var recording = new Recording { Title = tags.Title };
                            Apply(recording, tags);
                            recording.Sources.Add(new MediaSource
                            {
                                Uri = uri,
                                SizeBytes = info.Length,
                                ModifiedUtcTicks = modified,
                                ScannerVersion = ScannerVersion,
                                SidecarStamp = stamp,
                            });
                            db.Recordings.Add(recording);
                            await ReplaceStoredAsync(db, recording.Id, [], ToRows(recording.Id, found, problems), withDocuments, ct);
                            added++;
                        }
                        else
                        {
                            var recording = await db.Recordings
                                .Include(r => r.Credits)
                                .Include(r => r.ReleaseTracks)
                                .SingleAsync(r => r.Id == source.RecordingId, ct);
                            Apply(recording, tags);
                            db.MediaSources.Attach(source);
                            source.SizeBytes = info.Length;
                            source.ModifiedUtcTicks = modified;
                            source.ScannerVersion = ScannerVersion;
                            source.SidecarStamp = stamp;
                            source.IsMissing = false;
                            await ReplaceStoredAsync(
                                db, source.RecordingId, [DocumentOrigin.Sidecar, DocumentOrigin.Embedded],
                                ToRows(source.RecordingId, found, problems), withDocuments, ct);
                            updated++;
                        }

                        documentProblems.AddRange(problems.Select(p => new ScanFailure(file, p)));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    failed.Add(new ScanFailure(file, ex.Message));
                }

                progress?.Report(new ScanProgress(++processed, files.Count));
                if (processed % SaveEvery == 0) await SaveBatchAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            await SaveBatchAsync(CancellationToken.None); // keep what was scanned so far
            throw;
        }

        var markedMissing = 0;
        foreach (var source in known.Values.Where(s => !seen.Contains(s.Uri) && !s.IsMissing))
        {
            db.MediaSources.Attach(source);
            source.IsMissing = true;
            markedMissing++;
        }

        await SaveBatchAsync(ct);
        return new ScanResult(added, updated, unchanged, markedMissing, failed, documentProblems);
    }

    // A cover that cannot be stored (disk full, folder locked) must not stop the scan: the song just has no cover.
    private string? SaveCover(FileTags tags)
    {
        if (tags.Cover is null) return null;
        try
        {
            return _artwork.Save(tags.Cover);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (string, string) ReleaseKey(string title, string albumArtist) =>
        (title.ToLowerInvariant(), albumArtist.ToLowerInvariant());
}
