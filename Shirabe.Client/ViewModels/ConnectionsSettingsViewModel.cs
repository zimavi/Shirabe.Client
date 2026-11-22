using Shirabe.Client.Services;

namespace Shirabe.Client.ViewModels;

public sealed class ConnectionsSettingsViewModel(AppSettings settings)
    : SettingsPageViewModel("Connections", "Discord and online lookups")
{
    public AppSettings Settings { get; } = settings;
}
