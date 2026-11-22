using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;

namespace Shirabe.Client.Views;

public partial class SongsView : UserControl
{
    // The row the context menu was opened on.
    private TrackSummary? _menuTrack;

    public SongsView()
    {
        InitializeComponent();

        // Tunnelling: a list item may mark Enter as handled before it bubbles up to the list.
        SongList.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        SongList.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Ignore double clicks on empty space or the scroll bar: only a row plays.
        if (e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null)
            PlaySelected();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        PlaySelected();
        e.Handled = true;
    }

    // Right click: the row under the pointer is what the menu is about, and it becomes the selected row.
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(SongList).Properties.IsRightButtonPressed) return;

        var row = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        _menuTrack = row?.DataContext as TrackSummary;
        if (_menuTrack is not null) SongList.SelectedItem = _menuTrack;
    }

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _menuTrack ??= SongList.SelectedItem as TrackSummary; // opened with the keyboard
        if (_menuTrack is null) e.Cancel = true; // right click on empty space
    }

    private void OnMenuClosed(object? sender, RoutedEventArgs e) => _menuTrack = null;

    private void OnPlayClick(object? sender, RoutedEventArgs e) => Run(viewModel => viewModel.PlayCommand);

    private void OnPlayNextClick(object? sender, RoutedEventArgs e) => Run(viewModel => viewModel.PlayNextCommand);

    private void OnAddToQueueClick(object? sender, RoutedEventArgs e) => Run(viewModel => viewModel.AddToQueueCommand);

    private void Run(Func<SongsViewModel, System.Windows.Input.ICommand> command)
    {
        if (DataContext is SongsViewModel viewModel && _menuTrack is { } track)
            command(viewModel).Execute(track);
    }

    private void PlaySelected()
    {
        if (DataContext is SongsViewModel viewModel && SongList.SelectedItem is TrackSummary track)
            viewModel.PlayCommand.Execute(track);
    }
}
