using System.ComponentModel;
using DiscordRPC;
using Shirabe.Client.ViewModels;

namespace Shirabe.Client.Services;

/// <summary>
/// Shows the song that is playing as a "Listening to" card on the user's Discord profile. Does nothing if
/// Discord is not running. Paused or stopped playback removes the card (Discord cannot show a paused bar).
/// </summary>
public sealed class DiscordPresence : IDisposable
{
    /// <summary>Id of the "Shirabe" application in the Discord developer portal; its name is the card's header.</summary>
    public const string ApplicationId = "1574052178073755758";

    /// <summary>Key of the image uploaded under the application's Rich Presence assets.</summary>
    private const string FallbackImage = "main";

    private readonly PlayerViewModel _player;
    private readonly DiscordRpcClient _client;
    private readonly CoverLookup _covers = new();

    public DiscordPresence(PlayerViewModel player)
    {
        _player = player;
        _client = new DiscordRpcClient(ApplicationId);
        _client.Initialize();
        _player.PropertyChanged += OnPlayerPropertyChanged;
        _player.Seeked += (_, _) => Update();
    }

    public void Dispose()
    {
        _player.PropertyChanged -= OnPlayerPropertyChanged;
        _client.Dispose();
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsPlaying)) Update();
    }

    private async void Update()
    {
        if (!_player.IsPlaying)
        {
            _client.ClearPresence();
            return;
        }

        string? cover = null;
        var artist = _player.Subtitle;
        var album = _player.Album;
        var lookup = artist is not (null or "Unknown artist") && !string.IsNullOrWhiteSpace(album);
        var cached = lookup && _covers.TryGetCached(artist!, album!, out cover);

        SetPresence(cover);
        if (!lookup || cached) return;

        // Show the card at once, then again with the cover once it is found (if the same song is still playing).
        var found = await _covers.FindAsync(artist!, album!);
        if (found is not null && _player.IsPlaying && _player.Subtitle == artist && _player.Album == album)
            SetPresence(found);
    }

    private void SetPresence(string? cover) =>
        _client.SetPresence(Build(_player.Title, _player.Subtitle, _player.Album, cover,
            TimeSpan.FromSeconds(_player.PositionSeconds), TimeSpan.FromSeconds(_player.DurationSeconds), DateTime.UtcNow));

    /// <summary>Title on the first line, artist on the second, album on the third. Timestamps make the progress bar.</summary>
    public static RichPresence Build(string title, string? artist, string? album, string? coverUrl, TimeSpan position, TimeSpan duration, DateTime now)
    {
        var start = now - position;
        var presence = new RichPresence
        {
            Type = ActivityType.Listening,
            Details = Fit(title),
            State = Fit(artist),
            Timestamps = new Timestamps { Start = start, End = duration > TimeSpan.Zero ? start + duration : null },
        };

        // A cover must be a public URL (see CoverLookup); without one, show the app's own image uploaded in the portal.
        presence.Assets = new Assets { LargeImageKey = coverUrl ?? FallbackImage, LargeImageText = Fit(album) };

        return presence;
    }

    // Discord rejects fields shorter than 2 or longer than 128 bytes; the library throws on the latter.
    private static string? Fit(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        text = text.Trim();
        while (System.Text.Encoding.UTF8.GetByteCount(text) > 120) text = text[..^1];
        return text.Length < 2 ? text + "​" : text;
    }
}
