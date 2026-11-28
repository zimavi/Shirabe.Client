using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;

namespace Shirabe.Client.Views;

public partial class MainView : UserControl
{
    private readonly DispatcherTimer _positionTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private TopLevel? _topLevel;

    public MainView()
    {
        InitializeComponent();

        // The class lets any page style a drop target, see ".dragover Panel.dropzone" in Controls.axaml.
        AddHandler(DragDrop.DragEnterEvent, (_, _) => Classes.Set("dragover", true));
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => Classes.Set("dragover", false));
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        _positionTimer.Tick += (_, _) => Player?.RefreshPosition();
    }

    private PlayerViewModel? Player => (DataContext as MainViewModel)?.Player;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _positionTimer.Start();

        // On the window, not on this view: with nothing focused, key presses never reach the view.
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyDownEvent, OnKeyDownFirst, RoutingStrategies.Tunnel);
        _topLevel?.AddHandler(KeyDownEvent, OnKeyDownLast, RoutingStrategies.Bubble);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _positionTimer.Stop();
        _topLevel?.RemoveHandler(KeyDownEvent, OnKeyDownFirst);
        _topLevel?.RemoveHandler(KeyDownEvent, OnKeyDownLast);
        _topLevel = null;
    }

    // Shortcuts run before the focused control sees the key (a list would swallow Space)...
    private void OnKeyDownFirst(object? sender, KeyEventArgs e)
    {
        // With nothing focused, no control wants any key, and Avalonia would use an arrow to focus a button.
        if (!IsNavigationKey(e) || e.Source is TopLevel) RunShortcut(e);
    }

    // ...except the plain keys controls move around with: those are shortcuts only when no control wanted them.
    private void OnKeyDownLast(object? sender, KeyEventArgs e)
    {
        if (IsNavigationKey(e)) RunShortcut(e);
        else if (e is { Key: Key.Escape, KeyModifiers: KeyModifiers.None } && DataContext is MainViewModel viewModel)
            e.Handled = viewModel.GoBack(); // dialogs handle Esc themselves, before it gets here
    }

    private static bool IsNavigationKey(KeyEventArgs e) =>
        e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift
        && e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown
            or Key.Enter;

    private void RunShortcut(KeyEventArgs e)
    {
        if (e.Handled || DataContext is not MainViewModel viewModel) return;

        // Typing keeps its keys, and so does a shortcut button that is waiting for one.
        if (e.Source is TextBox or Button { DataContext: ShortcutRowViewModel { IsCapturing: true } }) return;

        // Space presses the focused button, switch or menu item.
        if (e is { Key: Key.Space, KeyModifiers: KeyModifiers.None }
            && e.Source is Button or ComboBox or ComboBoxItem or MenuItem) return;

        var action = e.Key switch
        {
            Key.MediaPlayPause => ShortcutAction.PlayPause,
            Key.MediaNextTrack => ShortcutAction.Next,
            Key.MediaPreviousTrack => ShortcutAction.Previous,
            _ => Shortcuts.Find(viewModel.Settings.Settings, Shortcuts.Gesture(
                e.KeyModifiers.HasFlag(KeyModifiers.Control), e.KeyModifiers.HasFlag(KeyModifiers.Alt),
                e.KeyModifiers.HasFlag(KeyModifiers.Shift), e.KeyModifiers.HasFlag(KeyModifiers.Meta),
                e.Key.ToString())),
        };
        if (action is { } found) e.Handled = viewModel.Run(found);
    }

    private static void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    // Only the first dropped file is played; queueing several files comes with the queue.
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        Classes.Set("dragover", false);
        var path = e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath())
            .FirstOrDefault(p => p is not null);
        if (path is not null && Player is { } player)
            await player.OpenAsync(path);
    }
}