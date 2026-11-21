using System.Runtime.InteropServices;
using ManagedBass;
using Shirabe.Core.Media;
using Shirabe.Core.Playback;
using PlaybackState = Shirabe.Core.Playback.PlaybackState; // ManagedBass has one too

namespace Shirabe.Audio;

/// <summary>
/// <see cref="IAudioPlayer"/> on top of BASS. BASS is initialised per process, so create one
/// instance and dispose it when the application exits.
/// </summary>
public sealed class BassAudioPlayer : IAudioPlayer
{
    private readonly object _gate = new();
    private readonly SyncProcedure _onEnd;
    private StreamSource? _source;
    private int _handle;
    private float _volume = 1f;
    private PlaybackState _state;

    /// <param name="device">BASS device number: -1 is the default output, 0 is "no sound" (used by tests).</param>
    public BassAudioPlayer(int device = -1)
    {
        // Already = BASS is initialised in this process; reuse it.
        if (!Bass.Init(device) && Bass.LastError != Errors.Already)
            throw new InvalidOperationException($"BASS could not initialise audio output: {Bass.LastError}");

        _onEnd = OnEnd;
    }

    public event EventHandler? StateChanged;

    public event EventHandler? Ended;

    public PlaybackState State
    {
        get { lock (_gate) return _state; }
    }

    public TimeSpan Position
    {
        get
        {
            var handle = Volatile.Read(ref _handle);
            if (handle == 0) return TimeSpan.Zero;
            var bytes = Bass.ChannelGetPosition(handle);
            return bytes < 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(handle, bytes));
        }
    }

    public TimeSpan? Duration
    {
        get
        {
            var handle = Volatile.Read(ref _handle);
            if (handle == 0) return null;
            var bytes = Bass.ChannelGetLength(handle);
            return bytes < 0 ? null : TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(handle, bytes));
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            var handle = Volatile.Read(ref _handle);
            if (handle != 0) Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, _volume);
        }
    }

    public async Task LoadAsync(IMediaSource source, CancellationToken cancellationToken = default)
    {
        var stream = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        var streamSource = new StreamSource(stream);
        var handle = Bass.CreateStream(StreamSystem.NoBuffer, BassFlags.Default, streamSource.Procedures);
        if (handle == 0)
        {
            var error = Bass.LastError;
            stream.Dispose();
            throw new InvalidOperationException($"BASS cannot play this media: {error}");
        }

        Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, _volume);
        Bass.ChannelSetSync(handle, SyncFlags.End, 0, _onEnd);

        lock (_gate)
        {
            FreeCurrent();
            _source = streamSource;
            _handle = handle;
        }

        Transition(PlaybackState.Paused);
    }

    public void Play()
    {
        var handle = Volatile.Read(ref _handle);
        if (handle == 0) return;

        // After the end of the media the channel must be restarted from the beginning.
        if (Bass.ChannelPlay(handle, Restart: State == PlaybackState.Stopped))
            Transition(PlaybackState.Playing);
    }

    public void Pause()
    {
        var handle = Volatile.Read(ref _handle);
        if (handle == 0 || State != PlaybackState.Playing) return;

        if (Bass.ChannelPause(handle))
            Transition(PlaybackState.Paused);
    }

    public void Stop()
    {
        var handle = Volatile.Read(ref _handle);
        if (handle == 0) return;

        Bass.ChannelStop(handle);
        Bass.ChannelSetPosition(handle, 0);
        Transition(PlaybackState.Stopped);
    }

    public void Seek(TimeSpan position)
    {
        var handle = Volatile.Read(ref _handle);
        if (handle == 0) return;

        var seconds = Math.Max(0, position.TotalSeconds);
        if (Duration is { } duration) seconds = Math.Min(seconds, duration.TotalSeconds);
        Bass.ChannelSetPosition(handle, Bass.ChannelSeconds2Bytes(handle, seconds));
    }

    public void Dispose()
    {
        lock (_gate) FreeCurrent();
        Bass.Free();
    }

    // Runs on a BASS thread. Handlers may call back into BASS (load, free, stop), which is not allowed
    // from inside a BASS callback, so hand off to the thread pool before raising any event.
    private void OnEnd(int handle, int channel, int data, IntPtr user) =>
        ThreadPool.UnsafeQueueUserWorkItem(_ =>
        {
            if (channel != Volatile.Read(ref _handle)) return;

            Transition(PlaybackState.Stopped);
            Ended?.Invoke(this, EventArgs.Empty);
        }, null);

    // Caller holds _gate. Freeing the BASS stream makes BASS close the underlying .NET stream.
    private void FreeCurrent()
    {
        var handle = _handle;
        _handle = 0;
        _source = null;
        if (handle != 0) Bass.StreamFree(handle);
    }

    private void Transition(PlaybackState next)
    {
        lock (_gate)
        {
            if (_state == next) return;
            _state = next;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Feeds a .NET stream to BASS. Keeps the callbacks alive for as long as BASS uses them.</summary>
    private sealed class StreamSource
    {
        private readonly Stream _stream;
        private byte[] _buffer = new byte[64 * 1024];

        public StreamSource(Stream stream)
        {
            _stream = stream;
            Procedures = new FileProcedures
            {
                Close = _ => _stream.Dispose(),
                Length = _ => Guarded(() => _stream.Length, 0),
                Read = (buffer, length, _) => Guarded(() => Read(buffer, length), 0),
                Seek = (offset, _) => Guarded(() => { _stream.Position = offset; return true; }, false),
            };
        }

        public FileProcedures Procedures { get; }

        private int Read(IntPtr destination, int length)
        {
            if (length > _buffer.Length) _buffer = new byte[length];
            var read = _stream.Read(_buffer, 0, length);
            Marshal.Copy(_buffer, 0, destination, read);
            return read;
        }

        // These run on BASS's threads; an exception escaping into native code would crash the process,
        // so a failing stream just looks like an ended or unreadable one.
        private T Guarded<T>(Func<T> action, T fallback)
        {
            try
            {
                lock (_stream) return action();
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
