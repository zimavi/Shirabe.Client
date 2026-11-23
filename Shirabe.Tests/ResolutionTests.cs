using Shirabe.Metadata;
using Xunit;

namespace Shirabe.Tests;

public sealed class ResolutionTests
{
    private static MetadataDocument Doc(string fields)
    {
        var json = $$"""
            { "format": "shirabe.metadata", "version": "0.1",
              "subject": { "type": "recording", "id": "0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55" },
              "layer": "canonical", "source": { "kind": "artist", "created": "2026-03-02T09:00:00Z" },
              "revision": 1, "fields": { {{fields}} } }
            """;
        Assert.True(MetadataDocument.TryParseFile(json, out var documents, out _));
        return documents.Single();
    }

    [Fact]
    public void The_first_document_with_a_value_supplies_each_field_on_its_own()
    {
        var resolved = Resolution.Resolve([
            Doc("""
                "title": "First",
                "releases": [ { "title": "Album One" } ]
                """),
            Doc("""
                "title": "Second",
                "credits": [ { "role": "artist", "artist": { "name": "Someone" } } ],
                "releases": [ { "title": "Album Two" } ]
                """)]);

        Assert.Equal(("First", "Someone", "Album One"), (resolved.Title, resolved.Artist, resolved.Album));
    }

    [Fact]
    public void An_empty_value_counts_as_missing_and_a_list_is_taken_whole_from_one_document()
    {
        var resolved = Resolution.Resolve([
            Doc(""" "title": "  ", "credits": [ { "role": "guitar", "artist": { "name": "Guitarist" } } ] """),
            Doc(""" "title": "Real", "credits": [ { "role": "artist", "artist": { "name": "Never used" } } ] """)]);

        Assert.Equal("Real", resolved.Title);
        Assert.Null(resolved.Artist); // the first credits list has no main artist, and lists are not mixed
    }

    [Fact]
    public void Several_main_artists_are_joined_and_other_roles_are_not_shown_as_the_artist()
    {
        var resolved = Resolution.Resolve([Doc("""
            "credits": [ { "role": "artist", "artist": { "name": "A" } }, { "role": "featured", "artist": { "name": "F" } }, { "role": "artist", "artist": { "name": "B" } } ]
            """)]);

        Assert.Equal("A, B", resolved.Artist);
    }

    [Fact]
    public void The_cover_is_the_front_one_and_a_link_needs_its_label_and_https()
    {
        var resolved = Resolution.Resolve([Doc("""
            "artwork": [ { "url": "https://example.com/back.jpg", "role": "back" }, { "url": "http://example.com/plain.jpg", "role": "front" }, { "url": "https://example.com/front.jpg", "role": "front" } ],
            "links": [ { "url": "http://example.com/plain", "label": "Plain" }, { "url": "https://example.com/story", "label": "Story" } ]
            """)]);

        Assert.Equal("https://example.com/front.jpg", resolved.CoverUrl);
        Assert.Equal(("https://example.com/story", "Story"), (resolved.LinkUrl, resolved.LinkLabel));
    }

    [Fact]
    public void No_documents_say_nothing()
    {
        Assert.Equal(new Resolved(null, null, null, null, null, null), Resolution.Resolve([]));
    }
}
