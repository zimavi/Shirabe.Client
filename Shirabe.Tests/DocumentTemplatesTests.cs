using Shirabe.Metadata;
using Xunit;

namespace Shirabe.Tests;

public class DocumentTemplatesTests
{
    private static readonly Guid Subject = Guid.Parse("0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55");
    private static readonly DateTime Now = new(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("personal")]
    [InlineData("canonical")]
    [InlineData("community")]
    public void Every_template_follows_the_standard_from_the_start(string layer)
    {
        var document = DocumentTemplates.New(Subject, layer, Now, "Poker Face", "Lady Gaga", "The Fame");

        Assert.Empty(document.Validate());
        Assert.Equal((layer, Subject.ToString(), 1), (document.Layer, document.SubjectId, document.Revision));
    }

    [Fact]
    public void A_canonical_template_starts_with_what_the_files_tags_say()
    {
        var document = DocumentTemplates.New(Subject, "canonical", Now, " Poker Face ", "Lady Gaga", "The Fame");

        Assert.Equal("artist", document.SourceKind); // writing about your own music
        Assert.Equal("Poker Face", document.Title);
        Assert.Equal("Lady Gaga", document.Root["fields"]!["credits"]![0]!["artist"]!["name"]!.GetValue<string>());
        Assert.Equal("artist", document.Root["fields"]!["credits"]![0]!["role"]!.GetValue<string>());
        Assert.Equal("The Fame", document.Root["fields"]!["releases"]![0]!["title"]!.GetValue<string>());
    }

    [Fact]
    public void A_personal_template_starts_empty_because_it_holds_only_your_own_additions()
    {
        var document = DocumentTemplates.New(Subject, "personal", Now, "Poker Face", "Lady Gaga", "The Fame");

        Assert.Equal("user", document.SourceKind);
        Assert.Empty(document.FieldNames);
    }

    [Fact]
    public void What_the_tags_do_not_say_is_left_out_not_written_empty()
    {
        var document = DocumentTemplates.New(Subject, "canonical", Now, title: "Only a title", artist: "  ", album: null);

        Assert.Equal(["title"], document.FieldNames);
        Assert.Empty(document.Validate());
    }

    [Fact]
    public void A_community_contribution_is_by_a_user_not_by_the_artist()
    {
        Assert.Equal("user", DocumentTemplates.New(Subject, "community", Now, "x").SourceKind);
    }
}
