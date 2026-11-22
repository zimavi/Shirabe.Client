using System.Text.Json;

namespace Shirabe.Metadata;

/// <summary>Starting points for new documents: valid from the first keystroke, so an author only has to fill in and change.</summary>
public static class DocumentTemplates
{
    /// <summary>
    /// A new document about a recording. A personal one starts empty (it holds your own additions). A canonical or community
    /// one starts with what is known from the file's tags: the title, the artist as a credit, the album as a release.
    /// </summary>
    /// <param name="layer">"personal", "canonical" or "community".</param>
    public static MetadataDocument New(Guid subjectId, string layer, DateTime nowUtc, string? title = null, string? artist = null, string? album = null)
    {
        var fields = new List<KeyValuePair<string, JsonElement>>();
        if (layer != "personal")
        {
            if (!string.IsNullOrWhiteSpace(title)) fields.Add(Field("title", title.Trim()));
            if (!string.IsNullOrWhiteSpace(artist))
                fields.Add(Field("credits", new[] { new { role = "artist", artist = new { name = artist.Trim() } } }));
            if (!string.IsNullOrWhiteSpace(album)) fields.Add(Field("releases", new[] { new { title = album.Trim() } }));
        }

        // Someone writing about their own music is the artist; a contribution to someone else's is a user's.
        var kind = layer == "canonical" ? "artist" : "user";
        return MetadataDocument.Create(subjectId, layer, kind, nowUtc, [], fields);
    }

    private static KeyValuePair<string, JsonElement> Field(string name, object value) =>
        KeyValuePair.Create(name, JsonSerializer.SerializeToElement(value));
}
