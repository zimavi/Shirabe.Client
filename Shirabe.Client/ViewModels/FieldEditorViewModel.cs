using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Shirabe.Core.Metadata;
using Shirabe.Metadata;

namespace Shirabe.Client.ViewModels;

/// <summary>
/// One custom field of a song in the properties dialog: what is typed, and how it becomes a JSON value of the field's
/// type. Nothing is written until the dialog is saved. A field that was not edited is left exactly as stored.
/// </summary>
public partial class FieldEditorViewModel : ViewModelBase
{
    private readonly string _originalText;
    private readonly bool _originalFlag;

    /// <param name="value">What the song has now, or null for a field that is being added.</param>
    /// <param name="isNewDefinition">The definition does not exist yet; it is created together with the value.</param>
    public FieldEditorViewModel(FieldDefinition definition, JsonElement? value, bool isNewDefinition)
    {
        Definition = definition;
        IsNewDefinition = isNewDefinition;
        HadStoredValue = value is not null;

        if (value is { } stored)
        {
            if (IsBoolean) Flag = stored.ValueKind == JsonValueKind.True;
            else Text = ToText(definition, stored);
        }

        _originalText = Text;
        _originalFlag = Flag;
    }

    public FieldDefinition Definition { get; }

    public bool IsNewDefinition { get; }

    /// <summary>The song had a value for this field when the dialog opened.</summary>
    public bool HadStoredValue { get; }

    public string Name => Definition.DisplayName;

    /// <summary>A single yes/no field is a switch; everything else is typed.</summary>
    public bool IsBoolean => Definition is { Type: FieldType.Boolean, Multiple: false };

    public bool IsMultiline => Definition.Multiple;

    public string Hint => Definition.Type switch
    {
        _ when Definition.Multiple => "One per line",
        FieldType.Number => "A number",
        FieldType.Date => "1998, 1998-07 or 1998-07-14",
        FieldType.Link => "https://...",
        _ => "",
    };

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    public partial bool Flag { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    /// <summary>Marked for removal; the value is deleted when the dialog is saved.</summary>
    [ObservableProperty]
    public partial bool IsRemoved { get; set; }

    public bool IsChanged => IsBoolean ? Flag != _originalFlag : Text.Trim() != _originalText.Trim();

    /// <summary>
    /// Turns what is typed into a value of the field's type. <paramref name="value"/> is null when nothing is typed:
    /// the field then has no value (it is removed). False, with a sentence for the person in <paramref name="error"/>,
    /// when what is typed does not fit the type.
    /// </summary>
    public bool TryBuild(out JsonElement? value, out string? error)
    {
        value = null;
        error = null;

        if (IsBoolean)
        {
            value = JsonSerializer.SerializeToElement(Flag);
            return true;
        }

        var lines = Definition.Multiple
            ? Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Text.Trim().Length > 0 ? [Text.Trim()] : [];
        if (lines.Length == 0) return true;

        var items = new List<JsonElement>();
        foreach (var line in lines)
        {
            if (!TryItem(line, out var item, out error)) return false;
            items.Add(item);
        }

        var built = Definition.Multiple ? JsonSerializer.SerializeToElement(items) : items[0];
        if (!Definition.Validate(built, allowFileLinks: true, out error)) return false;

        value = built;
        return true;
    }

    private bool TryItem(string text, out JsonElement item, out string? error)
    {
        error = null;
        item = default;
        switch (Definition.Type)
        {
            case FieldType.Number:
                // shortcut: a comma is read as a decimal point, so "1,234" is 1.234; fine for tempos and ratings, revisit with real quantities
                if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    || !double.IsFinite(number))
                {
                    error = "Needs a number.";
                    return false;
                }

                item = JsonSerializer.SerializeToElement(number);
                return true;

            case FieldType.Boolean: // only reachable for a list of yes/no values, which the dialog cannot create but a file may hold
                if (text.ToLowerInvariant() is "yes" or "true") item = JsonSerializer.SerializeToElement(true);
                else if (text.ToLowerInvariant() is "no" or "false") item = JsonSerializer.SerializeToElement(false);
                else
                {
                    error = "Needs yes or no.";
                    return false;
                }

                return true;

            case FieldType.Link:
                // shortcut: a link typed here is rewritten as just its url, so a hash or label from an imported link is lost when the url is edited
                item = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["url"] = text });
                return true;

            default:
                item = JsonSerializer.SerializeToElement(text);
                return true;
        }
    }

    private static string ToText(FieldDefinition definition, JsonElement value)
    {
        if (!definition.Multiple) return ItemToText(definition, value);
        return string.Join('\n', value.EnumerateArray().Select(item => ItemToText(definition, item)));
    }

    private static string ItemToText(FieldDefinition definition, JsonElement item) => definition.Type switch
    {
        FieldType.Link => item.TryGetProperty("url", out var url) ? url.GetString() ?? "" : "",
        FieldType.Number => item.GetRawText(),
        FieldType.Boolean => item.ValueKind == JsonValueKind.True ? "yes" : "no",
        _ => item.GetString() ?? "",
    };
}
