using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Shirabe.Client.Views;

public partial class TitleBarView : UserControl
{
    public TitleBarView()
    {
        InitializeComponent();
    }

    private Window? Window => TopLevel.GetTopLevel(this) as Window;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (Window is { } window)
        {
            window.PropertyChanged += (_, args) =>
            {
                if (args.Property == Window.WindowStateProperty) UpdateMaximizeIcon();
            };
            UpdateMaximizeIcon();
        }
    }

    private void UpdateMaximizeIcon()
    {
        var maximized = Window?.WindowState == WindowState.Maximized;
        MaximizeIcon.IsVisible = !maximized;
        RestoreIcon.IsVisible = maximized;
        AutomationProperties.SetName(MaximizeButton, maximized ? "Restore" : "Maximize");
    }

    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        if (Window is { } window) window.WindowState = WindowState.Minimized;
    }

    private void OnToggleMaximize(object? sender, RoutedEventArgs e)
    {
        if (Window is { } window)
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Window?.Close();
}
