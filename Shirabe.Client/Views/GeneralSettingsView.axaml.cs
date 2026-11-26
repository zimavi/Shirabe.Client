using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Shirabe.Client.Views;

public partial class GeneralSettingsView : UserControl
{
    public GeneralSettingsView()
    {
        InitializeComponent();
    }

    private void OnLinkClick(object? sender, RoutedEventArgs e) =>
        LinkLauncher.Open(this, (sender as Button)?.Tag as string);
}