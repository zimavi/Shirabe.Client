using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Shirabe.Metadata;

/// <summary>Checks a document against the rules of wiki/Metadata-Standard.md, section by section.</summary>
internal sealed partial class DocumentValidator(JsonObject root)
{
    private static readonly string[] TimestampFormats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"];

    [GeneratedRegex(@"^(\d+)\.(\d+)$")]
    private static partial Regex VersionPattern();

    private readonly List<DocumentProblem> _problems = [];
    private Dictionary<string, FieldDefinition> _definitions = [];
    private bool _newerMinor;

    private string? Layer => Text(root["layer"]);

    /// <summary>Links to local files are allowed in the personal layer only (section 7.2).</summary>
    private bool AllowFileLinks => Layer == "personal";

    public IReadOnlyList<DocumentProblem> Run()
    {
        if (Text(root["format"]) != MetadataDocument.FormatName)
        {
            Error("format", $"Must be \"{MetadataDocument.FormatName}\"; this is not a Shirabe metadata document.");
            return _problems;
        }

        CheckVersion();
        CheckSubject();
        CheckLayer();
        CheckSource();
        CheckRevision();
        CheckDefinitions();
        CheckFields();
        return _problems;
    }

    private void CheckVersion()
    {
        var match = VersionPattern().Match(Text(root["version"]) ?? "");
        if (!match.Success)
        {
            Error("version", "Must be MAJOR.MINOR, for example \"0.1\".");
            return;
        }

        if (match.Groups[1].Value != "0")
            Error("version", $"Version {match.Value} is not supported; this reader knows version 0.x.");
        else if (int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) > 1)
        {
            _newerMinor = true;
            Warn("version", $"Version {match.Value} is newer than this reader (0.1): parts it does not know are kept, but not checked.");
        }
    }

    private void CheckSubject()
    {
        if (root["subject"] is not JsonObject subject)
        {
            Error("subject", "Missing: the document must say what it is about.");
            return;
        }

        var type = Text(subject["type"]);
        if (type is null || !StandardVocabulary.SubjectTypes.Contains(type))
            Error("subject.type", "Must be \"recording\" in version 0.1.");

        if (!Guid.TryParse(Text(subject["id"]), out _))
            Error("subject.id", "Must be a UUID, for example 0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55.");

        if (subject["identifiers"] is { } identifiers) CheckIdentifiers(identifiers, "subject.identifiers");
    }

    private void CheckIdentifiers(JsonNode node, string path)
    {
        if (node is not JsonArray list)
        {
            Error(path, "Must be a list.");
            return;
        }

        for (var i = 0; i < list.Count; i++)
        {
            var at = $"{path}[{i}]";
            if (list[i] is not JsonObject identifier)
            {
                Error(at, "Must be an object with a scheme and a value.");
                continue;
            }

            var scheme = Text(identifier["scheme"]);
            if (scheme is null || !(StandardVocabulary.IdentifierSchemes.Contains(scheme) || FieldKey.IsExtension(scheme)))
                Error($"{at}.scheme", "Must be isrc, musicbrainz.recording, musicbrainz.release, musicbrainz.artist, or a namespaced name.");
            if (string.IsNullOrWhiteSpace(Text(identifier["value"]))) Error($"{at}.value", "Must be some text.");
        }
    }

    private void CheckLayer()
    {
        if (Layer is not { } layer || !StandardVocabulary.Layers.Contains(layer))
            Error("layer", "Must be \"canonical\", \"community\" or \"personal\".");
    }

    private void CheckSource()
    {
        if (root["source"] is not JsonObject source)
        {
            Error("source", "Missing: the document must say where it came from.");
            return;
        }

        var kind = Text(source["kind"]);
        if (kind is null || !StandardVocabulary.SourceKinds.Contains(kind))
            Error("source.kind", "Must be \"user\", \"artist\", \"instance\" or \"import\".");

        if (!IsTimestamp(Text(source["created"])))
            Error("source.created", "Must be a UTC timestamp like 2026-11-22T18:04:00Z.");
        if (source["updated"] is { } updated && !IsTimestamp(Text(updated)))
            Error("source.updated", "Must be a UTC timestamp like 2026-11-22T18:04:00Z.");
    }

    private void CheckRevision()
    {
        if (!(root["revision"] is JsonValue v && v.TryGetValue<int>(out var revision) && revision >= 1))
            Error("revision", "Must be a whole number, 1 or more.");
    }

    private void CheckDefinitions()
    {
        if (root["definitions"] is null) return;
        if (root["definitions"] is not JsonObject definitions)
        {
            Error("definitions", "Must be an object.");
            return;
        }

        foreach (var (key, node) in definitions)
        {
            var path = $"definitions.{key}";
            if (!FieldKey.IsExtension(key))
            {
                Error(path, "Only extension fields (with a namespace, like local:mood) have definitions.");
                continue;
            }

            if (node is not JsonObject definition)
            {
                Error(path, "Must be an object.");
                continue;
            }

            if (!FieldDefinition.TryParseType(Text(definition["type"]), out var type))
            {
                Error($"{path}.type", "Must be text, number, boolean, date or link.");
                continue;
            }

            var multiple = false;
            if (definition["multiple"] is { } m)
            {
                if (m is JsonValue mv && mv.TryGetValue<bool>(out var flag)) multiple = flag;
                else Error($"{path}.multiple", "Must be true or false.");
            }

            foreach (var member in new[] { "label", "description" })
                if (definition[member] is { } text && Text(text) is null)
                    Error($"{path}.{member}", "Must be text.");

            _definitions[key] = new FieldDefinition(key, type, multiple);
        }
    }

    private void CheckFields()
    {
        if (root["fields"] is not JsonObject fields)
        {
            Error("fields", "Missing: must be an object (it may be empty).");
            return;
        }

        foreach (var (key, node) in fields)
        {
            var path = $"fields.{key}";
            if (!FieldKey.IsValid(key))
            {
                Error(path, "Not a valid field name: lowercase letters, digits, - and _, optionally after a namespace and a colon.");
                continue;
            }

            if (node is null)
            {
                Warn(path, "Has no value. Leave the field out instead of writing null.");
                continue;
            }

            if (!key.Contains(':'))
            {
                if (StandardVocabulary.CoreFields.Contains(key)) CheckCoreField(key, node, path);
                else if (!_newerMinor) Error(path, "Not a core field of this version, and fields without a namespace belong to the standard. Use a namespace, like local:" + key + ".");
                continue;
            }

            if (_definitions.TryGetValue(key, out var definition)
                && !definition.Validate(JsonSerializer.SerializeToElement(node), AllowFileLinks, out var error))
                Error(path, error!);
        }
    }

    private void CheckCoreField(string key, JsonNode node, string path)
    {
        switch (key)
        {
            case "title" or "description":
                if (string.IsNullOrWhiteSpace(Text(node))) Error(path, "Must be some text.");
                break;
            case "credits":
                EachItem(node, path, CheckCredit);
                break;
            case "releases":
                EachItem(node, path, CheckRelease);
                break;
            case "artwork":
                EachItem(node, path, (item, at) => CheckLink(item, at, needRole: true, needLabel: false));
                break;
            case "links":
                EachItem(node, path, (item, at) => CheckLink(item, at, needRole: false, needLabel: true));
                break;
            case "presentation":
                CheckTimeline(node, path);
                break;
        }
    }

    private void EachItem(JsonNode node, string path, Action<JsonNode?, string> check)
    {
        if (node is not JsonArray list)
        {
            Error(path, "Must be a list.");
            return;
        }

        for (var i = 0; i < list.Count; i++) check(list[i], $"{path}[{i}]");
    }

    private void CheckCredit(JsonNode? node, string path)
    {
        if (node is not JsonObject credit)
        {
            Error(path, "Must be an object with a role and an artist.");
            return;
        }

        var role = Text(credit["role"]);
        if (role is null || !(StandardVocabulary.CoreRoles.Contains(role) || FieldKey.IsExtension(role)))
            Error($"{path}.role", "Must be a role of the standard (artist, featured, vocals, guitar, ...) or a namespaced one.");

        if (credit["artist"] is not JsonObject artist)
        {
            Error($"{path}.artist", "Missing: must be an object with at least a name.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Text(artist["name"]))) Error($"{path}.artist.name", "Must be some text.");
        if (artist["id"] is { } id && string.IsNullOrWhiteSpace(Text(id))) Error($"{path}.artist.id", "Must be some text.");
        if (artist["identifiers"] is { } identifiers) CheckIdentifiers(identifiers, $"{path}.artist.identifiers");
        if (credit["note"] is { } note && Text(note) is null) Error($"{path}.note", "Must be text.");
    }

    private void CheckRelease(JsonNode? node, string path)
    {
        if (node is not JsonObject release)
        {
            Error(path, "Must be an object with at least a title.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Text(release["title"]))) Error($"{path}.title", "Must be some text.");
        if (release["date"] is { } date && !FieldDefinition.IsValidDate(Text(date)))
            Error($"{path}.date", "Must be a date like 1998, 1998-07 or 1998-07-14.");
        foreach (var member in new[] { "disc", "track" })
            if (release[member] is { } number && !IsWholeNumber(number, 1)) Error($"{path}.{member}", "Must be a whole number, 1 or more.");
        if (release["id"] is { } id && string.IsNullOrWhiteSpace(Text(id))) Error($"{path}.id", "Must be some text.");
    }

    private void CheckLink(JsonNode? node, string path, bool needRole, bool needLabel)
    {
        if (node is not JsonObject link)
        {
            Error(path, "Must be an object with at least a url.");
            return;
        }

        var link0 = new FieldDefinition("link", FieldType.Link);
        if (!link0.Validate(JsonSerializer.SerializeToElement(link), AllowFileLinks, out var error)) Error(path, error!);

        if (link["role"] is { } roleNode)
        {
            var role = Text(roleNode);
            if (role is null || !(StandardVocabulary.LinkRoles.Contains(role) || FieldKey.IsExtension(role)))
                Error($"{path}.role", "Must be front, back, booklet, disc, artist, banner, other, or a namespaced role.");
        }
        else if (needRole)
        {
            Error($"{path}.role", "Missing: a picture needs a role such as front.");
        }

        if (needLabel && string.IsNullOrWhiteSpace(Text(link["label"]))) Error($"{path}.label", "Missing: a link needs a label saying what it is.");
        if (link["mime"] is { } mime && Text(mime) is null) Error($"{path}.mime", "Must be text.");
        foreach (var member in new[] { "width", "height" })
            if (link[member] is { } size && !IsWholeNumber(size, 1)) Error($"{path}.{member}", "Must be a whole number, 1 or more.");

        if (link["rights"] is { } rights)
        {
            if (rights is not JsonObject rightsObject) Error($"{path}.rights", "Must be an object.");
            else
                foreach (var member in new[] { "license", "holder", "evidence" })
                    if (rightsObject[member] is { } text && Text(text) is null) Error($"{path}.rights.{member}", "Must be text.");
        }
    }

    private void CheckTimeline(JsonNode node, string path)
    {
        if (node is not JsonObject timeline)
        {
            Error(path, "Must be an object with tracks.");
            return;
        }

        double? duration = null;
        if (timeline["duration"] is { } d)
        {
            if (Number(d) is > 0 and var seconds) duration = seconds;
            else Error($"{path}.duration", "Must be a number of seconds greater than 0.");
        }

        if (timeline["tracks"] is not JsonObject tracks)
        {
            Error($"{path}.tracks", "Missing: must be an object from track name to keyframes (it may be empty).");
            return;
        }

        var ownCredits = (root["fields"]?["credits"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        foreach (var (name, keyframes) in tracks)
        {
            var trackPath = $"{path}.tracks.{name}";
            var known = StandardVocabulary.Tracks.Contains(name);
            if (!known && !FieldKey.IsExtension(name))
            {
                if (!_newerMinor) Error(trackPath, "Not a track of this version. Other tracks need a namespace, like example.com:camera.");
                continue;
            }

            if (keyframes is not JsonArray list)
            {
                Error(trackPath, "Must be a list of keyframes.");
                continue;
            }

            CheckKeyframes(name, known, list, trackPath, duration, ownCredits);
        }
    }

    private void CheckKeyframes(string track, bool known, JsonArray list, string path, double? duration, List<JsonObject> ownCredits)
    {
        double? previous = null;
        for (var i = 0; i < list.Count; i++)
        {
            var at = $"{path}[{i}]";
            if (list[i] is not JsonObject keyframe)
            {
                Error(at, "Must be an object with at, and value.");
                continue;
            }

            if (Number(keyframe["at"]) is >= 0 and var time)
            {
                if (previous is { } before && time < before)
                    Warn($"{at}.at", "Keyframes are not in order. Readers sort them, but writers must.");
                else if (previous == time)
                    Warn($"{at}.at", "Two keyframes at the same time: the later one wins.");
                if (duration is { } length && time > length)
                    Warn($"{at}.at", $"After the end ({length:0.###} s): it will never be reached.");
                previous = time;
            }
            else
            {
                Error($"{at}.at", "Must be a number of seconds, 0 or more.");
            }

            if (keyframe["transition"] is { } transition
                && !(Text(transition) is { } t && StandardVocabulary.Transitions.Contains(t)))
                Error($"{at}.transition", "Must be cut, fade or slide.");

            if (!keyframe.ContainsKey("value")) Error($"{at}.value", "Missing: use null to return to the normal display.");
            else if (known && keyframe["value"] is { } value) CheckTrackValue(track, value, $"{at}.value", ownCredits);
        }
    }

    private void CheckTrackValue(string track, JsonNode value, string path, List<JsonObject> ownCredits)
    {
        switch (track)
        {
            case "title" or "description" or "note":
                if (string.IsNullOrWhiteSpace(Text(value))) Error(path, "Must be some text, or null.");
                break;
            case "artwork":
                CheckLink(value, path, needRole: false, needLabel: false);
                break;
            case "highlight":
                CheckCredit(value, path);
                WarnIfNotInCredits(value, path, ownCredits);
                break;
            case "performers":
                if (value is not JsonArray performers) Error(path, "Must be a list of credits, or null.");
                else
                    for (var i = 0; i < performers.Count; i++)
                    {
                        CheckCredit(performers[i], $"{path}[{i}]");
                        WarnIfNotInCredits(performers[i], $"{path}[{i}]", ownCredits);
                    }

                break;
        }
    }

    /// <summary>Section 6: the same credit has the same role, and the same artist id or, without ids, the same name.</summary>
    private void WarnIfNotInCredits(JsonNode? node, string path, List<JsonObject> ownCredits)
    {
        if (node is not JsonObject credit || credit["artist"] is not JsonObject artist || ownCredits.Count == 0) return;

        var matched = ownCredits.Any(own =>
        {
            if (Text(own["role"]) != Text(credit["role"]) || own["artist"] is not JsonObject ownArtist) return false;
            var (a, b) = (Text(artist["id"]), Text(ownArtist["id"]));
            return a is not null && b is not null ? a == b : Text(artist["name"]) == Text(ownArtist["name"]);
        });
        if (!matched) Warn(path, "Matches none of the document's own credits, so it is shown only as written here.");
    }

    private static bool IsTimestamp(string? text) =>
        DateTime.TryParseExact(text, TimestampFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static double? Number(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<double>(out var number) && double.IsFinite(number) ? number : null;

    private static bool IsWholeNumber(JsonNode node, int minimum) =>
        Number(node) is { } number && number >= minimum && number == Math.Floor(number);

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;

    private void Error(string path, string message) => _problems.Add(new DocumentProblem(ProblemSeverity.Error, path, message));

    private void Warn(string path, string message) => _problems.Add(new DocumentProblem(ProblemSeverity.Warning, path, message));
}
