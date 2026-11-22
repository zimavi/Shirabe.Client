using System.Security.Cryptography;

namespace Shirabe.Library;

/// <summary>
/// Cover images taken out of audio files, stored once per distinct image as &lt;sha256&gt;.&lt;ext&gt;.
/// A whole album shares one file because its tracks embed the same picture.
/// </summary>
internal sealed class ArtworkCache(string directory)
{
    public string Directory { get; } = directory;

    public string PathOf(string fileName) => System.IO.Path.Combine(Directory, fileName);

    /// <returns>The file name in the cache, or null if the data is not an image format we can show.</returns>
    /// <exception cref="IOException">The cache could not be written.</exception>
    public string? Save(byte[] image)
    {
        var extension = ExtensionOf(image);
        if (extension is null) return null;

        var fileName = Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant() + extension;
        var path = PathOf(fileName);
        if (File.Exists(path)) return fileName;

        System.IO.Directory.CreateDirectory(Directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp"; // never leave a half-written image under its real name
        File.WriteAllBytes(temporary, image);
        File.Move(temporary, path, overwrite: true);
        return fileName;
    }

    // Judged by content, not by what the tag claims: tags are often wrong about the image type.
    private static string? ExtensionOf(byte[] data) => data switch
    {
        [0xFF, 0xD8, 0xFF, ..] => ".jpg",
        [0x89, 0x50, 0x4E, 0x47, ..] => ".png",
        [(byte)'G', (byte)'I', (byte)'F', (byte)'8', ..] => ".gif",
        [(byte)'B', (byte)'M', ..] => ".bmp",
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => ".webp",
        _ => null,
    };
}
