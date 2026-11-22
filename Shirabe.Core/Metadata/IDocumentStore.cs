using Shirabe.Metadata;

namespace Shirabe.Core.Metadata;

/// <summary>Where a stored document came from.</summary>
public static class DocumentOrigin
{
    /// <summary>The <c>.shirabe.json</c> file beside the audio file (standard, section 13.2).</summary>
    public const string Sidecar = "sidecar";

    /// <summary>The <c>SHIRABE_METADATA</c> tag inside the audio file (section 13.3).</summary>
    public const string Embedded = "embedded";

    /// <summary>A document the person attached to the song themselves, from Studio.</summary>
    public const string Applied = "applied";
}

/// <summary>A document that belongs to a song, kept exactly as it was written.</summary>
/// <param name="Layer">The document's layer, or <c>invalid</c> when it does not name a valid one.</param>
/// <param name="ErrorCount">How many rules of the standard the document breaks; such documents are kept and shown, but not relied on.</param>
public sealed record StoredDocument(string Origin, string Layer, string Json, int? Revision, int ErrorCount, DateTime LoadedUtc)
{
    public bool IsValid => ErrorCount == 0;
}

public enum ApplyKind
{
    /// <summary>A field of the person's own is set (or replaced).</summary>
    Set,

    /// <summary>The document is kept with the song as another layer.</summary>
    Keep,

    /// <summary>Something in the document is not applied, and why.</summary>
    Skip,
}

/// <param name="Text">One sentence for the person to read.</param>
public sealed record ApplyOutcome(ApplyKind Kind, string Text);

/// <summary>
/// Metadata documents of songs: the ones found in or beside the audio file, and the ones a person attaches. The
/// personal layer (the person's own fields) is <see cref="IMetadataStore"/>; this is everything that came from outside.
/// </summary>
public interface IDocumentStore
{
    /// <summary>The documents of a song, the person's own layer first, then canonical and community.</summary>
    Task<IReadOnlyList<StoredDocument>> GetDocumentsAsync(Guid recordingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the song's sidecar file and embedded tag now and stores what they hold, replacing what they held when the
    /// library was last scanned. Documents the person attached are untouched.
    /// </summary>
    /// <returns>What could not be read, as sentences; empty when everything was fine.</returns>
    Task<IReadOnlyList<string>> RefreshFromFileAsync(Guid recordingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a document into the song's sidecar file (<see cref="DocumentOrigin.Sidecar"/>) or into the metadata tag inside its
    /// audio file (<see cref="DocumentOrigin.Embedded"/>). Documents of other layers already there are kept; one of the same layer
    /// is replaced and its revision goes up. Afterwards the song's stored documents are read again.
    /// Writing into the audio file is checked: the file is copied first, read back afterwards, and put back unchanged if anything
    /// but the new tag differs.
    /// </summary>
    /// <returns>What was written and where, as a sentence.</returns>
    /// <exception cref="ArgumentException">The document breaks the standard, or the origin is not sidecar or embedded.</exception>
    /// <exception cref="InvalidOperationException">The song is not a local file, what is already there cannot be read, or writing failed (the message says whether the file was left as it was).</exception>
    /// <exception cref="FileNotFoundException">The audio file was moved or deleted.</exception>
    Task<string> WriteToFileAsync(Guid recordingId, MetadataDocument document, string origin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a document to a song. A <c>personal</c> document puts its custom fields into the person's own fields; a
    /// canonical or community document is kept whole as another layer of the song. Core fields (title, credits...) of a
    /// personal document are not applied yet and are reported as skipped.
    /// </summary>
    /// <param name="dryRun">Only say what would happen; change nothing.</param>
    /// <exception cref="ArgumentException">The document breaks the standard (it must have no errors).</exception>
    /// <exception cref="InvalidOperationException">The song is not in the library.</exception>
    Task<IReadOnlyList<ApplyOutcome>> ApplyAsync(
        Guid recordingId, MetadataDocument document, bool dryRun, CancellationToken cancellationToken = default);
}
