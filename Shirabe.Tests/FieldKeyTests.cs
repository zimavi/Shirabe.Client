using Shirabe.Core.Metadata;
using Shirabe.Metadata;
using Xunit;

namespace Shirabe.Tests;

public class FieldKeyTests
{
    [Theory]
    [InlineData("local:mood")]
    [InlineData("shirabe:thing")]
    [InlineData("example.com:bpm-feel")]
    [InlineData("a.b-c.org:x_y")]
    [InlineData("local:a1")]
    public void Namespaced_names_are_valid_extension_fields(string key)
    {
        Assert.True(FieldKey.IsValid(key));
        Assert.True(FieldKey.IsExtension(key));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("description")]
    public void A_name_without_a_namespace_is_valid_but_is_a_core_field_not_an_extension(string key)
    {
        Assert.True(FieldKey.IsValid(key));
        Assert.False(FieldKey.IsExtension(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(":mood")]
    [InlineData("local:")]
    [InlineData("Local:mood")]          // lowercase only
    [InlineData("local:Mood")]
    [InlineData("local:1mood")]         // starts with a letter
    [InlineData("local:mo od")]
    [InlineData("example:mood")]        // a namespace is a domain (with a dot) unless reserved
    [InlineData("-bad.com:x")]
    [InlineData("bad-.com:x")]
    [InlineData("exa mple.com:x")]
    [InlineData("a..b.com:x")]
    [InlineData("local:mood:extra")]
    public void Malformed_names_are_rejected(string? key)
    {
        Assert.False(FieldKey.IsValid(key));
        Assert.False(FieldKey.IsExtension(key));
    }

    [Theory]
    [InlineData("Mood", "local:mood")]
    [InlineData("  Late-night   feel_ ", "local:late-night-feel")]
    [InlineData("Café Tempo", "local:cafe-tempo")]           // accents are dropped, not the letter
    [InlineData("BPM (feel)!", "local:bpm-feel")]
    [InlineData("2nd take", "local:field-2nd-take")]         // a name starts with a letter
    [InlineData("Настроение", "local:field")]                // a name that is not a-z at all still gets a valid key
    [InlineData("🎵", "local:field")]
    [InlineData("", "local:field")]
    public void A_key_is_made_from_the_name_a_person_typed(string label, string expected)
    {
        var key = FieldKey.FromLabel(label, []);

        Assert.Equal(expected, key);
        Assert.True(FieldKey.IsExtension(key));
    }

    [Fact]
    public void A_key_that_is_taken_gets_a_number()
    {
        Assert.Equal("local:mood-2", FieldKey.FromLabel("Mood", ["local:mood"]));
        Assert.Equal("local:mood-3", FieldKey.FromLabel("Mood", ["local:mood", "local:mood-2"]));
        Assert.Equal("local:field-2", FieldKey.FromLabel("Настроение", ["local:field"]));
    }

    [Fact]
    public void A_long_name_makes_a_key_that_is_still_valid()
    {
        var key = FieldKey.FromLabel(new string('a', 200) + " very long", []);

        Assert.True(FieldKey.IsExtension(key));
        Assert.True(key.Length <= "local:".Length + 40 + 4);
    }

    [Fact]
    public void A_name_can_be_64_characters_but_not_65()
    {
        Assert.True(FieldKey.IsValid("local:a" + new string('b', 63)));
        Assert.False(FieldKey.IsValid("local:a" + new string('b', 64)));
    }
}
