using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Shirabe.Core.Playback;
using Xunit;

namespace Shirabe.Tests;

public sealed class PlayerSessionTests : IDisposable
{
    private static readonly TrackSummary Song =
        new(Guid.NewGuid(), "Song", "Artist", "Album", TimeSpan.FromSeconds(120), "file:///C:/music/song.flac", "C:/cache/cover.jpg");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "shirabe-session-" + Guid.NewGuid());
    private readonly FakeAudioPlayer _player = new();
    private readonly PlayerViewModel _viewModel;

    public PlayerSessionTests()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        _viewModel = new PlayerViewModel(_player);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task Restore_brings_back_volume_shuffle_repeat_and_the_song_paused_at_its_position()
    {
        await _viewModel.RestoreAsync(new PlayerSession(0.4, 0.4, true, RepeatMode.One, Song, 42));

        Assert.Equal(0.4f, _player.Volume);
        Assert.True(_viewModel.IsShuffleOn);
        Assert.Equal(RepeatMode.One, _viewModel.Repeat);
        Assert.Equal("Song", _viewModel.Title);
        Assert.Equal("Artist", _viewModel.Subtitle);
        Assert.Equal("C:/cache/cover.jpg", _viewModel.ArtworkPath);
        Assert.Equal(PlaybackState.Paused, _player.State); // restoring never starts the music
        Assert.Equal([TimeSpan.FromSeconds(42)], _player.Seeks);
        Assert.Equal(42, _viewModel.PositionSeconds);
        Assert.Same(Song, _viewModel.CurrentTrack);
        Assert.True(_viewModel.TogglePlayPauseCommand.CanExecute(null));
        Assert.Null(_viewModel.ErrorMessage);
    }

    [Fact]
    public async Task A_restored_song_is_the_queue_so_next_songs_go_after_it()
    {
        await _viewModel.RestoreAsync(new PlayerSession(1, 1, false, RepeatMode.Off, Song, 0));
        var other = Song with { Title = "Other", MediaUri = "file:///C:/music/other.flac" };

        await _viewModel.EnqueueAsync(other);

        Assert.Equal(["Song", "Other"], _viewModel.QueueItems.Select(item => item.Track.Title));
        Assert.Equal(PlaybackState.Paused, _player.State); // queued behind the restored song, nothing started
    }

    [Fact]
    public async Task A_position_past_the_end_is_kept_inside_the_song()
    {
        await _viewModel.RestoreAsync(new PlayerSession(1, 1, false, RepeatMode.Off, Song, 99999));

        Assert.Equal(120, _viewModel.PositionSeconds);
    }

    [Fact]
    public async Task A_song_that_cannot_be_loaded_is_skipped_silently_but_the_rest_is_restored()
    {
        _player.LoadException = new FileNotFoundException();

        await _viewModel.RestoreAsync(new PlayerSession(0.3, 0.3, true, RepeatMode.All, Song, 10));

        Assert.Equal(0.3f, _player.Volume);
        Assert.True(_viewModel.IsShuffleOn);
        Assert.Equal(RepeatMode.All, _viewModel.Repeat);
        Assert.Null(_viewModel.ErrorMessage);
        Assert.False(_viewModel.HasMedia);
        Assert.Empty(_viewModel.QueueItems);
        Assert.Null(_viewModel.CurrentTrack);

        // Not stuck on the missing song: a new song starts as usual.
        _player.LoadException = null;
        await _viewModel.EnqueueAsync(Song);
        Assert.Equal(PlaybackState.Playing, _player.State);
    }

    [Fact]
    public async Task Without_a_song_only_the_settings_are_restored()
    {
        await _viewModel.RestoreAsync(new PlayerSession(0.7, 0.7, false, RepeatMode.All, null, 0));

        Assert.Equal(0.7f, _player.Volume);
        Assert.Equal(RepeatMode.All, _viewModel.Repeat);
        Assert.False(_viewModel.HasMedia);
    }

    [Fact]
    public async Task A_muted_session_unmutes_to_the_volume_before_the_mute()
    {
        await _viewModel.RestoreAsync(new PlayerSession(0, 0.35, false, RepeatMode.Off, null, 0));

        Assert.True(_viewModel.IsVolumeMuted);
        _viewModel.ToggleMuteCommand.Execute(null);

        Assert.Equal(0.35, _viewModel.Volume);
    }

    [Theory]
    [InlineData(-3, 5, 0)] // out of range volume
    [InlineData(7, 5, 1)]
    public async Task An_out_of_range_volume_is_clamped(double saved, double ignored, double expected)
    {
        await _viewModel.RestoreAsync(new PlayerSession(saved, ignored, false, RepeatMode.Off, null, 0));

        Assert.Equal(expected, _viewModel.Volume);
    }

    [Fact]
    public async Task Capture_returns_what_restore_needs()
    {
        await _viewModel.PlayAsync(Song);
        _viewModel.Volume = 0.25;
        _viewModel.ToggleShuffleCommand.Execute(null);
        _viewModel.CycleRepeatCommand.Execute(null);
        _viewModel.PositionSeconds = 30;

        var session = _viewModel.Capture();

        Assert.Equal(new PlayerSession(0.25, 0.25, true, RepeatMode.All, Song, 30), session);
    }

    [Fact]
    public async Task The_store_saves_and_loads_a_session_including_the_song()
    {
        var store = new SessionStore(Path.Combine(_folder, "session.json"), new AppSettings());
        Assert.Null(store.Load()); // no file yet

        await _viewModel.PlayAsync(Song);
        _viewModel.Volume = 0.6;
        _viewModel.CycleRepeatCommand.Execute(null);
        store.Attach(_viewModel);
        store.Flush();

        var loaded = Assert.IsType<PlayerSession>(store.Load());
        Assert.Equal(0.6, loaded.Volume);
        Assert.Equal(RepeatMode.All, loaded.Repeat);
        Assert.Equal(Song, loaded.Track);
        Assert.Contains("\"All\"", File.ReadAllText(Path.Combine(_folder, "session.json"))); // readable, not a number
    }

    [Fact]
    public void The_store_ignores_a_damaged_file_and_does_not_write_before_it_is_attached()
    {
        var path = Path.Combine(_folder, "session.json");
        var store = new SessionStore(path, new AppSettings());

        store.Flush();
        Assert.False(File.Exists(path)); // not attached: would only write defaults over a good file

        Directory.CreateDirectory(_folder);
        File.WriteAllText(path, "{ not json");
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task Switching_resume_off_deletes_the_file_and_switching_it_on_saves_again()
    {
        var path = Path.Combine(_folder, "session.json");
        var settings = new AppSettings();
        var store = new SessionStore(path, settings);
        await _viewModel.PlayAsync(Song);
        store.Attach(_viewModel);
        store.Flush();
        Assert.True(File.Exists(path));

        settings.ResumeLastSession = false;
        Assert.False(File.Exists(path)); // forgotten at once

        store.Flush(); // an exit or a change while off writes nothing
        Assert.False(File.Exists(path));

        settings.ResumeLastSession = true;
        Assert.Equal(Song, store.Load()?.Track); // saving resumed at once, with the current song
    }
}
