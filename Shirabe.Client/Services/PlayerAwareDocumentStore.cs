using Shirabe.Client.ViewModels;
using Shirabe.Core.Metadata;
using Shirabe.Metadata;

namespace Shirabe.Client.Services;

/// <summary>
/// The document store Studio uses: the same as the library's, except that writing into an audio file first lets the player
/// let go of that file if it is the song that is loaded (a file open for playing cannot be written to).
/// </summary>
public sealed class PlayerAwareDocumentStore(IDocumentStore inner, PlayerViewModel player) : IDocumentStore
{
    public Task<IReadOnlyList<StoredDocument>> GetDocumentsAsync(Guid recordingId, CancellationToken cancellationToken = default) =>
        inner.GetDocumentsAsync(recordingId, cancellationToken);

    public Task<IReadOnlyList<string>> RefreshFromFileAsync(Guid recordingId, CancellationToken cancellationToken = default) =>
        inner.RefreshFromFileAsync(recordingId, cancellationToken);

    public Task<string> WriteToFileAsync(Guid recordingId, MetadataDocument document, string origin, CancellationToken cancellationToken = default) =>
        // A sidecar is another file, which the player never opens.
        origin == DocumentOrigin.Embedded
            ? player.WithFileReleasedAsync(recordingId, () => inner.WriteToFileAsync(recordingId, document, origin, cancellationToken))
            : inner.WriteToFileAsync(recordingId, document, origin, cancellationToken);

    public Task<IReadOnlyList<ApplyOutcome>> ApplyAsync(Guid recordingId, MetadataDocument document, bool dryRun, CancellationToken cancellationToken = default) =>
        inner.ApplyAsync(recordingId, document, dryRun, cancellationToken);
}
