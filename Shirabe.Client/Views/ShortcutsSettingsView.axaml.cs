using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;

namespace Shirabe.Client.Views;

public partial class ShortcutsSettingsView : UserControl
{
    // The key that was just captured, until it is released.
    private Key _swallowUp = Key.None;

    public ShortcutsSettingsView()
    {
        InitializeComponent();

        AddHandler(KeyDownEvent, OnCaptureKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnCaptureKeyUp, RoutingStrategies.Tunnel);
        AddHandler(LostFocusEvent, (_, e) => (Row(e.Source))?.Cancel());
    }

    private static ShortcutRowViewModel? Row(object? source) =>
        (source as Button)?.DataContext as ShortcutRowViewModel;

    private void OnCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShortcutsSettingsViewModel page ||
            Row(e.Source) is not { IsCapturing: true } row) return;

        var gesture = Shortcuts.Gesture(
            e.KeyModifiers.HasFlag(KeyModifiers.Control), e.KeyModifiers.HasFlag(KeyModifiers.Alt),
            e.KeyModifiers.HasFlag(KeyModifiers.Shift), e.KeyModifiers.HasFlag(KeyModifiers.Meta), e.Key.ToString());
        e.Handled = page.Press(row, e.Key.ToString(), gesture);
        if (e.Handled) _swallowUp = e.Key;
    }

    // A button clicks when Space is released, which would start waiting for a key again.
    private void OnCaptureKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != _swallowUp) return;
        _swallowUp = Key.None;
        e.Handled = true;
    }
}