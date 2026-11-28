using System.ComponentModel;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;

namespace Shirabe.Client.Views;

public partial class AlbumsView : UserControl
{
    private const double TileSpace = 184; // a tile and the gap after it

    private AlbumsViewModel? _viewModel;

    // The album the context menu was asked for, and the tile that had the keyboard before an album opened.
    private AlbumSummary? _menuAlbum;
    private Control? _lastTile;

    public AlbumsView()
    {
        InitializeComponent();

        GridScroll.SizeChanged += (_, e) => SetColumns(e.NewSize.Width);

        // Right click and the menu key alike. Tunnelling: this must know the album before the menu opens.
        AlbumGrid.AddHandler(ContextRequestedEvent,
            (_, e) => _menuAlbum = (e.Source as Control)?.DataContext as AlbumSummary, RoutingStrategies.Tunnel);
        AlbumGrid.AddHandler(GotFocusEvent, (_, e) => _lastTile = e.Source as Control);

        // A disc heading is not a song: it cannot be selected, and the arrow keys pass over it.
        TrackList.ContainerPrepared += (_, e) =>
            e.Container.IsEnabled = TrackList.Items[e.Index] is not AlbumDiscHeading;
        // Tunnelling: a list item may mark Enter as handled before it bubbles up to the list.
        TrackList.AddHandler(KeyDownEvent, OnTrackKeyDown, RoutingStrategies.Tunnel);
    }

    // The view model outlives the view (a new view is built every time the page opens), so let go of it with the view.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _viewModel = DataContext as AlbumsViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = null;
    }

    private void SetColumns(double width)
    {
        if (DataContext is AlbumsViewModel viewModel && width > 0)
            viewModel.Columns = Math.Max(1, (int)(width / TileSpace));
    }

    // The button that was pressed disappears, so the keyboard focus is moved along.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AlbumsViewModel.OpenAlbum) || sender is not AlbumsViewModel viewModel) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (viewModel.IsAlbumOpen) PlayButton.Focus();
            else _lastTile?.Focus();
        });
    }

    private void OnMenuOpening(object? sender, CancelEventArgs e)
    {
        if (_menuAlbum is null) e.Cancel = true; // empty space
    }

    private void OnPlayClick(object? sender, RoutedEventArgs e) => Run(viewModel => viewModel.PlayCommand);

    private void OnPlayNextClick(object? sender, RoutedEventArgs e) => Run(viewModel => viewModel.PlayNextCommand);

    private void OnAddToQueueClick(object? sender, RoutedEventArgs e) => Run(viewModel => viewModel.AddToQueueCommand);

    private void Run(Func<AlbumsViewModel, ICommand> command)
    {
        if (DataContext is AlbumsViewModel viewModel && _menuAlbum is { } album)
            command(viewModel).Execute(album);
    }

    private void OnTrackDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Ignore double clicks on empty space or the scroll bar: only a row plays.
        if (e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null)
            PlaySelected();
    }

    private void OnTrackKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        PlaySelected();
        e.Handled = true;
    }

    private void PlaySelected()
    {
        if (DataContext is AlbumsViewModel viewModel && TrackList.SelectedItem is AlbumTrack track)
            viewModel.PlayTrackCommand.Execute(track);
    }
}