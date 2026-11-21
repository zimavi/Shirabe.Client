using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shirabe.Core.Media;
using Shirabe.Core.Playback;

namespace Shirabe.Client.ViewModels;

/// <summary>
/// Drives a single <see cref="IAudioPlayer"/>. Free of Avalonia types: the view calls
/// <see cref="RefreshPosition"/> from its own timer, and player events are marshalled back to the
/// thread that created this view model.
/// </summary>
public partial class PlayerViewModel : ViewModelBase
{
    private readonly IAudioPlayer _player;
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private bool _updatingPosition;

    public PlayerViewModel(IAudioPlayer player)
    {
        _player = player;
        _player.StateChanged += (_, _) => OnUiThread(() => IsPlaying = _player.State == PlaybackState.Playing);
        _player.Ended += (_, _) => OnUiThread(() =>
        {
            _player.Stop(); // rewinds, so the next Play starts from the beginning
            RefreshPosition();
        });
    }

    [ObservableProperty]
    public partial string Title { get; set; } = "Drop an audio file here";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TogglePlayPauseCommand))]
    public partial bool HasMedia { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseLabel))]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    public partial double DurationSeconds { get; set; }

    /// <summary>Bound two-way to the seek slider: a change made by the user seeks, see <see cref="RefreshPosition"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    public partial double PositionSeconds { get; set; }

    public string PlayPauseLabel => IsPlaying ? "Pause" : "Play";

    public string PositionText => FormatTime(PositionSeconds);

    public string DurationText => FormatTime(DurationSeconds);

    public async Task OpenAsync(string path)
    {
        try
        {
            await _player.LoadAsync(new LocalFileSource(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ErrorMessage = $"Cannot play {Path.GetFileName(path)}: {ex.Message}";
            return;
        }

        ErrorMessage = null;
        Title = Path.GetFileNameWithoutExtension(path);
        HasMedia = true;
        SetPosition(0); // before the duration changes, so the slider cannot coerce an old position into a seek
        DurationSeconds = _player.Duration?.TotalSeconds ?? 0;
        _player.Play();
    }

    /// <summary>Copies the player's position to <see cref="PositionSeconds"/> without seeking.</summary>
    public void RefreshPosition() => SetPosition(_player.Position.TotalSeconds);

    [RelayCommand(CanExecute = nameof(HasMedia))]
    private void TogglePlayPause()
    {
        if (_player.State == PlaybackState.Playing) _player.Pause();
        else _player.Play();
    }

    partial void OnPositionSecondsChanged(double value)
    {
        if (!_updatingPosition) _player.Seek(TimeSpan.FromSeconds(value));
    }

    private void SetPosition(double seconds)
    {
        _updatingPosition = true;
        try
        {
            PositionSeconds = seconds;
        }
        finally
        {
            _updatingPosition = false;
        }
    }

    private void OnUiThread(Action action)
    {
        if (_uiContext is null) action();
        else _uiContext.Post(_ => action(), null);
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }
}
