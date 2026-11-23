using System.Text.Json.Nodes;

namespace Shirabe.Metadata;

/// <summary>What a song shows about itself, taken from documents. A null member means "no document says": the tags fill it.</summary>
/// <param name="CoverSha256">The lowercase hex hash the cover link promises, when it has a well-formed one.</param>
public sealed record Resolved(
    string? Title, string? Artist, string? Album, string? CoverUrl, string? LinkUrl, string? LinkLabel, string? CoverSha256 = null);

/// <summary>Finds a field's value among several documents (standard, section 10.3).</summary>
public static class Resolution
{
    /// <param name="inOrder">Documents, most preferred first.</param>
    public static Resolved Resolve(IEnumerable<MetadataDocument> inOrder)
    {
        var documents = inOrder.ToList();

        var title = documents.Select(d => Text(Field(d, "title"))).FirstOrDefault(t => t is not null);

        // A list comes whole from the first document that has one, never mixed from several.
        var artists = Items(documents, "credits")
            .Where(c => Text(c["role"]) is "artist" or "featured" && Text(c["artist"]?["name"]) is not null)
            .Select(c => Text(c["artist"]!["name"])!).ToList();
        var album = Items(documents, "releases").Select(r => Text(r["title"])).FirstOrDefault(t => t is not null);
        var cover = Items(documents, "artwork").Where(a => Text(a["role"]) == "front" && Https(Text(a["url"])) is not null).FirstOrDefault();
        var link = Items(documents, "links").Select(l => (Url: Https(Text(l["url"])), Label: Text(l["label"]))).FirstOrDefault(l => l.Url is not null && l.Label is not null);

        var sha = Text(cover?["sha256"]);
        return new Resolved(title, Join(artists), album, Text(cover?["url"]), link.Url, link.Label,
            sha is { Length: 64 } && sha.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')) ? sha : null);
    }

    /// <summary>Main and featured artists in the order written: "A", "A &amp; B", "A, B &amp; C".</summary>
    private static string? Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => null,
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} & {names[^1]}",
    };

    private static JsonNode? Field(MetadataDocument document, string name) =>
        document.Root["fields"] is JsonObject fields ? fields[name] : null;

    private static List<JsonObject> Items(List<MetadataDocument> documents, string name) =>
        documents.Select(d => Field(d, name) as JsonArray).FirstOrDefault(a => a is { Count: > 0 })?.OfType<JsonObject>().ToList() ?? [];

    private static string? Text(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    // Only https is ever shown or fetched (section 7.2); a stored document that is valid already has it, this is the second lock.
    private static string? Https(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? url : null;
}
