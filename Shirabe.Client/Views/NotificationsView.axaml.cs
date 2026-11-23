using Avalonia.Controls;

namespace Shirabe.Client.Views;

public partial class NotificationsView : UserControl
{
    public NotificationsView()
    {
        InitializeComponent();
        // One handler for the buttons of every card (a Click= handler does not work inside a DataTemplate).
        AddHandler(Button.ClickEvent, (_, e) =>
        {
            if (e.Source is Button { Tag: string address }) LinkLauncher.Open(this, address);
        });
    }
}