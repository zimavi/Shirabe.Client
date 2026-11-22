using System.Text.Json;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Metadata;
using Shirabe.Metadata;
using Xunit;

namespace Shirabe.Tests;

public class FieldEditorViewModelTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static FieldEditorViewModel New(FieldType type, bool multiple = false, string? stored = null) =>
        new(new FieldDefinition("local:x", type, multiple), stored is null ? null : Json(stored), isNewDefinition: false);

    private static string? Built(FieldEditorViewModel editor, string text)
    {
        editor.Text = text;
        Assert.True(editor.TryBuild(out var value, out var error), error);
        return value?.GetRawText();
    }

    [Fact]
    public void Typed_text_becomes_a_value_of_the_fields_type()
    {
        Assert.Equal("\"late night\"", Built(New(FieldType.Text), "  late night "));
        Assert.Equal("128.5", Built(New(FieldType.Number), "128.5"));
        Assert.Equal("128.5", Built(New(FieldType.Number), "128,5")); // a decimal comma works too
        Assert.Equal("120", Built(New(FieldType.Number), "120"));
        Assert.Equal("\"1998-07\"", Built(New(FieldType.Date), "1998-07"));
        Assert.Equal("""{"url":"https://example.com/a"}""", Built(New(FieldType.Link), "https://example.com/a"));
    }

    [Fact]
    public void A_list_field_takes_one_value_per_line_and_skips_blank_lines()
    {
        var editor = New(FieldType.Text, multiple: true);

        Assert.Equal("""["a","b","c"]""", Built(editor, "a\r\n\n  b \nc\n"));
    }

    [Fact]
    public void Nothing_typed_means_no_value()
    {
        Assert.Null(Built(New(FieldType.Text), "   "));
        Assert.Null(Built(New(FieldType.Text, multiple: true), "\n \n"));
        Assert.Null(Built(New(FieldType.Number), ""));
    }

    [Fact]
    public void A_yes_no_field_is_a_switch_that_always_has_a_value()
    {
        var editor = New(FieldType.Boolean);
        Assert.True(editor.IsBoolean);

        Assert.True(editor.TryBuild(out var off, out _));
        Assert.Equal("false", off!.Value.GetRawText());

        editor.Flag = true;
        Assert.True(editor.TryBuild(out var on, out _));
        Assert.Equal("true", on!.Value.GetRawText());
    }

    [Theory]
    [InlineData(FieldType.Number, "fast", "Needs a number.")]
    [InlineData(FieldType.Number, "NaN", "Needs a number.")]
    [InlineData(FieldType.Date, "July 1998", "Needs a date like 1998, 1998-07 or 1998-07-14.")]
    [InlineData(FieldType.Link, "example.com/a.jpg", "Needs a link starting with https://.")]
    [InlineData(FieldType.Link, "http://example.com/a.jpg", "Needs a link starting with https://.")]
    public void Text_that_does_not_fit_gives_a_sentence_and_no_value(FieldType type, string text, string expected)
    {
        var editor = New(type);
        editor.Text = text;

        Assert.False(editor.TryBuild(out var value, out var error));
        Assert.Null(value);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void One_bad_line_in_a_list_is_reported()
    {
        var editor = New(FieldType.Number, multiple: true);
        editor.Text = "1\ntwo\n3";

        Assert.False(editor.TryBuild(out _, out var error));
        Assert.Equal("Needs a number.", error);
    }

    [Fact]
    public void A_stored_value_is_shown_the_way_it_would_be_typed()
    {
        Assert.Equal("late night", New(FieldType.Text, stored: "\"late night\"").Text);
        Assert.Equal("128.5", New(FieldType.Number, stored: "128.5").Text);
        Assert.Equal("1998-07", New(FieldType.Date, stored: "\"1998-07\"").Text);
        Assert.Equal("https://example.com/a", New(FieldType.Link, stored: """{"url":"https://example.com/a","sha256":"x"}""").Text);
        Assert.Equal("a\nb", New(FieldType.Text, multiple: true, stored: """["a","b"]""").Text);
        Assert.True(New(FieldType.Boolean, stored: "true").Flag);
        Assert.False(New(FieldType.Boolean, stored: "false").Flag);
    }

    [Fact]
    public void A_field_is_changed_only_when_what_is_typed_differs()
    {
        var editor = New(FieldType.Text, stored: "\"calm\"");
        Assert.False(editor.IsChanged);

        editor.Text = "  calm ";
        Assert.False(editor.IsChanged); // only whitespace

        editor.Text = "tense";
        Assert.True(editor.IsChanged);

        var flag = New(FieldType.Boolean, stored: "true");
        Assert.False(flag.IsChanged);
        flag.Flag = false;
        Assert.True(flag.IsChanged);
    }

    [Fact]
    public void The_name_shown_is_the_label_and_a_hint_helps_with_the_format()
    {
        var editor = new FieldEditorViewModel(new FieldDefinition("local:released", FieldType.Date, Label: "Heard"), null, true);

        Assert.Equal("Heard", editor.Name);
        Assert.Contains("1998", editor.Hint);
        Assert.Equal("One per line", New(FieldType.Text, multiple: true).Hint);
    }
}
