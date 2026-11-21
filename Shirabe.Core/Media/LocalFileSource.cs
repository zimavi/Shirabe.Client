namespace Shirabe.Core.Media;

public sealed class LocalFileSource(string path) : IMediaSource
{
    public string Path { get; } = path;

    public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(
            Path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }
}
