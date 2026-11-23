using Avalonia.Controls;

namespace Shirabe.Client.Views;

/// <summary>Opens web addresses in the browser. https only, whatever the caller was given.</summary>
public static class LinkLauncher
{
    public static void Open(Control owner, string? address)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            _ = TopLevel.GetTopLevel(owner)?.Launcher.LaunchUriAsync(uri);
    }
}