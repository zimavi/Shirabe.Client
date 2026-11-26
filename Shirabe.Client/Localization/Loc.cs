using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Shirabe.Client.Localization;

/// <summary>
/// The app's translated strings. XAML reads them through <see cref="TExtension"/> (an indexer binding, so a language change
/// updates every label), code through <see cref="Get"/>. English lives in Resources/Strings.resx; Crowdin adds
/// Strings.&lt;locale&gt;.resx beside it, and a missing translation falls back to English.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    private static readonly ResourceManager Strings = new("Shirabe.Client.Resources.Strings", typeof(Loc).Assembly);

    // Every other language but English is machine translation. Add a language once a person has translated or reviewed it.
    private static readonly string[] HumanTranslated = ["uk-UA"];

    public static Loc Instance { get; } = new();

    // Invariant (= English) until the app picks a language, so unit tests do not depend on the machine's language.
    private CultureInfo _culture = CultureInfo.InvariantCulture;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed; view models with texts built by <see cref="Get"/> refresh on it.</summary>
    public event EventHandler? LanguageChanged;

    // An empty translation counts as missing, so a bad export shows English instead of a blank label.
    public string this[string key] =>
        Strings.GetString(key, _culture) is { Length: > 0 } text
            ? text
            : Strings.GetString(key, CultureInfo.InvariantCulture) ?? key;

    /// <summary>The shipped language whose texts are on screen.</summary>
    public CultureInfo InUse => Shipped(_culture);

    /// <summary>Languages that ship with the app: English plus every Strings.&lt;locale&gt;.resx that was built.</summary>
    public static IReadOnlyList<CultureInfo> Available { get; } = FindAvailable();

    public static string Get(string key) => Instance[key];

    public static string Get(string key, params object[] args) => string.Format(Instance._culture, Instance[key], args);

    /// <summary>Uses the language with this tag ("uk", "pt-BR"); empty or unknown means the system's language.</summary>
    public void SetLanguage(string tag)
    {
        _culture = Resolve(tag);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]")); // every indexer binding re-reads
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The language itself or its closest shipped parent, as the resource fallback picks it; otherwise English.</summary>
    public static CultureInfo Shipped(CultureInfo culture)
    {
        for (; !culture.Equals(CultureInfo.InvariantCulture); culture = culture.Parent)
        {
            if (Available.Contains(culture)) return culture;
        }

        return Available[0];
    }

    public static bool IsMachineTranslated(CultureInfo culture) =>
        culture.Name != "en" && !HumanTranslated.Contains(culture.Name);

    /// <summary>
    /// The share of the English texts that a shipped translation has, in whole percent, rounded down. It relies on the
    /// translation files leaving untranslated texts out (skip_untranslated_strings in crowdin.yml).
    /// </summary>
    public static int Completeness(CultureInfo culture)
    {
        var translation = Strings.GetResourceSet(culture, true, false);
        if (translation is null) return 0;

        var english = Strings.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var keys = english.Cast<DictionaryEntry>().Select(entry => (string)entry.Key).ToList();
        return keys.Count(key => !string.IsNullOrEmpty(translation.GetString(key))) * 100 / keys.Count;
    }

    private static CultureInfo Resolve(string tag)
    {
        if (tag.Length > 0)
        {
            try
            {
                return CultureInfo.GetCultureInfo(tag);
            }
            catch (CultureNotFoundException)
            {
                // fall through to the system language
            }
        }

        return CultureInfo.InstalledUICulture;
    }

    private static List<CultureInfo> FindAvailable()
    {
        var languages = new List<CultureInfo> { CultureInfo.GetCultureInfo("en") };
        foreach (var folder in Directory.EnumerateDirectories(AppContext.BaseDirectory))
        {
            // shortcut: satellite assemblies are found by folder name next to the app, upgrade if the app is ever published as a single file
            if (!File.Exists(Path.Combine(folder, "Shirabe.Client.resources.dll"))) continue;
            try
            {
                languages.Add(CultureInfo.GetCultureInfo(Path.GetFileName(folder)));
            }
            catch (CultureNotFoundException)
            {
            }
        }

        return languages;
    }
}