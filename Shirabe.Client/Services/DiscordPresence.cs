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

    public const string PlayingId = "playing";
    public const string PausedId = "paused";
    public const string PlaylistRepeatId = "playlist_repeat";
    public const string RepeatId = "repeat";

    /// <summary>Key of the image uploaded under the application's Rich Presence assets.</summary>
    private const string FallbackImage = "main";

    private readonly PlayerViewModel _player;
    private readonly AppSettings _settings;
    private readonly DiscordRpcClient _client;
    private readonly CoverLookup _covers = new();

    public DiscordPresence(PlayerViewModel player, AppSettings settings)
    {
        _player = player;
        _settings = settings;
        _client = new DiscordRpcClient(ApplicationId);
        _client.Initialize();
        _player.PropertyChanged += OnPlayerPropertyChanged;
        _player.Seeked += OnSeeked;
        _player.TrackLoaded += OnTrackLoaded;
        _settings.PropertyChanged += OnSettingsChanged;
        if (_player.IsPlaying) Update(); // switched on while a song is playing
    }

    public void Dispose()
    {
        _player.PropertyChanged -= OnPlayerPropertyChanged;
        _player.Seeked -= OnSeeked;
        _player.TrackLoaded -= OnTrackLoaded;
        _settings.PropertyChanged -= OnSettingsChanged;
        _client.Dispose();
    }

    private void OnSeeked(object? sender, EventArgs e) => Update();

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.PlaybackSpeed) or nameof(AppSettings.PlaybackSpeedControl)
            && _player.IsPlaying) Update();
    }

    // Switching songs while one plays leaves IsPlaying true, so nothing else would tell us. A song loaded paused (the
    // restored session) shows no card; pressing play does.
    private void OnTrackLoaded(object? sender, EventArgs e)
    {
        if (_player.IsPlaying) Update();
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.IsPlaying) or 
            nameof(PlayerViewModel.IsRepeatActive) or 
            nameof(PlayerViewModel.IsRepeatOne)) Update();
    }

    private async void Update()
    {
        // A cover the song's own metadata points to is already a public address: no need to look one up.
        string? cover = _player.CurrentTrack?.CoverUrl;
        var artist = _player.Subtitle;
        var album = _player.Album;
        var lookup = cover is null && _settings.CoverLookupEnabled && artist is not (null or "Unknown artist") && !string.IsNullOrWhiteSpace(album);
        var cached = lookup && _covers.TryGetCached(artist!, album!, out cover);

        SetPresence(cover);
        
        if (!lookup || cached) return;

        // Show the card at once, then again with the cover once it is found (if the same song is still playing).
        var found = await _covers.FindAsync(artist!, album!);
        if (found is not null && _player.IsPlaying && _player.Subtitle == artist && _player.Album == album)
            SetPresence(found);
    }

    private void SetPresence(string? cover)
    {
        _client.SetPresence(Build(_player.Title, _player.Subtitle, _player.Album, cover, _player.IsPlaying,
            _player.IsRepeatActive, _player.IsRepeatOne,
            TimeSpan.FromSeconds(_player.PositionSeconds / _player.EffectiveSpeed),
            TimeSpan.FromSeconds(_player.DurationSeconds / _player.EffectiveSpeed),
            DateTime.UtcNow, _player.CurrentTrack?.LinkUrl, _player.CurrentTrack?.LinkLabel));
    }

    /// <summary>Title on the first line, artist on the second, album on the third. Timestamps make the progress bar.</summary>
    public static RichPresence Build(string title, string? artist, string? album, string? coverUrl, bool isPlaying, bool isRepeat, bool isRepeatOne, TimeSpan position, TimeSpan duration, DateTime now, string? linkUrl = null, string? linkLabel = null)
    {
        var start = now - position;
        var presence = new RichPresence
        {
            Type = ActivityType.Listening,
            Details = Fit(title),
            State = Fit(artist),
            Timestamps = new Timestamps { Start = start, End = duration > TimeSpan.Zero && isPlaying ? start + duration : null },
        };

        // A cover must be a public URL (see CoverLookup); without one, show the app's own image uploaded in the portal.
        presence.Assets = new Assets
        {
            LargeImageKey = coverUrl ?? FallbackImage, LargeImageText = Fit(album),
            SmallImageKey = isPlaying ? (isRepeatOne ? RepeatId : isRepeat ? PlaylistRepeatId : PlayingId) : PausedId,
            SmallImageText = isPlaying ? (isRepeatOne ? "On repeat" : isRepeat ? "Playlist on repeat" : "Playing") : "Paused"
        };

        // The link the song's metadata gives, as the card's button (Discord shows it to others, not to you). Its limits: a
        // label of up to 31 bytes, an address of up to 512 characters; a long label is cut, a long address leaves the button out.
        if (linkUrl is { Length: <= 512 } && Fit(linkLabel, 31) is { } label)
            presence.Buttons = [new Button { Label = label, Url = linkUrl }];

        return presence;
    }

    // Discord rejects fields shorter than 2 or longer than 128 bytes; the library throws on the latter.
    private static string? Fit(string? text, int maxBytes = 120)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        text = text.Trim();
        while (System.Text.Encoding.UTF8.GetByteCount(text) > maxBytes) text = text[..^1];
        return text.Length < 2 ? text + "​" : text;
    }
}
