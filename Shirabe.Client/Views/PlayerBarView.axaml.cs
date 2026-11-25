using Avalonia.Controls;
using Avalonia.Input;
using Shirabe.Client.ViewModels;

namespace Shirabe.Client.Views;

public partial class PlayerBarView : UserControl
{
    public PlayerBarView()
    {
        InitializeComponent();
    }

    private void OnVolumeWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not PlayerViewModel viewModel || e.Delta.Y == 0) return;

        viewModel.ScrollVolume(Math.Sign(e.Delta.Y));
        e.Handled = true;
    }
}
