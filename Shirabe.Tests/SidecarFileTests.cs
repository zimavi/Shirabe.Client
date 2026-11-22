using System.Text.Json;
using System.Text.Json.Nodes;
using Shirabe.Metadata;
using Xunit;

namespace Shirabe.Tests;

public class SidecarFileTests
{
    private static readonly Guid Song = Guid.Parse("0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55");

    private static MetadataDocument Personal(string created = "2026-11-22T20:04:00Z", string mood = "calm") =>
        MetadataDocument.Create(
            Song, "personal", "user", DateTime.Parse(created, null, System.Globalization.DateTimeStyles.AdjustToUniversal),
            [new FieldDefinition("local:mood", FieldType.Text)],
            [KeyValuePair.Create("local:mood", JsonDocument.Parse($"\"{mood}\"").RootElement)]);

    private const string ArtistDocument = """
        {
          "format": "shirabe.metadata", "version": "0.1",
          "subject": { "type": "recording", "id": "0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55" },
          "layer": "canonical",
          "source": { "kind": "artist", "name": "Example Artist", "created": "2026-03-02T09:00:00Z", "from-the-future": true },
          "revision": 4,
          "fields": { "title": "Example Song", "example.com:tempo-feel": "half-time" }
        }
        """;

    private static IReadOnlyList<MetadataDocument> Read(string json)
    {
        Assert.True(MetadataDocument.TryParseFile(json, out var documents, out var error), error);
        return documents;
    }

    [Theory]
    [InlineData(@"C:\Music\song.flac", @"C:\Music\song.flac.shirabe.json")]
    [InlineData("/music/song.mp3", "/music/song.mp3.shirabe.json")]
    public void The_sidecar_keeps_the_audio_extension_so_two_formats_do_not_share_one(string audio, string sidecar)
    {
        Assert.Equal(sidecar, SidecarFile.PathFor(audio));
    }

    [Fact]
    public void Without_a_sidecar_the_file_is_just_the_document()
    {
        Assert.True(SidecarFile.TryMerge(null, Personal(), out var json, out var error));

        Assert.Null(error);
        var document = Assert.Single(Read(json));
        Assert.Equal("personal", document.Layer);
        Assert.Equal(1, document.Revision);
        Assert.StartsWith("{", json.TrimStart()); // one document is an object, not a list
    }

    [Fact]
    public void A_blank_sidecar_counts_as_none()
    {
        Assert.True(SidecarFile.TryMerge("  \n", Personal(), out var json, out _));

        Assert.Single(Read(json));
    }

    [Fact]
    public void An_artists_document_in_the_sidecar_survives_saving_personal_notes()
    {
        Assert.True(SidecarFile.TryMerge(ArtistDocument, Personal(), out var json, out _));

        var documents = Read(json);
        Assert.Equal(["canonical", "personal"], documents.Select(d => d.Layer));
        Assert.StartsWith("[", json.TrimStart()); // two documents are a list
        // The artist's document is written back exactly as it was, including what this code does not understand.
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(ArtistDocument), documents[0].Root));
    }

    [Fact]
    public void A_second_save_replaces_the_personal_document_and_counts_the_revision_up()
    {
        Assert.True(SidecarFile.TryMerge(ArtistDocument, Personal("2026-11-22T20:04:00Z", "calm"), out var first, out _));

        Assert.True(SidecarFile.TryMerge(first, Personal("2026-11-25T08:00:00Z", "tense"), out var second, out _));

        var documents = Read(second);
        Assert.Equal(2, documents.Count); // replaced, not added
        var personal = documents.Single(d => d.Layer == "personal");
        Assert.Equal(2, personal.Revision);
        Assert.Equal("2026-11-22T20:04:00Z", personal.Created);                         // as old as its first version
        Assert.Equal("2026-11-25T08:00:00Z", personal.Root["source"]!["updated"]!.GetValue<string>());
        Assert.Equal("tense", personal.Root["fields"]!["local:mood"]!.GetValue<string>());
        Assert.Equal(4, documents.Single(d => d.Layer == "canonical").Revision);       // the artist's revision is not touched
        Assert.Empty(personal.Validate());
    }

    [Fact]
    public void A_personal_document_for_another_subject_is_replaced_too_because_the_file_decides()
    {
        var other = JsonNode.Parse(Personal().ToJson())!.AsObject();
        other["subject"]!["id"] = "11111111-2222-7333-8444-555555555555";

        Assert.True(SidecarFile.TryMerge(other.ToJsonString(), Personal(), out var json, out _));

        Assert.Single(Read(json));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("\"text\"")]
    public void A_sidecar_that_cannot_be_read_is_left_alone(string existing)
    {
        Assert.False(SidecarFile.TryMerge(existing, Personal(), out var json, out var error));

        Assert.Equal("", json);
        Assert.Contains("left alone", error);
    }
}
