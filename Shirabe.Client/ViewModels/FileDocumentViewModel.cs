using System.Text.Json;
using Shirabe.Core.Metadata;
using Shirabe.Metadata;

namespace Shirabe.Client.ViewModels;

/// <summary>A document that belongs to a song, as the properties dialog shows it: where it came from, whether it follows the standard, what it says.</summary>
public sealed class FileDocumentViewModel
{
    private const int MaxLineLength = 110;

    private readonly bool _canEdit;

    /// <param name="canEdit">Studio is there to hand the document to.</param>
    public FileDocumentViewModel(StoredDocument stored, bool canEdit = false)
    {
        _canEdit = canEdit;
        Stored = stored;
        Document = MetadataDocument.TryParseFile(stored.Json, out var documents, out _) && documents.Count == 1 ? documents[0] : null;
    }

    public StoredDocument Stored { get; }

    /// <summary>The document, ready to be applied; null if what was stored cannot be read as one document.</summary>
    public MetadataDocument? Document { get; }

    public bool IsValid => Stored.IsValid;

    public string Heading => Stored.Layer is { Length: > 0 } layer && layer != "invalid"
        ? $"{char.ToUpperInvariant(layer[0])}{layer[1..]} document"
        : "Document with no valid layer";

    public string Where => Stored.Origin switch
    {
        DocumentOrigin.Sidecar => "In the sidecar file beside the audio",
        DocumentOrigin.Embedded => "In the tag inside the audio file",
        _ => "Attached by you",
    };

    /// <summary>Who wrote it and which revision, as far as the document says.</summary>
    public string Details => string.Join(" · ", new[]
    {
        Document?.SourceKind is { } kind ? $"{kind}{(Document.SourceName is { } name ? $", {name}" : "")}" : null,
        Stored.Revision is { } revision ? $"revision {revision}" : null,
    }.Where(s => s is not null));

    public string Status => Stored.IsValid
        ? "Follows the standard"
        : $"Does not follow the standard ({Stored.ErrorCount} error{(Stored.ErrorCount == 1 ? "" : "s")}): shown here, but not relied on";

    /// <summary>One line per field: <c>name: value</c>, long values shortened.</summary>
    public IReadOnlyList<string> FieldLines =>
        [.. (Document?.Fields ?? new Dictionary<string, JsonElement>()).Select(f => Line(f.Key, f.Value))];

    /// <summary>Any document that can be read as one, even one that breaks the standard (that is what the editor is for), can be edited.</summary>
    public bool CanEdit => _canEdit && Document is not null;

    /// <summary>Only a valid personal document can be copied into the person's own fields.</summary>
    public bool CanCopy => Document is not null && Stored.IsValid && Stored.Layer == "personal";

    private static string Line(string key, JsonElement value)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
        return $"{key}: {(text.Length > MaxLineLength ? text[..MaxLineLength] + "..." : text)}";
    }
}
