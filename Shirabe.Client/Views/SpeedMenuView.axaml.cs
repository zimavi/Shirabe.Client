using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Shirabe.Client.ViewModels;

namespace Shirabe.Client.Views;

public partial class SpeedMenuView : UserControl
{
    public SpeedMenuView()
    {
        InitializeComponent();
    }

    // Not a Command binding: closing the menu first would take the view model away before the command ran.
    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var player = DataContext as PlayerViewModel;
        if (this.FindLogicalAncestorOfType<Popup>() is { } popup) popup.IsOpen = false;
        player?.OpenAudioSettingsCommand.Execute(null);
    }
}
