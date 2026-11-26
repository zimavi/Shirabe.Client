using CommunityToolkit.Mvvm.ComponentModel;

using Shirabe.Client.Localization;
using Shirabe.Client.Services;

namespace Shirabe.Client.ViewModels;

public sealed class ConnectionsSettingsViewModel : SettingsPageViewModel
{
    public ConnectionsSettingsViewModel(AppSettings settings, LastFmViewModel lastFm)
        : base("Connections_Title", "Connections_Description", "Icon.Settings.Connections")
    {
        Settings = settings;
        LastFm = lastFm;
        // shortcut: never unsubscribed, fine while the settings pages live as long as the app
        Loc.Instance.LanguageChanged += (_, _) => OnUiThread(() =>
        {
            foreach (var option in UpdateChannelOptions) option.Refresh();
        });
    }

    public AppSettings Settings { get; }

    public LastFmViewModel LastFm { get; }

    public IReadOnlyList<UpdateChannelOption> UpdateChannelOptions { get; } =
        [.. Enum.GetValues<UpdateChannel>().Select(channel => new UpdateChannelOption(channel))];

    public UpdateChannelOption? SelectedUpdateChannel
    {
        get => UpdateChannelOptions.FirstOrDefault(option => option.Channel == Settings.UpdateChannel);
        set
        {
            if (value is not null) Settings.UpdateChannel = value.Channel;
        }
    }
}

/// <summary>One entry of the update channel list.</summary>
public sealed class UpdateChannelOption(UpdateChannel channel) : ObservableObject
{
    public UpdateChannel Channel { get; } = channel;

    public string Name => Loc.Get(Channel == UpdateChannel.Beta
        ? "Connections_UpdateChannelBeta"
        : "Connections_UpdateChannelStable");

    public void Refresh() => OnPropertyChanged(nameof(Name));
}