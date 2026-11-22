using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shirabe.Metadata;

public enum FieldType
{
    Text,
    Number,
    Boolean,
    Date,
    Link,
}

/// <summary>
/// What a custom field is: its name, the type of its value and whether it holds a list. The standard calls this a
/// definition (wiki/Metadata-Standard.md, section 9.3).
/// </summary>
/// <param name="Label">Name shown to people; the key when null.</param>
public sealed partial record FieldDefinition(
    string Key, FieldType Type, bool Multiple = false, string? Label = null, string? Description = null)
{
    private static readonly string[] DateFormats =
        ["yyyy", "yyyy-MM", "yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"];

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();

    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Key : Label;

    /// <summary>The type as the standard spells it in documents: <c>text</c>, <c>number</c>, <c>boolean</c>, <c>date</c>, <c>link</c>.</summary>
    public string TypeName => Type.ToString().ToLowerInvariant();

    public static bool TryParseType(string? name, out FieldType type)
    {
        foreach (var candidate in Enum.GetValues<FieldType>())
        {
            if (candidate.ToString().ToLowerInvariant() != name) continue;
            type = candidate;
            return true;
        }

        type = default;
        return false;
    }

    /// <summary>
    /// A definition for a field that came without one (standard, section 9.3: it is read by its JSON type): text, number or
    /// yes/no, a list of one of those, or a link. Null for anything else (an object that is not a link, mixed lists).
    /// </summary>
    public static FieldDefinition? Infer(string key, JsonElement value)
    {
        static FieldType? TypeOf(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.String => FieldType.Text,
            JsonValueKind.Number => FieldType.Number,
            JsonValueKind.True or JsonValueKind.False => FieldType.Boolean,
            JsonValueKind.Object when e.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String => FieldType.Link,
            _ => null,
        };

        if (value.ValueKind != JsonValueKind.Array) return TypeOf(value) is { } single ? new FieldDefinition(key, single) : null;

        var types = value.EnumerateArray().Select(TypeOf).Distinct().ToList();
        return types is [{ } one] ? new FieldDefinition(key, one, Multiple: true) : null;
    }

    /// <summary>A date as the standard writes it: 1998, 1998-07, 1998-07-14 or a full UTC timestamp.</summary>
    public static bool IsValidDate(string? text) =>
        DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>Checks <paramref name="value"/> against this definition. Empty values are refused: remove the field instead.</summary>
    /// <param name="allowFileLinks">Links may use <c>file:</c> only in documents of the personal layer (standard, section 7.2).</param>
    /// <param name="error">Why the value is not acceptable, in words for the person who typed it.</param>
    public bool Validate(JsonElement value, bool allowFileLinks, out string? error)
    {
        error = null;
        if (Multiple)
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
            {
                error = "Needs at least one value.";
                return false;
            }

            foreach (var item in value.EnumerateArray())
                if (!ValidateOne(item, allowFileLinks, out error)) return false;
            return true;
        }

        return ValidateOne(value, allowFileLinks, out error);
    }

    private bool ValidateOne(JsonElement value, bool allowFileLinks, out string? error)
    {
        error = null;
        switch (Type)
        {
            case FieldType.Text:
                if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) return true;
                error = "Needs some text.";
                return false;

            case FieldType.Number:
                if (value.ValueKind == JsonValueKind.Number) return true;
                error = "Needs a number.";
                return false;

            case FieldType.Boolean:
                if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return true;
                error = "Needs yes or no.";
                return false;

            case FieldType.Date:
                if (value.ValueKind == JsonValueKind.String && IsValidDate(value.GetString())) return true;
                error = "Needs a date like 1998, 1998-07 or 1998-07-14.";
                return false;

            default:
                return ValidateLink(value, allowFileLinks, out error);
        }
    }

    private static bool ValidateLink(JsonElement value, bool allowFileLinks, out string? error)
    {
        error = "Needs a link starting with https://.";
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String
            || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri)
            || uri.UserInfo.Length > 0)
            return false;

        var allowed = uri.Scheme == Uri.UriSchemeHttps || (allowFileLinks && uri.Scheme == Uri.UriSchemeFile);
        if (!allowed) return false;

        if (value.TryGetProperty("sha256", out var hash)
            && (hash.ValueKind != JsonValueKind.String || !Sha256Pattern().IsMatch(hash.GetString()!)))
        {
            error = "The hash must be 64 lowercase hex characters.";
            return false;
        }

        error = null;
        return true;
    }
}
