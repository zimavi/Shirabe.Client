using System.ComponentModel;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;

namespace Shirabe.Client.Services;

/// <summary>
/// Downloads the cover a song's metadata links to, when that song is loaded into the player (the picture is about to be
/// shown, the only time the standard allows a fetch), and shows it in place of the file's own cover. Does nothing when
/// the setting is off. The Songs list shows a linked cover once it has been downloaded this way.
/// </summary>
public sealed class LinkedCovers
{
    private readonly PlayerViewModel _player;
    private readonly AppSettings _settings;
    private readonly IArtworkLinks _links;

    public LinkedCovers(PlayerViewModel player, AppSettings settings, IArtworkLinks links)
    {
        _player = player;
        _settings = settings;
        _links = links;
        _player.PropertyChanged += OnPlayerPropertyChanged;
    }

    private async void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlayerViewModel.CurrentTrack)) return;
        if (_player.CurrentTrack is not { CoverUrl: { } url } track || !_settings.DownloadArtworkEnabled) return;

        var path = await _links.FetchAsync(url, track.CoverSha256);

        // The song may have changed while the picture was on its way.
        if (path is not null && _player.CurrentTrack == track) _player.ArtworkPath = path;
    }
}
