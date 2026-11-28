using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;

using Shirabe.Core.Library;
using Shirabe.Library.Model;

namespace Shirabe.Library;

public sealed partial class LocalLibrary
{
    /// <summary>An album while its songs are being collected. A song's year is what its document says, to fill a gap.</summary>
    private sealed record AlbumDraft(Guid Id, string TagTitle, string Title, string? Artist, int? Year)
    {
        public List<(AlbumTrack Song, int? Year)> Songs { get; } = [];

        public bool IsNamed(string? name) => Same(name, Title) || Same(name, TagTitle);
    }

    public async Task<IReadOnlyList<AlbumSummary>> GetAlbumsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = new LibraryDbContext(_options);
        var documents = await ResolveDocumentsAsync(db, cancellationToken);
        // The same list the Songs page shows, so documents and missing files are treated alike on both.
        var tracks = await LoadTracksAsync(db, documents, cancellationToken);
        var playable = tracks.ToDictionary(t => t.RecordingId);

        var places = await db.ReleaseTracks
            .AsNoTracking()
            .Select(t => new
            {
                t.ReleaseId,
                t.RecordingId,
                t.DiscNumber,
                t.TrackNumber,
                t.Release.Title,
                t.Release.Year,
                Artist = t.Release.Credits.Where(c => c.Role == CreditRoles.AlbumArtist).OrderBy(c => c.SortOrder)
                    .Select(c => c.Artist.Name).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        // The releases of the tags come first. A document only fills the gaps of a song whose release it names.
        var albums = places.Where(p => playable.ContainsKey(p.RecordingId)).GroupBy(p => p.ReleaseId).Select(release =>
        {
            var first = release.First();
            var names = release.Select(p => playable[p.RecordingId].Album).Distinct().ToList();
            var album = new AlbumDraft(release.Key, first.Title, names is [{ } agreed] ? agreed : first.Title,
                first.Artist, first.Year);
            foreach (var place in release)
            {
                var said = documents.GetValueOrDefault(place.RecordingId);
                if (!album.IsNamed(said?.Album)) said = null;
                album.Songs.Add((new AlbumTrack(playable[place.RecordingId], place.DiscNumber ?? said?.Disc,
                    place.TrackNumber ?? said?.Track), said?.Year));
            }

            return album;
        }).ToList();

        // Songs without an album tag belong to the album their documents name, by name and album artist.
        var onRelease = places.Select(p => p.RecordingId).ToHashSet();
        foreach (var track in tracks)
        {
            if (onRelease.Contains(track.RecordingId)
                || !documents.TryGetValue(track.RecordingId, out var said) || said.Album is not { } name) continue;

            var artist = said.AlbumArtist ?? track.Artist;
            var album = albums.Find(a => a.IsNamed(name) && Same(a.Artist, artist));
            if (album is null) albums.Add(album = new AlbumDraft(DerivedId(name, artist), name, name, artist, null));
            album.Songs.Add((new AlbumTrack(track, said.Disc, said.Track), said.Year));
        }

        return
        [
            .. albums.Select(album =>
            {
                var songs = album.Songs
                    .OrderBy(s => s.Song.DiscNumber ?? 1)
                    .ThenBy(s => s.Song.TrackNumber ?? int.MaxValue)
                    .ThenBy(s => s.Song.Track.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(s => s.Song.Track.RecordingId)
                    .ToList();
                return new AlbumSummary(album.Id, album.Title, album.Artist,
                    album.Year ?? songs.Select(s => s.Year).FirstOrDefault(year => year is not null),
                    [.. songs.Select(s => s.Song)]);
            }),
        ];
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // An album that only documents name has no row, so no id of its own: the same name and artist give the same one again.
    private static Guid DerivedId(string name, string? artist) => new(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{name.ToUpperInvariant()}\0{artist?.ToUpperInvariant()}"))
            .AsSpan(0, 16));
}