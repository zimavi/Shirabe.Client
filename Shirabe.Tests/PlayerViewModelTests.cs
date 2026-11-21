using Shirabe.Client.ViewModels;
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
    public async Task OpenAsync_loads_the_file_and_starts_playing()
    {
        await _viewModel.OpenAsync(@"C:\music\Song One.mp3");

        var source = Assert.IsType<LocalFileSource>(_player.Loaded);
        Assert.Equal(@"C:\music\Song One.mp3", source.Path);
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

        await _viewModel.OpenAsync(@"C:\music\notes.txt");

        Assert.Contains("notes.txt", _viewModel.ErrorMessage);
        Assert.Contains("unsupported format", _viewModel.ErrorMessage);
        Assert.False(_viewModel.HasMedia);
        Assert.Equal(PlaybackState.Stopped, _player.State);
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

        await _viewModel.OpenAsync(@"C:\music\short.mp3");

        Assert.Empty(_player.Seeks);
        Assert.Equal(0, _viewModel.PositionSeconds);
    }

    [Fact]
    public async Task TogglePlayPause_pauses_and_resumes()
    {
        Assert.False(_viewModel.TogglePlayPauseCommand.CanExecute(null));
        await _viewModel.OpenAsync(@"C:\music\a.mp3");
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
        await _viewModel.OpenAsync(@"C:\music\a.mp3");
        _player.Position = TimeSpan.FromSeconds(120);
        _viewModel.RefreshPosition();

        _player.RaiseEnded();

        Assert.False(_viewModel.IsPlaying);
        Assert.Equal(0, _viewModel.PositionSeconds);
    }
}
