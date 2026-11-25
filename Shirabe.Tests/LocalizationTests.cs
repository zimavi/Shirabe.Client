using System.Text.RegularExpressions;
using System.Xml.Linq;

using Shirabe.Client.Localization;
using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;

using Xunit;

namespace Shirabe.Tests;

public partial class LocalizationTests
{
    [GeneratedRegex(@"\{loc:T\s+(\w+)\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"Loc\.Get\(\s*""(\w+)""")]
    private static partial Regex CodeKey();

    internal static string ClientFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shirabe.Client.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "Shirabe.Client");
    }

    private static HashSet<string> SourceKeys() =>
        [.. XamlKeys().Concat(CodeKeys())];

    private static IEnumerable<string> XamlKeys() =>
        Directory.EnumerateFiles(ClientFolder(), "*.axaml", SearchOption.AllDirectories)
            .SelectMany(file => XamlKey().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value));

    // Keys written as plain strings: Loc.Get("X"), the page view models' base("X_Title", "X_Description") and the tray's AddItem("X").
    // shortcut: a key chosen at run time (Loc.Get(flag ? "A" : "B")) is not seen, so name both keys in a Loc.Get("A") somewhere or check by hand.
    private static IEnumerable<string> CodeKeys() =>
        Directory.EnumerateFiles(ClientFolder(), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(@"\obj\") && !file.Contains("/obj/"))
            .SelectMany(file =>
            {
                var text = File.ReadAllText(file);
                var direct = CodeKey().Matches(text).Select(m => m.Groups[1].Value);
                var indirect = Regex.Matches(text, @"(?:base|AddItem)\(\s*""(\w+)""(?:\s*,\s*""(\w+)"")?")
                    .SelectMany(m => new[] { m.Groups[1], m.Groups[2] }.Where(g => g.Success).Select(g => g.Value));
                return direct.Concat(indirect);
            });

    private static HashSet<string> ResxKeys() =>
    [
        .. XDocument.Load(Path.Combine(ClientFolder(), "Resources", "Strings.resx")).Root!.Elements("data")
            .Select(e => (string)e.Attribute("name")!)
    ];

    [Fact]
    public void Every_key_used_in_the_source_exists_in_the_English_file()
    {
        var missing = SourceKeys().Except(ResxKeys()).Order().ToList();
        Assert.True(missing.Count == 0, "Missing from Strings.resx: " + string.Join(", ", missing));
    }

    [Fact]
    public void A_language_without_translations_shows_English_and_announces_the_change()
    {
        var changes = new List<string>();
        Loc.Instance.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);
        var raised = 0;
        Loc.Instance.LanguageChanged += (_, _) => raised++;

        Loc.Instance.SetLanguage("de"); // no Strings.de.resx is built here
        try
        {
            Assert.Equal("Home", Loc.Get("Nav_Home"));
            Assert.Equal("17 pt", Loc.Get("Lyrics_TextSizeValue", 17));
            Assert.Contains("Item[]", changes);
            Assert.Equal(1, raised);
        }
        finally
        {
            Loc.Instance.SetLanguage("en");
        }
    }

    [Fact]
    public void An_unknown_key_shows_the_key_instead_of_nothing()
    {
        Assert.Equal("No_Such_Key", Loc.Get("No_Such_Key"));
    }

    [Fact]
    public void The_language_list_starts_with_the_system_default_and_selecting_one_saves_its_tag()
    {
        var settings = new AppSettings();
        var page = new GeneralSettingsViewModel(settings);

        Assert.Equal("", page.Languages[0].Tag);
        Assert.Contains(page.Languages, language => language.Tag == "en");
        Assert.Equal("System default", page.Languages[0].Name);
        Assert.Equal("English", page.Languages.Single(language => language.Tag == "en").Name);
        Assert.Same(page.Languages[0], page.SelectedLanguage);

        page.SelectedLanguage = page.Languages.Single(language => language.Tag == "en");
        Assert.Equal("en", settings.Language);
        page.SelectedLanguage = null!; // a combo box reset must not clear the choice
        Assert.Equal("en", settings.Language);
    }

    [Fact]
    public void Texts_built_in_code_are_raised_again_when_the_language_changes()
    {
        var page = new LyricsSettingsViewModel(new AppSettings());
        var changed = new List<string?>();
        page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Loc.Instance.SetLanguage("en");

        Assert.Contains(nameof(SettingsPageViewModel.Title), changed);
        Assert.Contains(nameof(LyricsSettingsViewModel.DelayText), changed);
    }
}