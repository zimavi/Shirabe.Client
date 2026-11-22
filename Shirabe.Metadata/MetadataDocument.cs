using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shirabe.Metadata;

/// <summary>
/// A document of the Shirabe Metadata Format (wiki/Metadata-Standard.md): statements about one subject, from one
/// source, in one layer. It wraps the JSON as it was read, so everything this code does not understand (newer
/// members, extension fields, unknown tracks) is kept and written back unchanged, as the preservation rules of the
/// standard require. Reading does not judge: <see cref="Validate"/> reports what breaks the rules.
/// </summary>
public sealed class MetadataDocument
{
    public const string FormatName = "shirabe.metadata";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // Cyrillic and Japanese stay readable in the file
    };

    private MetadataDocument(JsonObject root) => Root = root;

    /// <summary>The whole document as read, including everything not understood.</summary>
    public JsonObject Root { get; }

    /// <summary>
    /// Reads the contents of a <c>.shirabe.json</c> file or of a sidecar: one document, or a list of documents
    /// (standard, section 13.2). Only the JSON is checked here; call <see cref="Validate"/> for the rules.
    /// </summary>
    /// <param name="error">What is wrong with the file, when this returns false.</param>
    public static bool TryParseFile(string json, out IReadOnlyList<MetadataDocument> documents, out string? error)
    {
        documents = [];
        JsonNode? node;
        try
        {
            // A member written twice is refused: the later value would silently win, hiding a bug in whatever wrote the file.
            node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false });
        }
        catch (JsonException ex)
        {
            // The parser's own text ends in "LineNumber: 0 | BytePositionInLine: 2."; say it the way a person would.
            var reason = ex.Message.Split(" Path:")[0].Split(" LineNumber:")[0].TrimEnd('.', ' ');
            error = ex.LineNumber is { } line
                ? $"Not valid JSON ({reason}) at line {line + 1}, column {(ex.BytePositionInLine ?? 0) + 1}."
                : $"Not valid JSON ({reason}).";
            return false;
        }
        catch (ArgumentException ex)
        {
            error = $"Not valid JSON: {ex.Message}";
            return false;
        }

        switch (node)
        {
            case JsonObject single:
                documents = [new MetadataDocument(single)];
                break;
            case JsonArray list when list.Count > 0 && list.All(item => item is JsonObject):
                documents = [.. list.Select(item => new MetadataDocument((JsonObject)item!))];
                break;
            default:
                error = "The file must hold one document (a JSON object) or a list of documents.";
                return false;
        }

        error = null;
        return true;
    }

    /// <summary>Builds a new document, for example to export a person's own fields.</summary>
    /// <param name="fields">Field name to value; every value must fit its definition (not checked here).</param>
    public static MetadataDocument Create(
        Guid subjectId, string layer, string sourceKind, DateTime createdUtc,
        IEnumerable<FieldDefinition> definitions, IEnumerable<KeyValuePair<string, JsonElement>> fields,
        int revision = 1, string? sourceName = null)
    {
        var source = new JsonObject { ["kind"] = sourceKind };
        if (!string.IsNullOrWhiteSpace(sourceName)) source["name"] = sourceName;
        source["created"] = createdUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        var root = new JsonObject
        {
            ["format"] = FormatName,
            ["version"] = StandardVocabulary.Version,
            ["subject"] = new JsonObject { ["type"] = "recording", ["id"] = subjectId.ToString() },
            ["layer"] = layer,
            ["source"] = source,
            ["revision"] = revision,
        };

        var defined = new JsonObject();
        foreach (var definition in definitions.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            var entry = new JsonObject { ["type"] = definition.TypeName };
            if (definition.Multiple) entry["multiple"] = true;
            if (!string.IsNullOrWhiteSpace(definition.Label)) entry["label"] = definition.Label;
            if (!string.IsNullOrWhiteSpace(definition.Description)) entry["description"] = definition.Description;
            defined[definition.Key] = entry;
        }

        if (defined.Count > 0) root["definitions"] = defined;

        var values = new JsonObject();
        foreach (var (key, value) in fields.OrderBy(f => f.Key, StringComparer.Ordinal))
            values[key] = JsonNode.Parse(value.GetRawText());
        root["fields"] = values;

        return new MetadataDocument(root);
    }

    /// <summary>Every problem with the document: <see cref="ProblemSeverity.Error"/>s break the standard.</summary>
    public IReadOnlyList<DocumentProblem> Validate() => new DocumentValidator(Root).Run();

    /// <summary>Indented JSON, with everything that was read written back unchanged.</summary>
    public string ToJson() => Root.ToJsonString(WriteOptions);

    /// <summary>One document as an object, several as a list: the two shapes a file may have (standard, section 13.2).</summary>
    public static string ToJson(IReadOnlyList<MetadataDocument> documents) =>
        documents.Count == 1
            ? documents[0].ToJson()
            : new JsonArray([.. documents.Select(d => d.Root.DeepClone())]).ToJsonString(WriteOptions);

    // What a person wants to see at a glance; null when the document does not have it (or has it wrong).

    public string? Version => Text(Root["version"]);

    public string? SubjectType => Text(Root["subject"]?["type"]);

    public string? SubjectId => Text(Root["subject"]?["id"]);

    public string? Layer => Text(Root["layer"]);

    public string? SourceKind => Text(Root["source"]?["kind"]);

    public string? SourceName => Text(Root["source"]?["name"]);

    public string? Created => Text(Root["source"]?["created"]);

    public int? Revision => Root["revision"] is JsonValue v && v.TryGetValue<int>(out var revision) ? revision : null;

    public string? Title => Text(Root["fields"]?["title"]);

    /// <summary>The definitions the document carries. Only well-formed ones: <see cref="Validate"/> reports the rest.</summary>
    public IReadOnlyDictionary<string, FieldDefinition> Definitions
    {
        get
        {
            var definitions = new Dictionary<string, FieldDefinition>();
            if (Root["definitions"] is not JsonObject written) return definitions;

            foreach (var (key, node) in written)
            {
                if (!FieldKey.IsExtension(key) || node is not JsonObject entry
                    || !FieldDefinition.TryParseType(Text(entry["type"]), out var type)) continue;

                definitions[key] = new FieldDefinition(
                    key, type, entry["multiple"] is JsonValue m && m.TryGetValue<bool>(out var multiple) && multiple,
                    Text(entry["label"]), Text(entry["description"]));
            }

            return definitions;
        }
    }

    /// <summary>The fields with a value, as JSON, in the order written.</summary>
    public IReadOnlyDictionary<string, JsonElement> Fields
    {
        get
        {
            var fields = new Dictionary<string, JsonElement>();
            if (Root["fields"] is not JsonObject written) return fields;

            foreach (var (key, node) in written)
                if (node is not null) fields[key] = JsonSerializer.SerializeToElement(node);
            return fields;
        }
    }

    /// <summary>The names of the fields the document has, in the order written.</summary>
    public IReadOnlyList<string> FieldNames => Root["fields"] is JsonObject fields ? [.. fields.Select(f => f.Key)] : [];

    public bool HasPresentation => Root["fields"]?["presentation"] is JsonObject;

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;
}
