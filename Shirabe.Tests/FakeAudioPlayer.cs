using Shirabe.Core.Media;
using Shirabe.Core.Playback;

namespace Shirabe.Tests;

internal sealed class FakeAudioPlayer : IAudioPlayer
{
    public PlaybackState State { get; private set; }

    public TimeSpan Position { get; set; }

    public TimeSpan? Duration { get; set; } = TimeSpan.FromSeconds(120);

    public float Volume { get; set; } = 1f;

    public IMediaSource? Loaded { get; private set; }

    public Exception? LoadException { get; set; }

    /// <summary>Makes loading fail for particular sources only.</summary>
    public Func<IMediaSource, Exception?>? FailWhen { get; set; }

    /// <summary>Every source that was loaded successfully, in order.</summary>
    public List<IMediaSource> Loads { get; } = [];

    public List<TimeSpan> Seeks { get; } = [];

    public event EventHandler? StateChanged;

    public event EventHandler? Ended;

    public Task LoadAsync(IMediaSource source, CancellationToken cancellationToken = default)
    {
        if (LoadException is not null) throw LoadException;
        if (FailWhen?.Invoke(source) is { } failure) throw failure;

        Loaded = source;
        Loads.Add(source);
        Position = TimeSpan.Zero;
        SetState(PlaybackState.Paused);
        return Task.CompletedTask;
    }

    public void Play() => SetState(PlaybackState.Playing);

    public void Pause() => SetState(PlaybackState.Paused);

    public void Stop()
    {
        Position = TimeSpan.Zero;
        SetState(PlaybackState.Stopped);
    }

    public void Seek(TimeSpan position)
    {
        Seeks.Add(position);
        Position = position;
    }

    public void RaiseEnded()
    {
        SetState(PlaybackState.Stopped);
        Ended?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
    }

    private void SetState(PlaybackState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
