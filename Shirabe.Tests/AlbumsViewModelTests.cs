using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;

using Xunit;

namespace Shirabe.Tests;

public class AlbumsViewModelTests
{
    private readonly FakeLibrary _library = new();
    private readonly FakeAudioPlayer _audio = new();
    private readonly AppSettings _settings = new();
    private readonly PlayerViewModel _player;
    private readonly AlbumsViewModel _viewModel;

    public AlbumsViewModelTests()
    {
        SynchronizationContext.SetSynchronizationContext(null); // run UI-thread hops inline
        _player = new PlayerViewModel(_audio);
        _viewModel = new AlbumsViewModel(_library, _player, _settings);
    }

    [Fact]
    public async Task Albums_are_sorted_by_title_artist_then_year_or_newest_first()
    {
        _library.Albums =
        [
            Album("Delta", "Beta", 2001),
            Album("alpha", "Beta", null),
            Album("Charlie", "Beta", 1999),
            Album("Bravo", null, 2010),
            Album("Echo", "abba", 1980),
        ];
        await _viewModel.LoadAsync();

        Assert.Equal(["alpha", "Bravo", "Charlie", "Delta", "Echo"], Titles());

        _viewModel.SortIndex = (int)AlbumSort.Artist;
        Assert.Equal(["Echo", "Charlie", "Delta", "alpha", "Bravo"], Titles());
        Assert.Equal(AlbumSort.Artist, _settings.AlbumSort);

        _viewModel.SortIndex = (int)AlbumSort.Year;
        Assert.Equal(["Bravo", "Delta", "Charlie", "Echo", "alpha"], Titles());

        _viewModel.SortIndex = -1; // what a drop-down reports while it changes
        Assert.Equal(AlbumSort.Year, _settings.AlbumSort);
    }

    [Theory]
    [InlineData("night", "Night Drive")]
    [InlineData("  ARIA ", "Second")]
    [InlineData("hidden gem", "Second")]
    [InlineData("guest", "Second")]
    public async Task Search_matches_album_album_artist_song_and_song_artist(string search, string expected)
    {
        _library.Albums =
        [
            Album("Night Drive", "Kou", 2000),
            Album("Second", "Aria", 2001, Song("Hidden Gem", artist: "Some Guest")),
        ];
        await _viewModel.LoadAsync();

        _viewModel.SearchText = search;

        Assert.Equal([expected], Titles());
        Assert.False(_viewModel.ShowNoMatches);
    }

    [Fact]
    public async Task A_search_without_matches_is_not_an_empty_library()
    {
        _library.Albums = [Album("One")];
        await _viewModel.LoadAsync();

        _viewModel.SearchText = "zzz";

        Assert.True(_viewModel.ShowNoMatches);
        Assert.False(_viewModel.ShowEmptyState);
        Assert.Equal("In the library: 1", _viewModel.Summary);
    }

    [Fact]
    public async Task The_grid_is_cut_into_rows_of_the_given_width()
    {
        _library.Albums = [.. Enumerable.Range(0, 7).Select(i => Album($"A{i}"))];
        await _viewModel.LoadAsync();

        _viewModel.Columns = 3;

        Assert.Equal([3, 3, 1], _viewModel.Rows.Select(r => r.Albums.Count));
    }

    [Fact]
    public async Task Disc_headings_appear_only_with_several_discs()
    {
        var one = Album("One", tracks: [Song("a", disc: null), Song("b", disc: 1)]);
        var two = Album("Two", tracks: [Song("a", disc: 1), Song("b", disc: 2), Song("c", disc: 2)]);
        _library.Albums = [one, two];
        await _viewModel.LoadAsync();

        _viewModel.OpenCommand.Execute(one);
        Assert.All(_viewModel.OpenRows, row => Assert.IsType<AlbumTrack>(row));

        _viewModel.OpenCommand.Execute(two);
        Assert.Equal(["Disc 1", "a", "Disc 2", "b", "c"],
            _viewModel.OpenRows.Select(r => r is AlbumDiscHeading h ? h.Text : ((AlbumTrack)r).Track.Title));
    }

    [Fact]
    public async Task Play_and_shuffle_set_the_shuffle_switch_and_queue_the_album()
    {
        var album = Album("One", tracks: [Song("a"), Song("b"), Song("c")]);

        await _viewModel.ShuffleCommand.ExecuteAsync(album);
        Assert.True(_player.IsShuffleOn);
        Assert.Equal(["a", "b", "c"], Queue().Order());

        await _viewModel.PlayCommand.ExecuteAsync(album);
        Assert.False(_player.IsShuffleOn);
        Assert.Equal(["a", "b", "c"], Queue());
        Assert.Equal("a", _player.CurrentTrack?.Title);
    }

    [Fact]
    public async Task A_song_of_the_open_album_plays_with_the_album_around_it()
    {
        var album = Album("One", tracks: [Song("a"), Song("b"), Song("c")]);
        _viewModel.OpenCommand.Execute(album);

        await _viewModel.PlayTrackCommand.ExecuteAsync(album.Tracks[1]);

        Assert.Equal(["a", "b", "c"], Queue());
        Assert.Equal("b", _player.CurrentTrack?.Title);
    }

    [Fact]
    public async Task Play_next_and_add_to_queue_keep_the_albums_order()
    {
        await _viewModel.PlayCommand.ExecuteAsync(Album("Playing", tracks: [Song("p1"), Song("p2")]));

        await _viewModel.AddToQueueCommand.ExecuteAsync(Album("Later", tracks: [Song("l1"), Song("l2")]));
        await _viewModel.PlayNextCommand.ExecuteAsync(Album("Next", tracks: [Song("n1"), Song("n2")]));

        Assert.Equal(["p1", "n1", "n2", "p2", "l1", "l2"], Queue());
        Assert.Equal("p1", _player.CurrentTrack?.Title);
    }

    [Fact]
    public async Task Play_next_with_nothing_playing_starts_the_album_from_its_first_song()
    {
        await _viewModel.PlayNextCommand.ExecuteAsync(Album("One", tracks: [Song("a"), Song("b")]));

        Assert.Equal(["a", "b"], Queue());
        Assert.Equal("a", _player.CurrentTrack?.Title);
    }

    [Fact]
    public async Task Reloading_keeps_the_open_album_until_it_is_gone()
    {
        var album = Album("One");
        _library.Albums = [album];
        await _viewModel.LoadAsync();
        _viewModel.OpenCommand.Execute(album);

        _library.Albums = [album with { Title = "Renamed" }];
        await _viewModel.LoadAsync();
        Assert.Equal("Renamed", _viewModel.OpenAlbum?.Title);

        _library.Albums = [];
        await _viewModel.LoadAsync();
        Assert.False(_viewModel.IsAlbumOpen);
    }

    [Fact]
    public async Task A_library_that_fails_to_load_shows_a_message_and_keeps_the_old_list()
    {
        _library.Albums = [Album("One")];
        await _viewModel.LoadAsync();
        _library.Failure = new InvalidOperationException("disk error");

        await _viewModel.LoadAsync();

        Assert.Contains("disk error", _viewModel.ErrorMessage);
        Assert.Single(_viewModel.Albums);
    }

    [Fact]
    public async Task The_sidebar_returns_to_the_open_album_and_a_second_click_or_escape_leaves_it()
    {
        var album = Album("One");
        _library.Albums = [album];
        var shell = new MainViewModel(_player, null, null, albums: _viewModel);

        await shell.ShowAlbumsCommand.ExecuteAsync(null);
        _viewModel.OpenCommand.Execute(album);
        shell.ShowHomeCommand.Execute(null);
        Assert.False(shell.GoBack()); // Escape elsewhere does not touch the album

        await shell.ShowAlbumsCommand.ExecuteAsync(null);
        Assert.True(_viewModel.IsAlbumOpen);

        await shell.ShowAlbumsCommand.ExecuteAsync(null);
        Assert.False(_viewModel.IsAlbumOpen);

        _viewModel.OpenCommand.Execute(album);
        _player.IsQueueOpen = true;
        Assert.True(shell.GoBack()); // the panel first
        Assert.True(_viewModel.IsAlbumOpen);
        Assert.True(shell.GoBack());
        Assert.False(_viewModel.IsAlbumOpen);
        Assert.False(shell.GoBack());
    }

    private string[] Titles() => [.. _viewModel.Albums.Select(a => a.Title)];

    private string[] Queue() => [.. _player.QueueItems.Select(i => i.Track.Title)];

    private static AlbumSummary Album(string title, string? artist = "Artist", int? year = 2000,
        params AlbumTrack[] tracks) =>
        new(Guid.NewGuid(), title, artist, year, tracks.Length > 0 ? tracks : [Song(title + " song")]);

    private static AlbumTrack Song(string title, string? artist = "Artist", int? disc = 1) =>
        new(new TrackSummary(Guid.NewGuid(), title, artist, null, null, $"file:///C:/Music/{title}.flac"), disc, null);
}