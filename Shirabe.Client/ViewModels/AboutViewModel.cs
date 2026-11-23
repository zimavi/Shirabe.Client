using Shirabe.Client.Services;

namespace Shirabe.Client.ViewModels;

/// <summary>An open-source component the app is built with, shown on the About page. Names and licenses are not translated.</summary>
public sealed record ThirdPartyComponent(string Name, string License, string Url);

/// <summary>Version, license, credits and the notices the licenses of what we ship ask for.</summary>
public sealed class AboutViewModel : SettingsPageViewModel
{
    public AboutViewModel() : this(AppVersion.Current)
    {
    }

    public AboutViewModel(AppVersion app) : base("About_Title", "About_Description")
    {
        Version = app.Version;
        Commit = app.Commit;
    }

    public string Version { get; }

    public string? Commit { get; }

    public bool HasCommit => Commit is not null;

    // The view opens these through one Click handler that only accepts https addresses.
    public string RepositoryUrl => "https://github.com/zimavi/Shirabe.Client";
    public string IssuesUrl => "https://github.com/zimavi/Shirabe.Client/issues";
    public string LicenseUrl => "https://github.com/zimavi/Shirabe.Client/blob/master/LICENSE.txt";
    public string AuthorUrl => "https://github.com/zimavi";
    public string BassUrl => "https://www.un4seen.com/";

    // Keep in step with the packages in Directory.Packages.props and NOTICE.txt.
    public IReadOnlyList<ThirdPartyComponent> Components { get; } =
    [
        new("Avalonia", "MIT", "https://avaloniaui.net/"),
        new("Inter typeface", "SIL Open Font License 1.1", "https://rsms.me/inter/"),
        new("Material Design Icons", "Apache License 2.0", "https://github.com/google/material-design-icons"),
        new("ManagedBass", "MIT", "https://github.com/ManagedBass/ManagedBass"),
        new("Audio Tools Library (ATL)", "MIT", "https://github.com/Zeugma440/atldotnet"),
        new("CommunityToolkit.Mvvm", "MIT", "https://github.com/CommunityToolkit/dotnet"),
        new("Entity Framework Core", "MIT", "https://github.com/dotnet/efcore"),
        new("SQLite", "Public domain", "https://sqlite.org/"),
        new("DiscordRichPresence", "MIT", "https://github.com/Lachee/discord-rpc-csharp"),
        new("Velopack", "MIT", "https://github.com/velopack/velopack"),
    ];
}