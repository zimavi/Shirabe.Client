using Avalonia.Media.Imaging;

namespace Shirabe.Client.Services;

/// <summary>
/// Loads cover images from the artwork cache, decoded at the size they are shown at (a 3000 px cover would
/// otherwise cost tens of megabytes per row). UI thread only: it is called from controls and not thread safe.
/// </summary>
public static class ArtworkBitmaps
{
    private const int MaxCached = 300;

    private static readonly Dictionary<(string Path, int Width), Bitmap?> Cache = [];

    /// <returns>The image, or null when there is no path, or the file is missing or not a readable image.</returns>
    public static Bitmap? Load(string? path, int width)
    {
        if (path is null) return null;

        var key = (path, width);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        Bitmap? bitmap = null;
        try
        {
            using var stream = File.OpenRead(path);
            bitmap = Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // A cover that is gone or damaged just means "no cover".
        }

        // Simple bound on memory. Bitmaps still shown stay alive through their controls.
        if (Cache.Count >= MaxCached) Cache.Clear();
        Cache[key] = bitmap;
        return bitmap;
    }
}
