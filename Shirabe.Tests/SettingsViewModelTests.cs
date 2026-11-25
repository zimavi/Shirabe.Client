using Shirabe.Client;
using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;

using Xunit;

namespace Shirabe.Tests;

public class SettingsViewModelTests
{
    [Fact]
    public void It_opens_on_the_first_page_and_lists_every_page()
    {
        var viewModel = new SettingsViewModel(new AppSettings(), new ThemeCatalog([], []));

        Assert.Same(viewModel.Pages[0], viewModel.SelectedPage);
        Assert.Equal(["General", "Audio", "Appearance", "Lyrics", "Shortcuts", "Connections", "About"],
            viewModel.Pages.Select(page => page.Title));
    }

    [Fact]
    public void The_appearance_page_offers_every_theme_and_shows_problems_with_theme_files()
    {
        var catalog = new ThemeCatalog([Theme.Dark with { Name = "Custom" }], ["bad.json: needs a \"name\""]);

        var page = new AppearanceSettingsViewModel(new AppSettings(), catalog);

        Assert.Equal(["Dark", "OLED", "Custom"], page.ThemeNames);
        Assert.True(page.HasThemeProblems);
        Assert.Contains("bad.json", page.ThemeProblems);
    }

    [Fact]
    public void Every_settings_page_has_a_view_the_view_locator_can_find()
    {
        // ViewLocator maps Shirabe.Client.ViewModels.FooViewModel to Shirabe.Client.Views.FooView.
        var viewModel = new SettingsViewModel(new AppSettings(), new ThemeCatalog([], []));
        ViewModelBase[] all = [viewModel, .. viewModel.Pages];

        foreach (var model in all)
        {
            var name = model.GetType().FullName!.Replace("ViewModel", "View");
            Assert.True(typeof(ViewLocator).Assembly.GetType(name) is not null, $"missing view {name}");
        }
    }
}