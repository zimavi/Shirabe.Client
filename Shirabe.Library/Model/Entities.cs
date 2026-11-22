namespace Shirabe.Library.Model;

// Identity is a GUID v7 generated here, never a name or a path: titles change and files move.
// Everything below is "scanned" data that a rescan may rewrite. User edits and personal data (ratings,
// tags, playlists) will live in their own tables, keyed by these ids, so a rescan can never touch them.

public class Artist
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }
}

/// <summary>An album, EP, single... What was published, as opposed to one recording of a song.</summary>
public class Release
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Title { get; set; }

    public int? Year { get; set; }

    public List<ReleaseTrack> Tracks { get; } = [];

    public List<ArtistCredit> Credits { get; } = [];
}

/// <summary>One performance/mix of a song. A file on disk is one source of a recording.</summary>
public class Recording
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Title { get; set; }

    public TimeSpan? Duration { get; set; }

    /// <summary>Name of the cover image in the artwork cache (&lt;sha256&gt;.jpg), taken from the file's embedded picture.</summary>
    public string? ArtworkFile { get; set; }

    public List<MediaSource> Sources { get; } = [];

    public List<ArtistCredit> Credits { get; } = [];

    public List<ReleaseTrack> ReleaseTracks { get; } = [];
}

/// <summary>Where the audio of a recording can be opened from. Today always a local file.</summary>
public class MediaSource
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid RecordingId { get; set; }

    public Recording Recording { get; set; } = null!;

    /// <summary>A URI, not a path, so remote providers need no schema change. For files: file:///C:/Music/a.flac.</summary>
    public required string Uri { get; set; }

    /// <summary>Size and modification time at the last scan, used to skip unchanged files.</summary>
    public long SizeBytes { get; set; }

    public long ModifiedUtcTicks { get; set; }

    /// <summary>The file was not found by the last scan of its folder. Rows are never deleted: personal data hangs off them.</summary>
    public bool IsMissing { get; set; }

    /// <summary>
    /// Which version of the scanner last read this file. Files read by an older version are read again on the
    /// next scan even if they did not change, so new kinds of data (covers) reach existing libraries.
    /// </summary>
    public int ScannerVersion { get; set; }

    /// <summary>
    /// Size and modification time of the sidecar file (<c>song.flac.shirabe.json</c>) at the last scan, or null when there was
    /// none. A change of this (or its disappearance) makes the scan read the sidecar again even if the audio is unchanged.
    /// </summary>
    public string? SidecarStamp { get; set; }
}

/// <summary>A recording's place on a release.</summary>
public class ReleaseTrack
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ReleaseId { get; set; }

    public Release Release { get; set; } = null!;

    public Guid RecordingId { get; set; }

    public Recording Recording { get; set; } = null!;

    public int? DiscNumber { get; set; }

    public int? TrackNumber { get; set; }
}

public static class CreditRoles
{
    public const string Artist = "artist";
    public const string AlbumArtist = "album-artist";
}

/// <summary>Structured credit: who did what on a recording or a release (exactly one of the two).</summary>
public class ArtistCredit
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ArtistId { get; set; }

    public Artist Artist { get; set; } = null!;

    public Guid? RecordingId { get; set; }

    public Guid? ReleaseId { get; set; }

    /// <summary>"artist" and "album-artist" for now; instrument and production roles come with credit editing.</summary>
    public required string Role { get; set; }

    public int SortOrder { get; set; }
}
