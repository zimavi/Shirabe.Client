using Avalonia.Media;
using Shirabe.Client.Services;
using Xunit;

namespace Shirabe.Tests;

public sealed class ThemeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "shirabe-themes-" + Guid.NewGuid());

    public ThemeTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private void Write(string file, string json) => File.WriteAllText(Path.Combine(_folder, file), json);

    [Fact]
    public void Parse_takes_the_colours_given_and_the_rest_from_dark()
    {
        var theme = Theme.Parse("""{ "name": " Midnight ", "background": "#000011", "accent": "#112233" }""", out var error);

        Assert.Null(error);
        Assert.NotNull(theme);
        Assert.Equal("Midnight", theme.Name);
        Assert.Equal(Color.Parse("#000011"), theme.Background);
        Assert.Equal(Color.Parse("#112233"), theme.Accent);
        Assert.Equal(Theme.Dark.Surface, theme.Surface);
    }

    [Theory]
    [InlineData("""{ "background": "#000000" }""", "name")]
    [InlineData("""{ "name": "   " }""", "name")]
    [InlineData("""{ "name": "X", "accent": "pinkish" }""", "accent")]
    [InlineData("""{ "name": "X", "accent": 5 }""", "accent")]
    [InlineData("""[1, 2]""", "name")]
    [InlineData("""{ "name": "X", """, "")]
    public void Parse_rejects_files_that_are_not_themes_and_says_why(string json, string mentions)
    {
        Assert.Null(Theme.Parse(json, out var error));
        Assert.Contains(mentions, error);
    }

    [Fact]
    public void Catalog_lists_built_in_themes_first_then_custom_ones_by_file_name()
    {
        Write("b.json", """{ "name": "Beta" }""");
        Write("a.json", """{ "name": "Alpha" }""");

        var catalog = ThemeCatalog.Load(_folder);

        Assert.Equal(["Dark", "OLED", "Alpha", "Beta"], catalog.Themes.Select(theme => theme.Name));
        Assert.Empty(catalog.Problems);
    }

    [Fact]
    public void Catalog_reports_unusable_files_and_name_clashes_but_keeps_the_good_ones()
    {
        Write("broken.json", "{ nope");
        Write("clash.json", """{ "name": "dark" }""");
        Write("good.json", """{ "name": "Good" }""");

        var catalog = ThemeCatalog.Load(_folder);

        Assert.Equal(["Dark", "OLED", "Good"], catalog.Themes.Select(theme => theme.Name));
        Assert.Equal(2, catalog.Problems.Count);
        Assert.Contains(catalog.Problems, problem => problem.StartsWith("broken.json:"));
        Assert.Contains(catalog.Problems, problem => problem.StartsWith("clash.json:") && problem.Contains("already exists"));
    }

    [Fact]
    public void A_missing_folder_means_only_the_built_in_themes()
    {
        var catalog = ThemeCatalog.Load(Path.Combine(_folder, "nothing-here"));

        Assert.Equal(2, catalog.Themes.Count);
        Assert.Empty(catalog.Problems);
    }

    [Fact]
    public void Find_ignores_case_and_falls_back_to_dark()
    {
        var catalog = ThemeCatalog.Load(_folder);

        Assert.Same(Theme.Oled, catalog.Find("oled"));
        Assert.Same(Theme.Dark, catalog.Find("a deleted theme"));
        Assert.Same(Theme.Dark, catalog.Find(null));
    }

    [Fact]
    public void Oled_has_a_true_black_background()
    {
        Assert.Equal(Colors.Black, Theme.Oled.Background);
        Assert.Equal(Theme.Dark.TextPrimary, Theme.Oled.TextPrimary);
    }
}
