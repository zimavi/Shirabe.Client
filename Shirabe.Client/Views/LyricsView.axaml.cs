using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Shirabe.Client.ViewModels;

namespace Shirabe.Client.Views;

public partial class LyricsView : UserControl
{
    private LyricsViewModel? _model;

    public LyricsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null) _model.PropertyChanged -= OnModelChanged;
            _model = DataContext as LyricsViewModel;
            if (_model is not null) _model.PropertyChanged += OnModelChanged;
        };
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // After layout, so rows that were only just created have a position.
        if (e.PropertyName is nameof(LyricsViewModel.CurrentIndex) or nameof(LyricsViewModel.Lines))
            Dispatcher.UIThread.Post(KeepCurrentLineInView, DispatcherPriority.Loaded);
    }

    // The current line sits near the middle of the panel; before the first line (or in plain lyrics) the text starts at the top.
    private void KeepCurrentLineInView()
    {
        if (_model is not { CurrentIndex: var index }) return;

        if (index < 0 || LinesList.ContainerFromIndex(index) is not Control row)
        {
            Scroller.Offset = default;
            return;
        }

        if (row.TranslatePoint(new Point(0, row.Bounds.Height / 2), Scroller) is not { } inViewport) return;

        Scroller.Offset = new Vector(0, Math.Max(0, Scroller.Offset.Y + inViewport.Y - Scroller.Viewport.Height / 2));
    }
}
