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
    public SongsView()
    {
        InitializeComponent();

        // Tunnelling: a list item may mark Enter as handled before it bubbles up to the list.
        SongList.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
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

    private void PlaySelected()
    {
        if (DataContext is SongsViewModel viewModel && SongList.SelectedItem is TrackSummary track)
            viewModel.PlayCommand.Execute(track);
    }
}
