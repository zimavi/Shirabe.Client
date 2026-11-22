namespace Shirabe.Core.Media;

public static class MediaSources
{
    /// <summary>
    /// Turns a stored media address into something playable. Only <c>file:</c> addresses exist so far;
    /// remote providers will be added here.
    /// </summary>
    /// <exception cref="UriFormatException">Not a valid address.</exception>
    /// <exception cref="NotSupportedException">No provider handles this kind of address.</exception>
    public static IMediaSource FromUri(string uri)
    {
        var address = new Uri(uri);
        return address.IsFile
            ? new LocalFileSource(address.LocalPath)
            : throw new NotSupportedException($"Cannot open '{address.Scheme}' addresses.");
    }
}
