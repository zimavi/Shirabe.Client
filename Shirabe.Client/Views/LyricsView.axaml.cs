using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Shirabe.Client.ViewModels;

namespace Shirabe.Client.Views;

public partial class LyricsView : UserControl
{
    private static readonly TimeSpan GlideDuration = TimeSpan.FromMilliseconds(300);

    private LyricsViewModel? _model;
    private bool _frameRequested;
    private bool _attached;

    // The glide to the current line: from, to, and the frame time it started at (taken from the first frame after it was asked for).
    private bool _gliding;
    private TimeSpan? _glideStarted;
    private double _glideFrom, _glideTo;
    private bool _jumpNext = true; // new lyrics are shown at once; only the move from one line to the next glides

    public LyricsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null) _model.PropertyChanged -= OnModelChanged;
            _model = DataContext as LyricsViewModel;
            if (_model is not null) _model.PropertyChanged += OnModelChanged;
            RequestFrame();
        };
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _attached = true;
        RequestFrame();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _attached = false;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LyricsViewModel.Lines)) _jumpNext = true;
        if (e.PropertyName is nameof(LyricsViewModel.CurrentIndex) or nameof(LyricsViewModel.Lines))
            // After layout, so rows that were only just created have a position.
            Dispatcher.UIThread.Post(KeepCurrentLineInView, DispatcherPriority.Loaded);
        if (e.PropertyName == nameof(LyricsViewModel.NeedsFrames)) RequestFrame();
    }

    // Frames come from the display (TopLevel.RequestAnimationFrame), not from a timer: a timer at Background priority is late
    // and uneven when the UI is busy, which shows as lines and words stepping unevenly. The loop runs only while something
    // needs it: the lyrics follow a song that is playing, or a glide is under way.
    private void RequestFrame()
    {
        if (_frameRequested || !_attached || TopLevel.GetTopLevel(this) is not { } top) return;
        if (_model is not { } model || !(model.NeedsFrames || _gliding)) return;

        _frameRequested = true;
        top.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        _frameRequested = false;
        if (!_attached || _model is not { } model) return;

        if (model.NeedsFrames) model.Tick();
        if (_gliding) AdvanceGlide(time);
        RequestFrame();
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

        if (!glide)
        {
            _gliding = false;
            Scroller.Offset = new Vector(0, target);
            return;
        }

        _glideFrom = Scroller.Offset.Y;
        _glideTo = target;
        _glideStarted = null;
        _gliding = true;
        RequestFrame();
    }

    private void AdvanceGlide(TimeSpan time)
    {
        _glideStarted ??= time;
        var done = Math.Min(1, (time - _glideStarted.Value) / GlideDuration);
        var eased = 1 - Math.Pow(1 - done, 3); // fast start, soft landing
        Scroller.Offset = new Vector(0, _glideFrom + (_glideTo - _glideFrom) * eased);
        if (done >= 1) _gliding = false;
    }
}
