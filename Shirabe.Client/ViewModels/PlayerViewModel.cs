using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shirabe.Core.Library;
using Shirabe.Core.Media;
using Shirabe.Core.Playback;

namespace Shirabe.Client.ViewModels;

/// <summary>
/// Drives a single <see cref="IAudioPlayer"/> through a <see cref="PlaybackQueue"/>. Free of Avalonia types:
/// the view calls <see cref="RefreshPosition"/> from its own timer, and player events are marshalled back to
/// the UI thread.
/// </summary>
public partial class PlayerViewModel : ViewModelBase
{
    /// <summary>"Previous" restarts the song instead of going back when it has been playing longer than this.</summary>
    private static readonly TimeSpan RestartThreshold = TimeSpan.FromSeconds(3);

    private readonly IAudioPlayer _player;
    private readonly PlaybackQueue _queue = new();
    private bool _updatingPosition;
    private double _lastAudibleVolume = 1;

    public PlayerViewModel(IAudioPlayer player)
    {
        _player = player;
        Volume = player.Volume;
        _player.StateChanged += (_, _) => OnUiThread(() => IsPlaying = _player.State == PlaybackState.Playing);
        _player.Ended += (_, _) => OnUiThread(OnEnded);
        _queue.Changed += (_, _) => QueueItems = [.. _queue.Items];
    }

    [ObservableProperty]
    public partial string Title { get; set; } = "Nothing playing";

    /// <summary>Second line of the now-playing area: the artist, or "Local file" for a dropped file.</summary>
    [ObservableProperty]
    public partial string? Subtitle { get; set; }

    [ObservableProperty]
    public partial string? Album { get; set; }

    /// <summary>The song that is loaded, playing or not; null until the first one.</summary>
    [ObservableProperty]
    public partial TrackSummary? CurrentTrack { get; set; }

    /// <summary>Raised when the user (not the playback clock) moves the position.</summary>
    public event EventHandler? Seeked;

    /// <summary>
    /// Raised after a song was loaded (and started, if it was meant to), with the title, position and length already
    /// those of the new song. Needed because switching songs while one plays does not change <see cref="IsPlaying"/>.
    /// </summary>
    public event EventHandler? TrackLoaded;

    /// <summary>Full path of the cover of the song that is playing, if its file has one.</summary>
    [ObservableProperty]
    public partial string? ArtworkPath { get; set; }

    /// <summary>Why the last attempt to play failed. Shown in the player bar, so it is visible on every page.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TogglePlayPauseCommand), nameof(NextCommand), nameof(PreviousCommand))]
    public partial bool HasMedia { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseText))]
    public partial bool IsPlaying { get; set; }

    /// <summary>What the play/pause command will do now, for menus that have text instead of an icon.</summary>
    public string PlayPauseText => IsPlaying ? "Pause" : "Play";

    /// <summary>0.0 (silent) to 1.0 (full), bound two-way to the volume slider.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeText), nameof(IsVolumeMuted), nameof(IsVolumeLow), nameof(IsVolumeMedium), nameof(IsVolumeHigh))]
    public partial double Volume { get; set; }

    /// <summary>Shown in the volume slider's tooltip.</summary>
    public string VolumeText => $"{Math.Round(Volume * 100)}%";

    // Icon states: muted, then thirds of the range.
    public bool IsVolumeMuted => Volume <= 0;

    public bool IsVolumeLow => Volume > 0 && Volume < 1.0 / 3;

    public bool IsVolumeMedium => Volume >= 1.0 / 3 && Volume < 2.0 / 3;

    public bool IsVolumeHigh => Volume >= 2.0 / 3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    public partial double DurationSeconds { get; set; }

    /// <summary>Bound two-way to the seek slider: a change made by the user seeks, see <see cref="RefreshPosition"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    public partial double PositionSeconds { get; set; }

    [ObservableProperty]
    public partial bool IsShuffleOn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRepeatActive), nameof(IsRepeatOne), nameof(RepeatText))]
    public partial RepeatMode Repeat { get; set; }

    /// <summary>The queue in play order, played songs included. A snapshot, replaced whenever the queue changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQueue))]
    public partial IReadOnlyList<QueueItem> QueueItems { get; set; } = [];

    [ObservableProperty]
    public partial bool IsQueueOpen { get; set; }

    public bool HasQueue => QueueItems.Count > 0;

    public bool IsRepeatActive => Repeat != RepeatMode.Off;

    public bool IsRepeatOne => Repeat == RepeatMode.One;

    public string RepeatText => Repeat switch
    {
        RepeatMode.All => "Repeat: all songs",
        RepeatMode.One => "Repeat: this song",
        _ => "Repeat: off",
    };

    public string PositionText => FormatTime(PositionSeconds);

    public string DurationText => FormatTime(DurationSeconds);

    /// <summary>Plays a file that was not picked from the library (dropped, or passed on the command line).</summary>
    public Task OpenAsync(string path)
    {
        var track = new TrackSummary(
            Guid.NewGuid(), Path.GetFileNameWithoutExtension(path), "Local file", null, null, new Uri(Path.GetFullPath(path)).AbsoluteUri);
        return PlayTracksAsync([track], 0);
    }

    public Task PlayAsync(TrackSummary track) => PlayTracksAsync([track], 0);

    /// <summary>Replaces the queue with <paramref name="tracks"/> and plays the one at <paramref name="startIndex"/>.</summary>
    public async Task PlayTracksAsync(IReadOnlyList<TrackSummary> tracks, int startIndex)
    {
        _queue.SetQueue(tracks, startIndex);
        await PlayCurrentAsync(skipFailures: false); // the user picked this song: if it fails, say so instead of moving on
    }

    /// <summary>Puts a song at the end of the queue; starts it if nothing is playing.</summary>
    public async Task EnqueueAsync(TrackSummary track)
    {
        if (_queue.Current is null) await PlayTracksAsync([track], 0);
        else _queue.Enqueue(track);
    }

    /// <summary>Puts a song right after the current one; starts it if nothing is playing.</summary>
    public async Task PlayNextAsync(TrackSummary track)
    {
        if (_queue.Current is null) await PlayTracksAsync([track], 0);
        else _queue.PlayNext(track);
    }

    /// <summary>What <see cref="RestoreAsync"/> needs to bring the player back after a restart.</summary>
    public PlayerSession Capture() =>
        new(Volume, _lastAudibleVolume, IsShuffleOn, Repeat, CurrentTrack, PositionSeconds);

    /// <summary>
    /// Brings back volume, shuffle, repeat and the last song, loaded but paused at the position it was left at.
    /// A song that can no longer be loaded (file moved) is skipped without a message: the user did not ask for it.
    /// </summary>
    public async Task RestoreAsync(PlayerSession session)
    {
        Volume = Math.Clamp(session.Volume, 0, 1);
        _lastAudibleVolume = session.LastAudibleVolume is > 0 and <= 1 ? session.LastAudibleVolume : 1;
        _queue.Shuffle = IsShuffleOn = session.Shuffle;
        _queue.Repeat = Repeat = session.Repeat;

        if (session.Track is not { } track) return;

        // Load before queueing, so a song that is gone leaves the queue empty instead of stuck on it.
        if (!await TryLoadAsync(track, play: false))
        {
            ErrorMessage = null;
            return;
        }

        _queue.SetQueue([track], 0);
        var position = Math.Clamp(session.PositionSeconds, 0, DurationSeconds);
        _player.Seek(TimeSpan.FromSeconds(position));
        SetPosition(position);
    }

    /// <summary>
    /// Runs <paramref name="action"/> with the song's file closed, if that song is the one loaded: while it is open for
    /// playing, nothing else can write to it (Windows refuses). Afterwards the song is loaded again at the same position,
    /// and plays on if it was playing.
    /// </summary>
    public async Task<T> WithFileReleasedAsync<T>(Guid recordingId, Func<Task<T>> action)
    {
        if (CurrentTrack is not { } track || track.RecordingId != recordingId || !HasMedia) return await action();

        var position = _player.Position;
        var wasPlaying = _player.State == PlaybackState.Playing;
        _player.Release();
        try
        {
            return await action();
        }
        finally
        {
            try
            {
                await _player.LoadAsync(MediaSources.FromUri(track.MediaUri));
                _player.Seek(position);
                SetPosition(position.TotalSeconds);
                if (wasPlaying) _player.Play();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                ErrorMessage = $"Cannot play {track.Title} again: {ex.Message}";
            }
        }
    }

    /// <summary>Copies the player's position to <see cref="PositionSeconds"/> without seeking.</summary>
    public void RefreshPosition() => SetPosition(_player.Position.TotalSeconds);

    [RelayCommand(CanExecute = nameof(HasMedia))]
    private void TogglePlayPause()
    {
        if (_player.State == PlaybackState.Playing) _player.Pause();
        else _player.Play();
    }

    [RelayCommand(CanExecute = nameof(HasMedia))]
    private async Task NextAsync()
    {
        if (_queue.MoveNext(autoAdvance: false) is not null)
            await PlayCurrentAsync(skipFailures: true);
    }

    [RelayCommand(CanExecute = nameof(HasMedia))]
    private async Task PreviousAsync()
    {
        var before = _queue.Current;
        var previous = _player.Position > RestartThreshold ? before : _queue.MovePrevious();

        if (previous is null) return;
        if (previous == before) // far into the song, or nothing before it: start it over
        {
            _player.Seek(TimeSpan.Zero);
            RefreshPosition();
            Seeked?.Invoke(this, EventArgs.Empty);
            return;
        }

        await PlayCurrentAsync(skipFailures: true);
    }

    [RelayCommand]
    private void ToggleShuffle()
    {
        _queue.Shuffle = !_queue.Shuffle;
        IsShuffleOn = _queue.Shuffle;
    }

    /// <summary>Off, then repeat all, then repeat one.</summary>
    [RelayCommand]
    private void CycleRepeat()
    {
        _queue.Repeat = _queue.Repeat switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off,
        };
        Repeat = _queue.Repeat;
    }

    [RelayCommand]
    private void ToggleQueue() => IsQueueOpen = !IsQueueOpen;

    [RelayCommand]
    private async Task PlayQueueItemAsync(QueueItem? item)
    {
        if (item is not null && _queue.JumpTo(item))
            await PlayCurrentAsync(skipFailures: false);
    }

    [RelayCommand]
    private void RemoveQueueItem(QueueItem? item)
    {
        if (item is not null) _queue.Remove(item);
    }

    [RelayCommand]
    private void ClearQueue() => _queue.ClearUpcoming();

    // Event handler of the player: runs on the UI thread, and an unexpected exception should be loud, hence async void.
    private async void OnEnded()
    {
        if (_queue.MoveNext(autoAdvance: true) is not null && await PlayCurrentAsync(skipFailures: true)) return;

        _player.Stop(); // end of the queue, or nothing left that can be played; rewinds for the next Play
        RefreshPosition();
    }

    /// <param name="skipFailures">Move on to the next song when this one cannot be loaded, at most once around the queue.</param>
    private async Task<bool> PlayCurrentAsync(bool skipFailures)
    {
        for (var attempt = 0; attempt < _queue.Items.Count; attempt++)
        {
            if (_queue.Current is not { } item) return false;
            if (await TryLoadAsync(item.Track, play: true)) return true;
            if (!skipFailures || _queue.MoveNext(autoAdvance: false) is null) return false;
        }

        return false;
    }

    private async Task<bool> TryLoadAsync(TrackSummary track, bool play)
    {
        try
        {
            await _player.LoadAsync(MediaSources.FromUri(track.MediaUri));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            ErrorMessage = $"Cannot play {track.Title}: the file was moved or deleted. Add its folder again to update the library.";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or UriFormatException or NotSupportedException)
        {
            ErrorMessage = $"Cannot play {track.Title}: {ex.Message}";
            return false;
        }

        ErrorMessage = null;
        Title = track.Title;
        Subtitle = track.Artist ?? "Unknown artist";
        Album = track.Album;
        ArtworkPath = track.ArtworkPath;
        CurrentTrack = track;
        HasMedia = true;
        SetPosition(0); // before the duration changes, so the slider cannot coerce an old position into a seek
        DurationSeconds = _player.Duration?.TotalSeconds ?? 0;
        if (play) _player.Play();
        TrackLoaded?.Invoke(this, EventArgs.Empty);
        return true;
    }

    partial void OnVolumeChanged(double value)
    {
        _player.Volume = (float)value;
        if (value > 0) _lastAudibleVolume = value;
    }

    /// <summary>Mutes by moving the volume to 0; the next call restores the last non-zero volume.</summary>
    [RelayCommand]
    private void ToggleMute() => Volume = Volume > 0 ? 0 : _lastAudibleVolume;

    partial void OnPositionSecondsChanged(double value)
    {
        if (_updatingPosition) return;
        _player.Seek(TimeSpan.FromSeconds(value));
        Seeked?.Invoke(this, EventArgs.Empty);
    }

    private void SetPosition(double seconds)
    {
        _updatingPosition = true;
        try
        {
            PositionSeconds = seconds;
        }
        finally
        {
            _updatingPosition = false;
        }
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }
}
