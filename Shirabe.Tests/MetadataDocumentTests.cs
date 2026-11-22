using System.Text.Json;
using System.Text.Json.Nodes;
using Shirabe.Metadata;
using Xunit;

namespace Shirabe.Tests;

/// <summary>The Shirabe Metadata Format, draft 0.1: reading, validating and writing documents.</summary>
public class MetadataDocumentTests
{
    // The two full examples of section 15 of the standard. They must always validate without a single problem.
    private const string PersonalExample = """
        {
          "format": "shirabe.metadata",
          "version": "0.1",
          "subject": { "type": "recording", "id": "0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55" },
          "layer": "personal",
          "source": { "kind": "user", "created": "2026-11-22T18:04:00Z", "updated": "2026-11-22T18:30:12Z" },
          "revision": 3,
          "definitions": { "local:mood": { "type": "text", "multiple": true, "label": "Mood" } },
          "fields": {
            "title": "Example Song (my preferred title)",
            "local:mood": [ "late night", "driving" ],
            "artwork": [
              { "url": "https://example.com/art/cover-front.jpg", "role": "front", "mime": "image/jpeg",
                "sha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08" }
            ]
          }
        }
        """;

    private const string CanonicalExample = """
        {
          "format": "shirabe.metadata",
          "version": "0.1",
          "subject": {
            "type": "recording",
            "id": "0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55",
            "identifiers": [ { "scheme": "isrc", "value": "QZ0000000001" } ]
          },
          "layer": "canonical",
          "source": { "kind": "artist", "name": "Example Artist", "created": "2026-03-02T09:00:00Z" },
          "revision": 2,
          "fields": {
            "title": "Example Song",
            "credits": [
              { "role": "artist", "artist": { "name": "Example Artist", "id": "0192f0a9-0000-7000-8000-000000000001" } },
              { "role": "featured", "artist": { "name": "Example Guest" } },
              { "role": "guitar", "artist": { "name": "Example Guitarist" }, "note": "recorded at home" },
              { "role": "mastering-engineer", "artist": { "name": "Example Engineer" } }
            ],
            "releases": [ { "title": "Example Album", "date": "2026-03", "disc": 1, "track": 4 } ],
            "artwork": [
              { "url": "https://example.com/art/cover-front.jpg", "role": "front", "mime": "image/jpeg",
                "sha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
                "rights": { "license": "CC-BY-4.0", "holder": "Example Artist" } }
            ],
            "links": [ { "url": "https://example.com/about-example-song", "label": "Why I wrote this" } ],
            "description": "Written on a night train.",
            "presentation": {
              "duration": 236.4,
              "tracks": {
                "performers": [
                  { "at": 0, "value": [ { "role": "artist", "artist": { "name": "Example Artist", "id": "0192f0a9-0000-7000-8000-000000000001" } } ] },
                  { "at": 92.5, "transition": "fade",
                    "value": [ { "role": "artist", "artist": { "name": "Example Artist", "id": "0192f0a9-0000-7000-8000-000000000001" } },
                               { "role": "featured", "artist": { "name": "Example Guest" } } ] },
                  { "at": 200, "value": [ { "role": "artist", "artist": { "name": "Example Artist", "id": "0192f0a9-0000-7000-8000-000000000001" } } ] }
                ],
                "artwork": [
                  { "at": 160, "transition": "fade",
                    "value": { "url": "https://example.com/art/cover-bridge.jpg", "sha256": "2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae" } },
                  { "at": 200, "transition": "fade", "value": null }
                ],
                "highlight": [
                  { "at": 120, "value": { "role": "guitar", "artist": { "name": "Example Guitarist" } } },
                  { "at": 150, "value": null }
                ],
                "note": [ { "at": 120, "value": "The solo is one take." }, { "at": 150, "value": null } ]
              }
            }
          }
        }
        """;

    private static MetadataDocument Parse(string json)
    {
        Assert.True(MetadataDocument.TryParseFile(json, out var documents, out var error), error);
        return Assert.Single(documents);
    }

    private static JsonObject Node(string json) => JsonNode.Parse(json)!.AsObject();

    // ---- the standard's own examples ----

    [Theory]
    [InlineData(PersonalExample)]
    [InlineData(CanonicalExample)]
    public void The_examples_of_the_standard_have_no_problems_at_all(string json)
    {
        Assert.Empty(Parse(json).Validate());
    }

    [Fact]
    public void The_summary_reads_the_main_things()
    {
        var document = Parse(CanonicalExample);

        Assert.Equal(("0.1", "recording", "0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55"), (document.Version, document.SubjectType, document.SubjectId));
        Assert.Equal(("canonical", "artist", "Example Artist", "2026-03-02T09:00:00Z"), (document.Layer, document.SourceKind, document.SourceName, document.Created));
        Assert.Equal(2, document.Revision);
        Assert.Equal("Example Song", document.Title);
        Assert.Equal(["title", "credits", "releases", "artwork", "links", "description", "presentation"], document.FieldNames);
        Assert.True(document.HasPresentation);
        Assert.False(Parse(PersonalExample).HasPresentation);
    }

    // ---- reading files ----

    [Fact]
    public void A_file_can_hold_one_document_or_a_list_of_them()
    {
        Assert.True(MetadataDocument.TryParseFile($"[{PersonalExample}, {CanonicalExample}]", out var documents, out _));

        Assert.Equal(["personal", "canonical"], documents.Select(d => d.Layer));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("[1, 2]")]
    [InlineData("[{ }, 5]")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("\"text\"")]
    [InlineData("""{ "format": "shirabe.metadata", "format": "again" }""")] // a member written twice
    public void Files_that_are_not_documents_are_refused_with_a_reason(string json)
    {
        Assert.False(MetadataDocument.TryParseFile(json, out var documents, out var error));
        Assert.Empty(documents);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    // ---- preservation (section 11) ----

    [Fact]
    public void Everything_that_is_not_understood_is_written_back_unchanged()
    {
        var original = Node(CanonicalExample);
        original["from-the-future"] = Node("""{ "a": [1, 2, { "b": null }] }""");
        original["source"]!["added-later"] = "kept";
        original["fields"]!["example.com:tempo-feel"] = "half-time";
        original["fields"]!["presentation"]!["tracks"]!["example.com:camera"] = JsonNode.Parse("""[ { "at": 1, "value": "wide" } ]""");
        original["definitions"] = Node("""{ "example.com:tempo-feel": { "type": "text", "unknown-member": 1 } }""");

        var document = Parse(original.ToJsonString());
        var again = Parse(document.ToJson());

        Assert.True(JsonNode.DeepEquals(original, again.Root), "a member was lost or changed on the way through");
        Assert.DoesNotContain(document.Validate(), p => p.Severity == ProblemSeverity.Error);
    }

    [Fact]
    public void Writing_keeps_non_ascii_text_readable_and_the_order_of_the_members()
    {
        var node = Node(PersonalExample);
        node["fields"]!["title"] = "Настроение 夜";
        var json = Parse(node.ToJsonString()).ToJson();

        Assert.Contains("Настроение 夜", json); // not \u041D...
        Assert.True(json.IndexOf("\"format\"", StringComparison.Ordinal) < json.IndexOf("\"fields\"", StringComparison.Ordinal));
    }

    // ---- every rule has a case that breaks it ----

    /// <summary>Applies a change to the canonical example and expects a problem at a path.</summary>
    private static readonly (string Name, Action<JsonObject> Change, string Path, ProblemSeverity Severity)[] Broken =
    [
        ("not our format", d => d["format"] = "something.else", "format", ProblemSeverity.Error),
        ("version not MAJOR.MINOR", d => d["version"] = "1", "version", ProblemSeverity.Error),
        ("unknown major version", d => d["version"] = "1.0", "version", ProblemSeverity.Error),
        ("newer minor version", d => d["version"] = "0.2", "version", ProblemSeverity.Warning),
        ("subject missing", d => d.Remove("subject"), "subject", ProblemSeverity.Error),
        ("subject type not supported", d => d["subject"]!["type"] = "release", "subject.type", ProblemSeverity.Error),
        ("subject id not a uuid", d => d["subject"]!["id"] = "song-1", "subject.id", ProblemSeverity.Error),
        ("identifier scheme unknown", d => d["subject"]!["identifiers"]![0]!["scheme"] = "barcode", "subject.identifiers[0].scheme", ProblemSeverity.Error),
        ("identifier value empty", d => d["subject"]!["identifiers"]![0]!["value"] = "", "subject.identifiers[0].value", ProblemSeverity.Error),
        ("layer unknown", d => d["layer"] = "private", "layer", ProblemSeverity.Error),
        ("source missing", d => d.Remove("source"), "source", ProblemSeverity.Error),
        ("source kind unknown", d => d["source"]!["kind"] = "robot", "source.kind", ProblemSeverity.Error),
        ("created is not a timestamp", d => d["source"]!["created"] = "yesterday", "source.created", ProblemSeverity.Error),
        ("created is not UTC", d => d["source"]!["created"] = "2026-03-02T09:00:00", "source.created", ProblemSeverity.Error),
        ("revision zero", d => d["revision"] = 0, "revision", ProblemSeverity.Error),
        ("revision text", d => d["revision"] = "2", "revision", ProblemSeverity.Error),
        ("revision fraction", d => d["revision"] = 1.5, "revision", ProblemSeverity.Error),
        ("fields missing", d => d.Remove("fields"), "fields", ProblemSeverity.Error),
        ("field name with capitals", d => d["fields"]!["Bad Key"] = "x", "fields.Bad Key", ProblemSeverity.Error),
        ("field without namespace that is not core", d => d["fields"]!["mood"] = "x", "fields.mood", ProblemSeverity.Error),
        ("namespace that is not a domain", d => d["fields"]!["example:mood"] = "x", "fields.example:mood", ProblemSeverity.Error),
        ("field with null", d => d["fields"]!["description"] = null, "fields.description", ProblemSeverity.Warning),
        ("definition of a core field", d => d["definitions"] = Node("""{ "title": { "type": "text" } }"""), "definitions.title", ProblemSeverity.Error),
        ("definition type unknown", d => d["definitions"] = Node("""{ "local:x": { "type": "emoji" } }"""), "definitions.local:x.type", ProblemSeverity.Error),
        ("definition multiple not a flag", d => d["definitions"] = Node("""{ "local:x": { "type": "text", "multiple": "yes" } }"""), "definitions.local:x.multiple", ProblemSeverity.Error),
        ("extension value of the wrong type", d => { d["definitions"] = Node("""{ "local:bpm": { "type": "number" } }"""); d["fields"]!["local:bpm"] = "fast"; }, "fields.local:bpm", ProblemSeverity.Error),
        ("extension list that is not a list", d => { d["definitions"] = Node("""{ "local:m": { "type": "text", "multiple": true } }"""); d["fields"]!["local:m"] = "calm"; }, "fields.local:m", ProblemSeverity.Error),
        ("title empty", d => d["fields"]!["title"] = "  ", "fields.title", ProblemSeverity.Error),
        ("credits not a list", d => d["fields"]!["credits"] = "me", "fields.credits", ProblemSeverity.Error),
        ("credit role unknown", d => d["fields"]!["credits"]![0]!["role"] = "wizard", "fields.credits[0].role", ProblemSeverity.Error),
        ("credit without artist", d => d["fields"]!["credits"]![0]!.AsObject().Remove("artist"), "fields.credits[0].artist", ProblemSeverity.Error),
        ("credit artist without name", d => d["fields"]!["credits"]![1]!["artist"]!["name"] = "", "fields.credits[1].artist.name", ProblemSeverity.Error),
        ("release without title", d => d["fields"]!["releases"]![0]!.AsObject().Remove("title"), "fields.releases[0].title", ProblemSeverity.Error),
        ("release date not a date", d => d["fields"]!["releases"]![0]!["date"] = "March", "fields.releases[0].date", ProblemSeverity.Error),
        ("release track zero", d => d["fields"]!["releases"]![0]!["track"] = 0, "fields.releases[0].track", ProblemSeverity.Error),
        ("artwork over http", d => d["fields"]!["artwork"]![0]!["url"] = "http://example.com/a.jpg", "fields.artwork[0]", ProblemSeverity.Error),
        ("artwork with a password in the url", d => d["fields"]!["artwork"]![0]!["url"] = "https://me:pw@example.com/a.jpg", "fields.artwork[0]", ProblemSeverity.Error),
        ("artwork file link outside the personal layer", d => d["fields"]!["artwork"]![0]!["url"] = "file:///C:/cover.jpg", "fields.artwork[0]", ProblemSeverity.Error),
        ("artwork without a role", d => d["fields"]!["artwork"]![0]!.AsObject().Remove("role"), "fields.artwork[0].role", ProblemSeverity.Error),
        ("artwork role unknown", d => d["fields"]!["artwork"]![0]!["role"] = "poster", "fields.artwork[0].role", ProblemSeverity.Error),
        ("artwork hash in capitals", d => d["fields"]!["artwork"]![0]!["sha256"] = "ABC", "fields.artwork[0]", ProblemSeverity.Error),
        ("artwork size zero", d => d["fields"]!["artwork"]![0]!["width"] = 0, "fields.artwork[0].width", ProblemSeverity.Error),
        ("link without a label", d => d["fields"]!["links"]![0]!.AsObject().Remove("label"), "fields.links[0].label", ProblemSeverity.Error),
        ("presentation not an object", d => d["fields"]!["presentation"] = 5, "fields.presentation", ProblemSeverity.Error),
        ("presentation duration zero", d => d["fields"]!["presentation"]!["duration"] = 0, "fields.presentation.duration", ProblemSeverity.Error),
        ("presentation without tracks", d => d["fields"]!["presentation"]!.AsObject().Remove("tracks"), "fields.presentation.tracks", ProblemSeverity.Error),
        ("track unknown", d => d["fields"]!["presentation"]!["tracks"]!["zoom"] = new JsonArray(), "fields.presentation.tracks.zoom", ProblemSeverity.Error),
        ("track not a list", d => d["fields"]!["presentation"]!["tracks"]!["note"] = "hi", "fields.presentation.tracks.note", ProblemSeverity.Error),
        ("keyframe before zero", d => d["fields"]!["presentation"]!["tracks"]!["note"]![0]!["at"] = -1, "fields.presentation.tracks.note[0].at", ProblemSeverity.Error),
        ("keyframe time not a number", d => d["fields"]!["presentation"]!["tracks"]!["note"]![0]!["at"] = "1:00", "fields.presentation.tracks.note[0].at", ProblemSeverity.Error),
        ("keyframe transition unknown", d => d["fields"]!["presentation"]!["tracks"]!["note"]![0]!["transition"] = "wipe", "fields.presentation.tracks.note[0].transition", ProblemSeverity.Error),
        ("keyframe without value", d => d["fields"]!["presentation"]!["tracks"]!["note"]![0]!.AsObject().Remove("value"), "fields.presentation.tracks.note[0].value", ProblemSeverity.Error),
        ("keyframes out of order", d => d["fields"]!["presentation"]!["tracks"]!["note"]![1]!["at"] = 100, "fields.presentation.tracks.note[1].at", ProblemSeverity.Warning),
        ("two keyframes at one time", d => d["fields"]!["presentation"]!["tracks"]!["note"]![1]!["at"] = 120, "fields.presentation.tracks.note[1].at", ProblemSeverity.Warning),
        ("keyframe after the end", d => d["fields"]!["presentation"]!["tracks"]!["note"]![1]!["at"] = 999, "fields.presentation.tracks.note[1].at", ProblemSeverity.Warning),
        ("title track value not text", d => d["fields"]!["presentation"]!["tracks"]!["title"] = JsonNode.Parse("""[ { "at": 0, "value": 5 } ]"""), "fields.presentation.tracks.title[0].value", ProblemSeverity.Error),
        ("performers not a list", d => d["fields"]!["presentation"]!["tracks"]!["performers"]![0]!["value"] = "Example Artist", "fields.presentation.tracks.performers[0].value", ProblemSeverity.Error),
        ("performer not among the credits", d => d["fields"]!["presentation"]!["tracks"]!["performers"]![0]!["value"]![0]!["artist"]!["id"] = "someone-else", "fields.presentation.tracks.performers[0].value[0]", ProblemSeverity.Warning),
        ("highlight not among the credits", d => d["fields"]!["presentation"]!["tracks"]!["highlight"]![0]!["value"]!["role"] = "drums", "fields.presentation.tracks.highlight[0].value", ProblemSeverity.Warning),
    ];

    [Fact]
    public void Every_rule_of_the_standard_is_enforced()
    {
        var failures = new List<string>();
        foreach (var (name, change, path, severity) in Broken)
        {
            var root = Node(CanonicalExample);
            change(root);
            var problems = Parse(root.ToJsonString()).Validate();

            if (!problems.Any(p => p.Path == path && p.Severity == severity))
                failures.Add($"{name}: expected {severity} at {path}, got [{string.Join("; ", problems.Select(p => p.ToString()))}]");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Things_the_standard_allows_are_not_flagged()
    {
        var failures = new List<string>();
        (string Name, Action<JsonObject> Change)[] fine =
        [
            ("a namespaced role", d => d["fields"]!["credits"]![1]!["role"] = "example.com:theremin"),
            ("a namespaced identifier scheme", d => d["subject"]!["identifiers"]![0]!["scheme"] = "example.com:catalogue-no"),
            ("a namespaced artwork role", d => d["fields"]!["artwork"]![0]!["role"] = "example.com:poster"),
            ("an extension field with no definition", d => d["fields"]!["example.com:anything"] = Node("""{ "free": "form" }""")),
            ("an unknown track with a namespace", d => d["fields"]!["presentation"]!["tracks"]!["example.com:camera"] = JsonNode.Parse("""[ { "at": 1, "value": "wide" } ]""")),
            ("an empty list of performers", d => d["fields"]!["presentation"]!["tracks"]!["performers"]![0]!["value"] = new JsonArray()),
            ("a presentation without a duration", d => d["fields"]!["presentation"]!.AsObject().Remove("duration")),
            ("a file link in a personal document", d => { d["layer"] = "personal"; d["fields"]!["artwork"]![0]!["url"] = "file:///C:/cover.jpg"; }),
            ("an empty presentation", d => d["fields"]!["presentation"] = Node("""{ "tracks": { } }""")),
        ];

        foreach (var (name, change) in fine)
        {
            var root = Node(CanonicalExample);
            change(root);
            var errors = Parse(root.ToJsonString()).Validate().Where(p => p.Severity == ProblemSeverity.Error).ToList();
            if (errors.Count > 0) failures.Add($"{name}: {string.Join("; ", errors)}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void A_newer_minor_version_does_not_turn_unknown_names_into_errors()
    {
        var root = Node(CanonicalExample);
        root["version"] = "0.9";
        root["fields"]!["lyrics"] = "words";
        root["fields"]!["presentation"]!["tracks"]!["annotation"] = JsonNode.Parse("[]");

        var problems = Parse(root.ToJsonString()).Validate();

        Assert.DoesNotContain(problems, p => p.Severity == ProblemSeverity.Error);
        Assert.Contains(problems, p => p.Path == "version" && p.Severity == ProblemSeverity.Warning);
    }

    [Fact]
    public void A_document_that_is_not_ours_is_not_checked_further()
    {
        var problems = Parse("""{ "name": "some other json" }""").Validate();

        Assert.Equal("format", Assert.Single(problems).Path);
    }

    [Fact]
    public void Problems_read_as_sentences()
    {
        var root = Node(CanonicalExample);
        root["layer"] = "private";

        var problem = Assert.Single(Parse(root.ToJsonString()).Validate());

        Assert.Equal("Error: layer: Must be \"canonical\", \"community\" or \"personal\".", problem.ToString());
    }

    // ---- creating documents ----

    [Fact]
    public void A_created_document_is_valid_and_survives_a_trip_through_a_file()
    {
        var id = Guid.Parse("0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55");
        var mood = new FieldDefinition("local:mood", FieldType.Text, Multiple: true, Label: "Настроение");
        var heard = new FieldDefinition("local:heard", FieldType.Date);

        var document = MetadataDocument.Create(
            id, "personal", "user", new DateTime(2026, 11, 22, 20, 4, 0, DateTimeKind.Utc),
            [mood, heard],
            [
                KeyValuePair.Create("local:mood", JsonDocument.Parse("""[ "calm", "late" ]""").RootElement),
                KeyValuePair.Create("local:heard", JsonDocument.Parse("\"2026-11\"").RootElement),
            ]);

        Assert.Empty(document.Validate());
        var again = Parse(document.ToJson());
        Assert.Empty(again.Validate());
        Assert.Equal(("personal", "user", "2026-11-22T20:04:00Z", 1), (again.Layer, again.SourceKind, again.Created, again.Revision));
        Assert.Equal(id.ToString(), again.SubjectId);
        Assert.Equal(["local:heard", "local:mood"], again.FieldNames); // sorted, so exports are stable
        Assert.Equal("Настроение", again.Root["definitions"]!["local:mood"]!["label"]!.GetValue<string>());
        Assert.True(again.Root["definitions"]!["local:mood"]!["multiple"]!.GetValue<bool>());
        Assert.Null(again.Root["definitions"]!["local:heard"]!["multiple"]); // only written when true
    }

    [Fact]
    public void A_created_document_converts_local_times_to_utc_and_leaves_out_what_is_empty()
    {
        var local = new DateTime(2026, 11, 22, 20, 4, 0, DateTimeKind.Utc).ToLocalTime();

        var document = MetadataDocument.Create(Guid.NewGuid(), "personal", "user", local, [], []);

        Assert.Equal("2026-11-22T20:04:00Z", document.Created);
        Assert.Null(document.Root["definitions"]); // nothing defined, nothing written
        Assert.Empty(document.FieldNames);
        Assert.Empty(document.Validate());
    }
}
