using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

using Shirabe.Client.Localization;

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
            Loc.Instance.LanguageChanged += (_, _) => UpdateMaximizeIcon();
        }
    }

    private void UpdateMaximizeIcon()
    {
        var maximized = Window?.WindowState == WindowState.Maximized;
        MaximizeIcon.IsVisible = !maximized;
        RestoreIcon.IsVisible = maximized;
        AutomationProperties.SetName(MaximizeButton, Loc.Get(maximized ? "Window_Restore" : "Window_Maximize"));
    }

    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        if (Window is { } window) window.WindowState = WindowState.Minimized;
    }

    private void OnToggleMaximize(object? sender, RoutedEventArgs e)
    {
        if (Window is { } window)
            window.WindowState =
                window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Window?.Close();
}