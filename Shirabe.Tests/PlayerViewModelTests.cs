using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Shirabe.Core.Media;
using Shirabe.Core.Playback;
using Xunit;

namespace Shirabe.Tests;

public class PlayerViewModelTests
{
    private readonly FakeAudioPlayer _player = new();
    private readonly PlayerViewModel _viewModel;

    public PlayerViewModelTests()
    {
        // The view model posts player events to the context it was created on; run them inline in tests.
        SynchronizationContext.SetSynchronizationContext(null);
        _viewModel = new PlayerViewModel(_player);
    }

    [Fact]
    public void Volume_starts_at_the_players_volume_and_changes_it()
    {
        var player = new FakeAudioPlayer { Volume = 0.6f };
        var viewModel = new PlayerViewModel(player);
        Assert.Equal(0.6, viewModel.Volume, precision: 3);

        viewModel.Volume = 0.25;

        Assert.Equal(0.25f, player.Volume);
    }

    [Fact]
    public void ToggleMute_zeroes_the_volume_and_restores_the_previous_one()
    {
        _viewModel.Volume = 0.4;

        _viewModel.ToggleMuteCommand.Execute(null);
        Assert.Equal(0, _viewModel.Volume);
        Assert.Equal(0f, _player.Volume);

        _viewModel.ToggleMuteCommand.Execute(null);
        Assert.Equal(0.4, _viewModel.Volume);
    }

    [Fact]
    public void ToggleMute_restores_full_volume_when_the_player_started_silent()
    {
        var viewModel = new PlayerViewModel(new FakeAudioPlayer { Volume = 0f });

        viewModel.ToggleMuteCommand.Execute(null);

        Assert.Equal(1, viewModel.Volume);
    }

    [Theory]
    [InlineData(0.0, "0%", true, false, false, false)]
    [InlineData(0.2, "20%", false, true, false, false)]
    [InlineData(0.5, "50%", false, false, true, false)]
    [InlineData(0.9, "90%", false, false, false, true)]
    public void Volume_text_and_icon_state_follow_the_volume(
        double volume, string text, bool muted, bool low, bool medium, bool high)
    {
        _viewModel.Volume = volume;

        Assert.Equal(text, _viewModel.VolumeText);
        Assert.Equal((muted, low, medium, high),
            (_viewModel.IsVolumeMuted, _viewModel.IsVolumeLow, _viewModel.IsVolumeMedium, _viewModel.IsVolumeHigh));
    }

    [Fact]
    public async Task PlayPauseText_says_what_the_next_press_does()
    {
        Assert.Equal("Play", _viewModel.PlayPauseText);

        await _viewModel.OpenAsync(Path.Combine(Path.GetTempPath(), "Song One.mp3"));
        Assert.Equal("Pause", _viewModel.PlayPauseText);

        _viewModel.TogglePlayPauseCommand.Execute(null);
        Assert.Equal("Play", _viewModel.PlayPauseText);
    }

    [Fact]
    public async Task OpenAsync_loads_the_file_and_starts_playing()
    {
        await _viewModel.OpenAsync(Path.Combine(Path.GetTempPath(), "Song One.mp3"));

        var source = Assert.IsType<LocalFileSource>(_player.Loaded);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "Song One.mp3"), source.Path);
        Assert.Equal(PlaybackState.Playing, _player.State);
        Assert.Equal("Song One", _viewModel.Title);
        Assert.Equal(120, _viewModel.DurationSeconds);
        Assert.True(_viewModel.HasMedia);
        Assert.True(_viewModel.IsPlaying);
        Assert.Null(_viewModel.ErrorMessage);
    }

    [Fact]
    public async Task OpenAsync_shows_an_error_when_the_file_cannot_be_played()
    {
        _player.LoadException = new InvalidOperationException("unsupported format");

        await _viewModel.OpenAsync(Path.Combine(Path.GetTempPath(), "notes.txt"));

        Assert.Contains("notes", _viewModel.ErrorMessage);
        Assert.Contains("unsupported format", _viewModel.ErrorMessage);
        Assert.False(_viewModel.HasMedia);
        Assert.Equal(PlaybackState.Stopped, _player.State);
    }

    [Fact]
    public async Task A_file_that_was_moved_or_deleted_gets_a_helpful_message()
    {
        _player.LoadException = new FileNotFoundException(@"Could not find file 'C:\music\gone.flac'.");

        await _viewModel.OpenAsync(Path.Combine(Path.GetTempPath(), "gone.flac"));

        Assert.Contains("moved or deleted", _viewModel.ErrorMessage);
        Assert.DoesNotContain("Could not find file", _viewModel.ErrorMessage); // no raw exception text
        Assert.False(_viewModel.HasMedia);
    }

    [Fact]
    public async Task A_dropped_file_is_shown_as_a_local_file()
    {
        await _viewModel.OpenAsync(Path.Combine(Path.GetTempPath(), "Song One.mp3"));

        Assert.Equal("Local file", _viewModel.Subtitle);
    }

    [Fact]
    public async Task PlayAsync_opens_the_track_from_its_media_address_and_shows_its_artist()
    {
        var path = Path.Combine(Path.GetTempPath(), "Song One.flac");
        var track = new TrackSummary(Guid.NewGuid(), "Song One", "Artist A", "Album X", null, new Uri(path).AbsoluteUri);

        await _viewModel.PlayAsync(track);

        Assert.Equal(path, Assert.IsType<LocalFileSource>(_player.Loaded).Path);
        Assert.Equal(PlaybackState.Playing, _player.State);
        Assert.Equal(("Song One", "Artist A"), (_viewModel.Title, _viewModel.Subtitle));
    }

    [Fact]
    public async Task A_track_without_an_artist_is_shown_as_unknown()
    {
        var track = new TrackSummary(Guid.NewGuid(), "Song", null, null, null, new Uri(Path.Combine(Path.GetTempPath(), "s.flac")).AbsoluteUri);

        await _viewModel.PlayAsync(track);

        Assert.Equal("Unknown artist", _viewModel.Subtitle);
    }

    [Fact]
    public async Task A_track_from_an_unsupported_kind_of_address_shows_an_error_and_plays_nothing()
    {
        var track = new TrackSummary(Guid.NewGuid(), "Remote Song", "A", null, null, "https://example.test/song.mp3");

        await _viewModel.PlayAsync(track);

        Assert.Contains("Remote Song", _viewModel.ErrorMessage);
        Assert.Null(_player.Loaded);
        Assert.False(_viewModel.HasMedia);
    }

    [Fact]
    public void RefreshPosition_follows_the_player_without_seeking()
    {
        _player.Position = TimeSpan.FromSeconds(30);

        _viewModel.RefreshPosition();

        Assert.Equal(30, _viewModel.PositionSeconds);
        Assert.Empty(_player.Seeks);
    }

    [Fact]
    public void Changing_the_position_from_the_slider_seeks()
    {
        _viewModel.PositionSeconds = 50;

        Assert.Equal([TimeSpan.FromSeconds(50)], _player.Seeks);
    }

    [Fact]
    public async Task Opening_a_shorter_file_does_not_seek_to_the_old_position()
    {
        _player.Position = TimeSpan.FromSeconds(100);
        _viewModel.RefreshPosition();

        await _viewModel.OpenAsync(Path.Combine(Path.GetTempPath(), "short.mp3"));

        Assert.Empty(_player.Seeks);
        Assert.Equal(0, _viewModel.PositionSeconds);
    }

    [Fact]
    public async Task TogglePlayPause_pauses_and_resumes()
    {
        Assert.False(_viewModel.TogglePlayPauseCommand.CanExecute(null));
        await _viewModel.OpenAsync(Path.Combine(Path.GetTempPath(), "a.mp3"));
        Assert.True(_viewModel.TogglePlayPauseCommand.CanExecute(null));

        _viewModel.TogglePlayPauseCommand.Execute(null);
        Assert.Equal(PlaybackState.Paused, _player.State);
        Assert.False(_viewModel.IsPlaying);

        _viewModel.TogglePlayPauseCommand.Execute(null);
        Assert.Equal(PlaybackState.Playing, _player.State);
        Assert.True(_viewModel.IsPlaying);
    }

    [Fact]
    public async Task When_playback_ends_the_position_returns_to_the_start()
    {
        await _viewModel.OpenAsync(Path.Combine(Path.GetTempPath(), "a.mp3"));
        _player.Position = TimeSpan.FromSeconds(120);
        _viewModel.RefreshPosition();

        _player.RaiseEnded();

        Assert.False(_viewModel.IsPlaying);
        Assert.Equal(0, _viewModel.PositionSeconds);
    }
}
