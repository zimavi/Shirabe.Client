using Shirabe.Core.Metadata;
using Shirabe.Metadata;

namespace Shirabe.Library.Model;

public static class MetadataLayers
{
    public const string Personal = "personal";
}

/// <summary>
/// What a custom field is (name, type, list or not), shared by every song that has it. Part of the user's own
/// data: a rescan never touches it.
/// </summary>
public class MetadataDefinition
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>The field name of the standard, e.g. <c>local:mood</c>. Unique.</summary>
    public required string Key { get; set; }

    public FieldType Type { get; set; }

    public bool Multiple { get; set; }

    public string? Label { get; set; }

    public string? Description { get; set; }
}

/// <summary>
/// One field of one recording in one layer, with its value as JSON (one value, or an array when the definition is
/// a list). One row per field, so values can be queried and edited one by one. Today only the personal layer is used.
/// </summary>
public class MetadataEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid RecordingId { get; set; }

    public Recording Recording { get; set; } = null!;

    public required string Layer { get; set; }

    /// <summary>Refers to a <see cref="MetadataDefinition.Key"/> by name, not by foreign key: later layers may bring fields that have no definition.</summary>
    public required string Key { get; set; }

    public required string ValueJson { get; set; }

    public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// A metadata document of a recording kept exactly as written: found in the sidecar or the embedded tag by a scan (the scan
/// replaces those), or attached by the person (never touched by a scan). One per recording, origin and layer.
/// </summary>
public class StoredMetadataDocument
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid RecordingId { get; set; }

    public Recording Recording { get; set; } = null!;

    /// <summary>See <see cref="DocumentOrigin"/>.</summary>
    public required string Origin { get; set; }

    /// <summary>The document's layer; <c>invalid</c> when it does not name a valid one.</summary>
    public required string Layer { get; set; }

    public required string Json { get; set; }

    public int? Revision { get; set; }

    /// <summary>Rules of the standard the document breaks. Kept and shown anyway, never silently dropped.</summary>
    public int ErrorCount { get; set; }

    public DateTime LoadedUtc { get; set; }
}
