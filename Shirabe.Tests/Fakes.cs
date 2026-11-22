using Shirabe.Client.Services;
using Shirabe.Core.Library;

namespace Shirabe.Tests;

internal sealed class FakeLibrary : ILibrary
{
    public List<TrackSummary> Tracks { get; set; } = [];

    public Exception? Failure { get; set; }

    public int LoadCount { get; private set; }

    public Task<IReadOnlyList<TrackSummary>> GetTracksAsync(CancellationToken cancellationToken = default)
    {
        LoadCount++;
        if (Failure is not null) throw Failure;
        return Task.FromResult<IReadOnlyList<TrackSummary>>(Tracks.ToList());
    }
}

internal sealed class FakeScanner : IFolderScanner
{
    public ScanResult Result { get; set; } = new(0, 0, 0, 0, []);

    public Exception? Failure { get; set; }

    /// <summary>Called while the scan "runs", to report progress.</summary>
    public Action<IProgress<ScanProgress>?>? DuringScan { get; set; }

    /// <summary>Keep scanning until the token is cancelled.</summary>
    public bool WaitForCancellation { get; set; }

    public string? ScannedFolder { get; private set; }

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<ScanResult> ScanFolderAsync(
        string folder, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ScannedFolder = folder;
        Started.TrySetResult();
        DuringScan?.Invoke(progress);
        if (WaitForCancellation) await Task.Delay(Timeout.Infinite, cancellationToken);
        if (Failure is not null) throw Failure;
        return Result;
    }
}

internal sealed class FakeFolderPicker : IFolderPicker
{
    public string? Choice { get; set; }

    public Task<string?> PickFolderAsync() => Task.FromResult(Choice);
}
