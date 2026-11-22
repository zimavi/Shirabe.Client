using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Playback;

namespace Shirabe.Client.Views;

public partial class QueueView : UserControl
{
    public QueueView()
    {
        InitializeComponent();
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Only a row jumps; a double click on a remove button or empty space does not.
        if (e.Source is Visual source
            && source.FindAncestorOfType<Button>(includeSelf: true) is null
            && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null
            && DataContext is PlayerViewModel player
            && QueueList.SelectedItem is QueueItem item)
            player.PlayQueueItemCommand.Execute(item);
    }
}
