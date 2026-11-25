using Shirabe.Client.Services;

namespace Shirabe.Client.ViewModels;

public sealed class ConnectionsSettingsViewModel(AppSettings settings)
    : SettingsPageViewModel("Connections_Title", "Connections_Description", "Icon.Settings.Connections")
{
    public AppSettings Settings { get; } = settings;
}