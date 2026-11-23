using System.Security.Cryptography;

namespace Shirabe.Library;

/// <summary>
/// Cover images, stored once per distinct image as &lt;sha256&gt;.&lt;ext&gt;: those taken out of audio files (a whole album
/// shares one file because its tracks embed the same picture) and those downloaded from links in metadata documents
/// (named by the hash the document promises, or by a hash of the address when it promises none).
/// </summary>
internal sealed class ArtworkCache(string directory)
{
    private static readonly string[] Extensions = [".jpg", ".png", ".gif", ".bmp", ".webp"];

    public string Directory { get; } = directory;

    public string PathOf(string fileName) => System.IO.Path.Combine(Directory, fileName);

    /// <returns>The file name of what was stored under <paramref name="name"/>, or null if nothing was.</returns>
    public string? Find(string name) => Extensions.Select(e => name + e).FirstOrDefault(f => File.Exists(PathOf(f)));

    /// <param name="name">What to call it (without extension); by default the image's own hash.</param>
    /// <returns>The file name in the cache, or null if the data is not an image format we can show.</returns>
    /// <exception cref="IOException">The cache could not be written.</exception>
    public string? Save(byte[] image, string? name = null)
    {
        var extension = ExtensionOf(image);
        if (extension is null) return null;

        var fileName = (name ?? Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant()) + extension;
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
