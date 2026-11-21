using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Shirabe.Client.ViewModels;

namespace Shirabe.Client.Views;

public partial class MainView : UserControl
{
    private readonly DispatcherTimer _positionTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public MainView()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        _positionTimer.Tick += (_, _) => Player?.RefreshPosition();
    }

    private PlayerViewModel? Player => (DataContext as MainViewModel)?.Player;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _positionTimer.Start();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _positionTimer.Stop();
    }

    private static void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    // Only the first dropped file is played; queueing several files comes with the queue.
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).FirstOrDefault(p => p is not null);
        if (path is not null && Player is { } player)
            await player.OpenAsync(path);
    }
}
