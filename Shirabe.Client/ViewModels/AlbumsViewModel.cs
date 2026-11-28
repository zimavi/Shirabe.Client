using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Shirabe.Client.Localization;
using Shirabe.Client.Services;
using Shirabe.Core.Library;

namespace Shirabe.Client.ViewModels;

/// <summary>One line of the album grid. The grid is a list of lines, so that only the visible ones are built.</summary>
public sealed record AlbumRow(IReadOnlyList<AlbumSummary> Albums);

/// <summary>Stands before the songs of one disc in the track list of an album with several discs.</summary>
public sealed record AlbumDiscHeading(string Text);

/// <summary>The library's albums, as a grid; one of them can be open, showing its songs.</summary>
public partial class AlbumsViewModel(ILibrary library, PlayerViewModel player, AppSettings settings) : ViewModelBase
{
    private IReadOnlyList<AlbumSummary> _all = [];

    /// <summary>What the search and the sort order leave, in the order shown.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<AlbumSummary> Albums { get; private set; } = [];

    [ObservableProperty] public partial IReadOnlyList<AlbumRow> Rows { get; private set; } = [];

    [ObservableProperty] public partial string? SearchText { get; set; }

    /// <summary>How many albums fit beside each other; the view sets it from its width.</summary>
    [ObservableProperty]
    public partial int Columns { get; set; } = 4;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAlbumOpen), nameof(OpenRows))]
    public partial AlbumSummary? OpenAlbum { get; private set; }

    [ObservableProperty] public partial string? ErrorMessage { get; private set; }

    public bool IsAlbumOpen => OpenAlbum is not null;

    /// <summary>The songs of the open album, with a heading before each disc when there are several.</summary>
    public IReadOnlyList<object> OpenRows
    {
        get
        {
            if (OpenAlbum is not { } album) return [];

            var discs = album.Tracks.GroupBy(t => t.DiscNumber ?? 1).ToList(); // already in disc order
            if (discs.Count < 2) return [.. album.Tracks];

            return
            [
                .. discs.SelectMany(disc => disc.Cast<object>()
                    .Prepend(new AlbumDiscHeading(Loc.Get("Albums_Disc", disc.Key)))),
            ];
        }
    }

    // The page reloads every time it opens (MainViewModel.ShowAlbumsAsync), so this follows a language change by itself.
    public string Summary => Loc.Get("Albums_Summary", _all.Count);

    public bool HasAlbums => _all.Count > 0;

    public bool ShowEmptyState => !HasAlbums && ErrorMessage is null;

    public bool ShowNoMatches => HasAlbums && Albums.Count == 0;

    /// <summary>The position of the sort order in the drop-down, which lists <see cref="AlbumSort"/> in its order.</summary>
    public int SortIndex
    {
        get => (int)settings.AlbumSort;
        set
        {
            var sort = (AlbumSort)value;
            if (!Enum.IsDefined(sort) || sort == settings.AlbumSort) return; // a drop-down reports -1 while it changes

            settings.AlbumSort = sort;
            OnPropertyChanged();
            Refresh();
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            _all = await library.GetAlbumsAsync();
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            // A broken database must not take the app down: say what happened and keep what was shown.
            ErrorMessage = Loc.Get("Albums_LoadFailed", ex.Message);
        }

        // The open album stays open with its new content; one that has no playable song any more closes.
        if (OpenAlbum is { } open) OpenAlbum = _all.FirstOrDefault(a => a.Id == open.Id);
        Refresh();
    }

    partial void OnSearchTextChanged(string? value) => Refresh();

    partial void OnColumnsChanged(int value) => BuildRows();

    private void Refresh()
    {
        var search = SearchText?.Trim();
        var found = string.IsNullOrEmpty(search) ? _all : _all.Where(a => Matches(a, search));

        var byTitle = StringComparer.OrdinalIgnoreCase;
        var ordered = settings.AlbumSort switch
        {
            AlbumSort.Artist => found.OrderBy(a => a.Artist is null).ThenBy(a => a.Artist, byTitle)
                .ThenBy(a => a.Year ?? int.MaxValue).ThenBy(a => a.Title, byTitle),
            AlbumSort.Year => found.OrderByDescending(a => a.Year ?? int.MinValue).ThenBy(a => a.Title, byTitle),
            _ => found.OrderBy(a => a.Title, byTitle).ThenBy(a => a.Artist, byTitle),
        };
        Albums = [.. ordered.ThenBy(a => a.Id)];
        BuildRows();

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasAlbums));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowNoMatches));
    }

    private void BuildRows() => Rows = [.. Albums.Chunk(Math.Max(1, Columns)).Select(row => new AlbumRow(row))];

    private static bool Matches(AlbumSummary album, string search)
    {
        bool Has(string? text) => text?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

        return Has(album.Title) || Has(album.Artist)
                                || album.Tracks.Any(t => Has(t.Track.Title) || Has(t.Track.Artist));
    }

    [RelayCommand]
    private void Open(AlbumSummary? album)
    {
        if (album is not null) OpenAlbum = album;
    }

    [RelayCommand]
    private void Back() => OpenAlbum = null;

    /// <summary>Plays an album from its first song, in order.</summary>
    [RelayCommand]
    private Task PlayAsync(AlbumSummary? album) => album is null ? Task.CompletedTask : StartAsync(album, false);

    /// <summary>Plays an album in random order.</summary>
    [RelayCommand]
    private Task ShuffleAsync(AlbumSummary? album) => album is null ? Task.CompletedTask : StartAsync(album, true);

    // Both set the player's shuffle switch, so that it says what is happening.
    private Task StartAsync(AlbumSummary album, bool shuffle)
    {
        if (player.IsShuffleOn != shuffle) player.ToggleShuffleCommand.Execute(null);
        return player.PlayTracksAsync(Songs(album), shuffle ? Random.Shared.Next(album.Tracks.Count) : 0);
    }

    /// <summary>Plays a song of the open album and queues the album after it, like the Songs page does with its list.</summary>
    [RelayCommand]
    private async Task PlayTrackAsync(AlbumTrack? track)
    {
        if (track is null || OpenAlbum is not { } album) return;

        var songs = Songs(album);
        var index = songs.FindIndex(t => t.RecordingId == track.Track.RecordingId);
        if (index < 0) await player.PlayTracksAsync([track.Track], 0); // the album changed under us
        else await player.PlayTracksAsync(songs, index);
    }

    [RelayCommand]
    private Task PlayNextAsync(AlbumSummary? album) =>
        album is null ? Task.CompletedTask : player.PlayNextAsync(Songs(album));

    [RelayCommand]
    private Task AddToQueueAsync(AlbumSummary? album) =>
        album is null ? Task.CompletedTask : player.EnqueueAsync(Songs(album));

    private static List<TrackSummary> Songs(AlbumSummary album) => [.. album.Tracks.Select(t => t.Track)];
}