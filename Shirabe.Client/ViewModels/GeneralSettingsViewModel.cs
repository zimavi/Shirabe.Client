using Shirabe.Client.Services;

namespace Shirabe.Client.ViewModels;

public sealed class GeneralSettingsViewModel(AppSettings settings) : SettingsPageViewModel("General", "Startup")
{
    public AppSettings Settings { get; } = settings;
}
