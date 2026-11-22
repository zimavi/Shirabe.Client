using System.Text.Json;
using Shirabe.Core.Metadata;
using Shirabe.Metadata;
using Xunit;

namespace Shirabe.Tests;

public class FieldDefinitionTests
{
    private const string Sha = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static bool Valid(FieldType type, string json, bool multiple = false, bool allowFile = false) =>
        new FieldDefinition("local:x", type, multiple).Validate(Json(json), allowFile, out _);

    [Theory]
    [InlineData("\"late night\"", true)]
    [InlineData("\"\"", false)]
    [InlineData("\"   \"", false)]
    [InlineData("5", false)]
    [InlineData("null", false)]
    public void Text(string json, bool expected) => Assert.Equal(expected, Valid(FieldType.Text, json));

    [Theory]
    [InlineData("3", true)]
    [InlineData("-2.5", true)]
    [InlineData("\"3\"", false)]
    [InlineData("true", false)]
    public void Number(string json, bool expected) => Assert.Equal(expected, Valid(FieldType.Number, json));

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", true)]
    [InlineData("\"true\"", false)]
    [InlineData("1", false)]
    public void Boolean(string json, bool expected) => Assert.Equal(expected, Valid(FieldType.Boolean, json));

    [Theory]
    [InlineData("\"1998\"", true)]
    [InlineData("\"1998-07\"", true)]
    [InlineData("\"1998-07-14\"", true)]
    [InlineData("\"2026-11-22T18:04:00Z\"", true)]
    [InlineData("\"2026-11-22T18:04:00.250Z\"", true)]
    [InlineData("\"98\"", false)]
    [InlineData("\"1998-13\"", false)]
    [InlineData("\"1998-02-30\"", false)]
    [InlineData("\"July 1998\"", false)]
    [InlineData("\"2026-11-22T18:04:00\"", false)]   // a full timestamp is UTC and says so
    [InlineData("1998", false)]
    public void Date(string json, bool expected) => Assert.Equal(expected, Valid(FieldType.Date, json));

    [Theory]
    [InlineData("""{ "url": "https://example.com/a.jpg" }""", true)]
    [InlineData("""{ "url": "https://example.com/a.jpg", "label": "x", "future-member": 1 }""", true)] // unknown members are fine
    [InlineData("""{ "url": "http://example.com/a.jpg" }""", false)]
    [InlineData("""{ "url": "ftp://example.com/a.jpg" }""", false)]
    [InlineData("""{ "url": "javascript:alert(1)" }""", false)]
    [InlineData("""{ "url": "https://user:secret@example.com/a.jpg" }""", false)]
    [InlineData("""{ "url": "not a url" }""", false)]
    [InlineData("""{ "label": "no url" }""", false)]
    [InlineData("\"https://example.com/a.jpg\"", false)]   // a link is an object
    public void Link_schemes_and_shape(string json, bool expected) => Assert.Equal(expected, Valid(FieldType.Link, json));

    [Fact]
    public void A_file_link_is_allowed_only_where_the_caller_says_so()
    {
        const string json = """{ "url": "file:///C:/Music/cover.jpg" }""";

        Assert.False(Valid(FieldType.Link, json, allowFile: false));
        Assert.True(Valid(FieldType.Link, json, allowFile: true));
    }

    [Theory]
    [InlineData(Sha, true)]
    [InlineData("9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08", false)] // lowercase hex only
    [InlineData("abc", false)]
    public void A_link_hash_must_be_64_lowercase_hex_characters(string hash, bool expected)
    {
        var definition = new FieldDefinition("local:x", FieldType.Link);

        var ok = definition.Validate(Json($$"""{ "url": "https://example.com/a.jpg", "sha256": "{{hash}}" }"""), false, out var error);

        Assert.Equal(expected, ok);
        if (!expected) Assert.Contains("hash", error);
    }

    [Fact]
    public void A_list_field_takes_a_non_empty_array_of_valid_values()
    {
        Assert.True(Valid(FieldType.Text, "[\"a\", \"b\"]", multiple: true));
        Assert.False(Valid(FieldType.Text, "[]", multiple: true));                // empty means "remove the field"
        Assert.False(Valid(FieldType.Text, "\"a\"", multiple: true));             // a list field holds an array
        Assert.False(Valid(FieldType.Text, "[\"a\", 5]", multiple: true));        // every item is checked
        Assert.False(Valid(FieldType.Text, "[\"a\"]"));                           // and a single-value field holds one value
    }

    [Fact]
    public void The_error_is_a_sentence_a_person_can_act_on()
    {
        new FieldDefinition("local:released", FieldType.Date).Validate(Json("\"soon\""), false, out var error);

        Assert.Equal("Needs a date like 1998, 1998-07 or 1998-07-14.", error);
    }

    [Fact]
    public void The_display_name_is_the_label_or_else_the_key()
    {
        Assert.Equal("Mood", new FieldDefinition("local:mood", FieldType.Text, Label: "Mood").DisplayName);
        Assert.Equal("local:mood", new FieldDefinition("local:mood", FieldType.Text).DisplayName);
        Assert.Equal("local:mood", new FieldDefinition("local:mood", FieldType.Text, Label: "  ").DisplayName);
    }
}
