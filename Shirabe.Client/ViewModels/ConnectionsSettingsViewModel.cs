using Shirabe.Client.Services;

namespace Shirabe.Client.ViewModels;

public sealed class ConnectionsSettingsViewModel(AppSettings settings, LastFmViewModel lastFm)
    : SettingsPageViewModel("Connections_Title", "Connections_Description", "Icon.Settings.Connections")
{
    public AppSettings Settings { get; } = settings;

    public LastFmViewModel LastFm { get; } = lastFm;
}