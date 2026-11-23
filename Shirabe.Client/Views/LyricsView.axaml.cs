using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Shirabe.Client.ViewModels;

namespace Shirabe.Client.Views;

public partial class LyricsView : UserControl
{
    private static readonly TimeSpan ScrollDuration = TimeSpan.FromMilliseconds(300);

    private readonly DispatcherTimer _scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private LyricsViewModel? _model;
    private DateTime _scrollStarted;
    private double _scrollFrom, _scrollTo;
    private bool _jumpNext = true; // new lyrics are shown at once; only the move from one line to the next glides

    public LyricsView()
    {
        InitializeComponent();
        _scrollTimer.Tick += OnScrollTick;
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
        if (e.PropertyName == nameof(LyricsViewModel.Lines)) _jumpNext = true;
        if (e.PropertyName is nameof(LyricsViewModel.CurrentIndex) or nameof(LyricsViewModel.Lines))
            Dispatcher.UIThread.Post(KeepCurrentLineInView, DispatcherPriority.Loaded);
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _scrollTimer.Stop();
    }

    // The current line sits near the middle of the panel; before the first line (or in plain lyrics) the text starts at the top.
    private void KeepCurrentLineInView()
    {
        if (_model is not { CurrentIndex: var index }) return;

        if (index < 0 || LinesList.ContainerFromIndex(index) is not Control row)
        {
            ScrollTo(0);
            return;
        }

        if (row.TranslatePoint(new Point(0, row.Bounds.Height / 2), Scroller) is not { } inViewport) return;

        ScrollTo(Math.Max(0, Scroller.Offset.Y + inViewport.Y - Scroller.Viewport.Height / 2));
    }

    // Glides to the line unless motion is reduced, the lyrics are new, or the line is far away (a seek): then it just is there.
    private void ScrollTo(double target)
    {
        var motion = TopLevel.GetTopLevel(this)?.Classes.Contains("motion") == true;
        var near = Math.Abs(target - Scroller.Offset.Y) < Scroller.Viewport.Height * 2;
        var glide = motion && near && !_jumpNext;
        _jumpNext = false;
        _scrollTimer.Stop();

        if (!glide)
        {
            Scroller.Offset = new Vector(0, target);
            return;
        }

        _scrollFrom = Scroller.Offset.Y;
        _scrollTo = target;
        _scrollStarted = DateTime.UtcNow;
        _scrollTimer.Start();
    }

    private void OnScrollTick(object? sender, EventArgs e)
    {
        var done = Math.Min(1, (DateTime.UtcNow - _scrollStarted) / ScrollDuration);
        var eased = 1 - Math.Pow(1 - done, 3); // fast start, soft landing
        Scroller.Offset = new Vector(0, _scrollFrom + (_scrollTo - _scrollFrom) * eased);
        if (done >= 1) _scrollTimer.Stop();
    }
}
