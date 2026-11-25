using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Shirabe.Client.Views;

public partial class LastFmView : UserControl
{
    public LastFmView()
    {
        InitializeComponent();
        // The buttons that carry an address open it; the others run commands.
        AddHandler(Button.ClickEvent, OnButtonClick);
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { Tag: string address }) LinkLauncher.Open(this, address);
    }
}