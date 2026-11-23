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

    public static Loc Instance { get; } = new();

    // Invariant (= English) until the app picks a language, so unit tests do not depend on the machine's language.
    private CultureInfo _culture = CultureInfo.InvariantCulture;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed; view models with texts built by <see cref="Get"/> refresh on it.</summary>
    public event EventHandler? LanguageChanged;

    public string this[string key] => Strings.GetString(key, _culture) ?? key;

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