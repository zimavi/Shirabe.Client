using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

using Shirabe.Client.Localization;

namespace Shirabe.Client.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        // One handler for every button, templates included (a Click= handler does not work inside a DataTemplate).
        AddHandler(Button.ClickEvent, OnButtonClick);
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { Tag: string tag } button) return;

        if (button.Classes.Contains("copy")) _ = CopyAsync(button, tag);
        // Link buttons carry an address; https only, whatever the view model says.
        else if (Uri.TryCreate(tag, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(uri);
    }

    // The button's Tag holds its text, so a second click during the "Copied" moment still copies the right thing.
    private async Task CopyAsync(Button button, string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        await clipboard.SetTextAsync(text);
        button.Content = Loc.Get("About_Copied");
        await Task.Delay(1500);
        button.Content = text;
    }
}