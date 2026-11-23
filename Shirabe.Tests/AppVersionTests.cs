using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;

using Xunit;

namespace Shirabe.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("0.1.0+ce863a5e7a1b2c3d4e5f", "0.1.0", "ce863a5e7")]
    [InlineData("0.2.0-beta.1+abc", "0.2.0-beta.1", "abc")]
    [InlineData("0.1.0", "0.1.0", null)]
    [InlineData("0.1.0+", "0.1.0", null)]
    [InlineData("", "unknown", null)]
    [InlineData(null, "unknown", null)]
    public void It_splits_the_version_from_the_commit(string? stamped, string version, string? commit) =>
        Assert.Equal(new AppVersion(version, commit), AppVersion.Parse(stamped));

    [Fact]
    public void The_build_is_stamped_with_the_version_of_version_txt()
    {
        // The client assembly, not this test project, carries the stamp; both read the same file.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "version.txt"))) directory = directory.Parent!;

        Assert.Equal(File.ReadAllText(Path.Combine(directory.FullName, "version.txt")).Trim(),
            AppVersion.Current.Version);
    }

    [Fact]
    public void The_about_page_only_links_to_https_addresses()
    {
        var page = new AboutViewModel(new AppVersion("1.0.0", null));
        string[] links =
        [
            page.RepositoryUrl, page.IssuesUrl, page.LicenseUrl, page.AuthorUrl, page.BassUrl,
            .. page.Components.Select(component => component.Url)
        ];

        Assert.All(links, link => Assert.StartsWith("https://", link));
        Assert.False(page.HasCommit);
    }
}