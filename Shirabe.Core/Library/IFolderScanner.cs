namespace Shirabe.Core.Library;

/// <summary>Fills a library from folders on disk. Specific to local libraries, hence separate from <see cref="ILibrary"/>.</summary>
public interface IFolderScanner
{
    /// <summary>
    /// Adds or updates the audio files under <paramref name="folder"/>. Safe to repeat: unchanged files are
    /// skipped, changed files update their recording in place, and files that disappeared are flagged missing,
    /// never deleted. A file that cannot be read is reported in the result and skipped.
    /// </summary>
    Task<ScanResult> ScanFolderAsync(
        string folder, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed record ScanProgress(int Processed, int Total);

public sealed record ScanFailure(string Path, string Reason);

/// <param name="Added">New recordings.</param>
/// <param name="Updated">Files that changed since the last scan; their recording was updated in place.</param>
/// <param name="Unchanged">Files skipped because size and modification time match the last scan.</param>
/// <param name="MarkedMissing">Previously known files in this folder that were not found. Their data is kept.</param>
/// <param name="DocumentProblems">Metadata documents (sidecar or embedded) that could not be read; the audio itself was scanned fine.</param>
public sealed record ScanResult(
    int Added, int Updated, int Unchanged, int MarkedMissing, IReadOnlyList<ScanFailure> Failed,
    IReadOnlyList<ScanFailure>? DocumentProblems = null);
