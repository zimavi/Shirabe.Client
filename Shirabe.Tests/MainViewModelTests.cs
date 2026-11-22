using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;
using Xunit;

namespace Shirabe.Tests;

public class MainViewModelTests
{
    private readonly FakeLibrary _library = new();
    private readonly PlayerViewModel _player;
    private readonly SongsViewModel _songs;

    public MainViewModelTests()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        _player = new PlayerViewModel(new FakeAudioPlayer());
        _songs = new SongsViewModel(_library, new FakeScanner(), new FakeFolderPicker(), _player);
    }

    [Fact]
    public void It_starts_on_the_home_page()
    {
        var viewModel = new MainViewModel(_player, _songs, null);

        Assert.IsType<HomeViewModel>(viewModel.CurrentPage);
        Assert.True(viewModel.IsHomeSelected);
        Assert.False(viewModel.IsSongsSelected);
    }

    [Fact]
    public async Task Showing_songs_navigates_there_and_loads_the_library_every_time()
    {
        _library.Tracks = [new TrackSummary(Guid.NewGuid(), "One", null, null, null, "file:///one.flac")];
        var viewModel = new MainViewModel(_player, _songs, null);

        await viewModel.ShowSongsCommand.ExecuteAsync(null);

        Assert.Same(_songs, viewModel.CurrentPage);
        Assert.True(viewModel.IsSongsSelected);
        Assert.False(viewModel.IsHomeSelected);
        Assert.Single(_songs.Tracks);

        viewModel.ShowHomeCommand.Execute(null);
        Assert.True(viewModel.IsHomeSelected);

        await viewModel.ShowSongsCommand.ExecuteAsync(null);
        Assert.Equal(2, _library.LoadCount);
    }

    [Fact]
    public void Without_a_library_songs_is_unavailable_and_the_reason_is_on_the_home_page()
    {
        var viewModel = new MainViewModel(_player, songs: null, startupError: null, libraryNotice: "library is damaged");

        Assert.False(viewModel.HasLibrary);
        Assert.False(viewModel.ShowSongsCommand.CanExecute(null));
        Assert.Equal("library is damaged", Assert.IsType<HomeViewModel>(viewModel.CurrentPage).Notice);
    }
}
