using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Shirabe.Core.Metadata;
using Shirabe.Core.Playback;

namespace Shirabe.Client.Services;

public enum ScrobbleResult
{
    Scrobbled,

    /// <summary>Last.fm received it and chose not to keep it.</summary>
    Ignored,

    Failed,
}

/// <summary>What became of one scrobble.</summary>
public sealed record ScrobbleReport(ScrobbleResult Result, LastFmTrack Song);

/// <summary>
/// Tells Last.fm what the player plays: "now playing" when a song starts and a scrobble once enough of it was heard
/// (see <see cref="ScrobbleTracker"/>), while the user is signed in and scrobbling is on. Sends the artist, title,
/// album, length and start time. Call <see cref="TickAsync"/> about once a second, on the UI thread.
/// </summary>
// shortcut: a send that fails is lost, until there is a queue on disk to retry from
public sealed class Scrobbler
{
    private readonly PlayerViewModel _player;
    private readonly AppSettings _settings;
    private readonly LastFmAccount _account;
    private readonly LastFmClient _client;
    private readonly IFileTags? _tags;
    private readonly TimeProvider _time;
    private TrackSummary? _track;
    private ScrobbleTracker? _tracker;

    /// <param name="tags">Used to tell a real title from a file name; null when there is no library.</param>
    public Scrobbler(PlayerViewModel player, AppSettings settings, LastFmAccount account, LastFmClient client,
        IFileTags? tags = null, TimeProvider? time = null)
    {
        _player = player;
        _settings = settings;
        _account = account;
        _client = client;
        _tags = tags;
        _time = time ?? TimeProvider.System;
        player.TrackLoaded += OnTrackLoaded;
        player.Seeked += (_, _) => _tracker?.Seek(player.EnginePosition);
    }

    private void OnTrackLoaded(object? sender, EventArgs e)
    {
        _track = _player.CurrentTrack;
        _tracker = new ScrobbleTracker(_player.DurationSeconds > 0
            ? TimeSpan.FromSeconds(_player.DurationSeconds)
            : null);
    }

    /// <summary>Raised on the UI thread after each scrobble was tried. "Now playing" is not reported.</summary>
    public event EventHandler<ScrobbleReport>? Reported;

    /// <summary>The returned task ends when what this moment calls for has been sent (usually nothing).</summary>
    public Task TickAsync()
    {
        // Only while playing: a stopped or released player reads as being at the start.
        if (_tracker is null || _track is null || !_player.IsPlaying) return Task.CompletedTask;

        var step = _tracker.Advance(_player.EnginePosition, _time.GetUtcNow());
        if (step == ScrobbleStep.None || !_settings.ScrobblingEnabled || _account.Session is not { } session)
            return Task.CompletedTask;

        var duration = _player.DurationSeconds > 0 ? TimeSpan.FromSeconds(_player.DurationSeconds) : (TimeSpan?)null;
        return SendAsync(step, session, _track, duration, _tracker.StartedAt!.Value);
    }

    private async Task SendAsync(ScrobbleStep step, LastFmSession session, TrackSummary track, TimeSpan? duration,
        DateTimeOffset startedAt)
    {
        if (await DescribeAsync(track, duration) is not { } song) return;

        ScrobbleResult result;
        try
        {
            if (step == ScrobbleStep.Started)
            {
                await _client.UpdateNowPlayingAsync(session, song);
                return;
            }

            result = await _client.ScrobbleAsync(session, song, startedAt)
                ? ScrobbleResult.Scrobbled
                : ScrobbleResult.Ignored;
        }
        catch (Exception ex) when (ex is LastFmException or HttpRequestException or TaskCanceledException)
        {
            // Playing goes on; "now playing" is not worth retrying, nor worth a message.
            if (step == ScrobbleStep.Started) return;
            result = ScrobbleResult.Failed;
        }

        Reported?.Invoke(this, new ScrobbleReport(result, song));
    }

    /// <summary>
    /// The song as Last.fm may be told about it, or null when its metadata is not enough: no artist, a file dropped on
    /// the app (its tags are not read), or a title that is only the file's name.
    /// </summary>
    private async Task<LastFmTrack?> DescribeAsync(TrackSummary track, TimeSpan? duration)
    {
        if (string.IsNullOrWhiteSpace(track.Artist) || track.Artist == PlayerViewModel.DroppedFileArtist) return null;

        return IsFileName(track) && !await HasTitleTagAsync(track.RecordingId)
            ? null
            : new LastFmTrack(track.Artist, track.Title, track.Album, duration);
    }

    private static bool IsFileName(TrackSummary track) =>
        Uri.TryCreate(track.MediaUri, UriKind.Absolute, out var uri) && uri.IsFile
        && Path.GetFileNameWithoutExtension(uri.LocalPath).Trim() == track.Title.Trim();

    // Many well-tagged files are named after their title, so the file itself has to say whether it has one.
    private async Task<bool> HasTitleTagAsync(Guid recordingId)
    {
        if (_tags is null) return false;

        try
        {
            var tags = await _tags.ReadFileTagsAsync(recordingId);
            return tags.Any(tag => tag is { Group: FileTag.TagsGroup, Name: "Title" });
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException
                                       or InvalidDataException)
        {
            return false;
        }
    }
}
