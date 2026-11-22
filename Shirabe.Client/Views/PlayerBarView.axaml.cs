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

    // One wheel notch is 1%. Rounding first keeps the volume on whole percents after a drag.
    private void OnVolumeWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not PlayerViewModel viewModel || e.Delta.Y == 0) return;

        var percent = Math.Round(viewModel.Volume * 100) + Math.Sign(e.Delta.Y);
        viewModel.Volume = Math.Clamp(percent, 0, 100) / 100;
        e.Handled = true;
    }
}
