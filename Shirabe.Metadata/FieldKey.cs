using System.Text.RegularExpressions;

namespace Shirabe.Metadata;

/// <summary>
/// Field names of the Shirabe Metadata Format: <c>[namespace:]name</c>. No namespace means a core field of the
/// standard; <c>local</c> and <c>shirabe</c> are reserved; any other namespace must be a domain name
/// (<c>example.com:bpm-feel</c>). See wiki/Metadata-Standard.md, section 9.1.
/// </summary>
public static partial class FieldKey
{
    public const string LocalNamespace = "local";

    [GeneratedRegex("^[a-z][a-z0-9_-]{0,63}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$")]
    private static partial Regex DomainPattern();

    /// <summary>Whether <paramref name="key"/> is a well-formed field name, with or without a namespace.</summary>
    public static bool IsValid(string? key)
    {
        if (key is null) return false;

        var colon = key.IndexOf(':');
        return colon < 0
            ? NamePattern().IsMatch(key)
            : IsValidNamespace(key[..colon]) && NamePattern().IsMatch(key[(colon + 1)..]);
    }

    /// <summary>A well-formed name that has a namespace, i.e. one a user or third party may define (not a core field).</summary>
    public static bool IsExtension(string? key) => key is not null && key.Contains(':') && IsValid(key);

    public static bool IsValidNamespace(string ns) => ns is "local" or "shirabe" || DomainPattern().IsMatch(ns);

    /// <summary>
    /// The key for a field a person named <paramref name="label"/>: <c>local:mood</c> for "Mood". People type any name in
    /// any script; the key only has to be valid and unique, so what cannot be written as a-z and digits (a Cyrillic
    /// name, say) falls back to <c>local:field</c>, and a taken key gets <c>-2</c>, <c>-3</c>...
    /// </summary>
    public static string FromLabel(string label, IEnumerable<string> taken)
    {
        var slug = new System.Text.StringBuilder();
        foreach (var c in label.Normalize(System.Text.NormalizationForm.FormD)) // "é" becomes "e" plus an accent that is dropped
        {
            if (char.IsAsciiLetterOrDigit(c)) slug.Append(char.ToLowerInvariant(c));
            else if (c is ' ' or '-' or '_' && slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }

        var name = slug.ToString().Trim('-');
        if (name.Length > 40) name = name[..40].TrimEnd('-');
        if (name.Length == 0) name = "field";
        else if (!char.IsAsciiLetter(name[0])) name = "field-" + name; // a name starts with a letter

        var used = new HashSet<string>(taken);
        var key = $"{LocalNamespace}:{name}";
        for (var n = 2; used.Contains(key); n++) key = $"{LocalNamespace}:{name}-{n}";
        return key;
    }
}
