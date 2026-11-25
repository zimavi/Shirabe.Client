using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Fx;
using Shirabe.Core.Media;
using Shirabe.Core.Playback;
using PlaybackState = Shirabe.Core.Playback.PlaybackState; // ManagedBass has one too

namespace Shirabe.Audio;

/// <summary>
/// <see cref="IAudioPlayer"/> on top of BASS. BASS is initialised per process, so create one
/// instance and dispose it when the application exits.
/// </summary>
public sealed class BassAudioPlayer : IAudioPlayer, IAudioOutput
{
    // BASS_ATTRIB_VOLDSP: a factor on the sample data that may exceed 1. ManagedBass has no name for it.
    private const ChannelAttribute SampleGain = (ChannelAttribute)19;

    private readonly object _gate = new();
    private readonly int _defaultDevice;
    private readonly HashSet<int> _opened = []; // every device BASS was initialised on, to free them all
    private int _device;
    private bool _tempoStage; // the loaded stream goes through BASS_FX's tempo processing, needed to keep the pitch
    private int _reverbEffect;
    private int _equalizerEffect;
    private bool _keepPitch;
    private bool _reverb;
    private double[] _equalizer = [];
    private double _speed = 1;
    private float _gain = 1f;
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

        _defaultDevice = _device = device >= 0 ? device : Bass.CurrentDevice;
        _opened.Add(_device);
        SupportsEffects = LoadEffects();
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

    public double Speed
    {
        get => _speed;
        set
        {
            _speed = Math.Clamp(value, 0.5, 2);
            var handle = Volatile.Read(ref _handle);
            if (handle != 0) ApplySpeed(handle);
        }
    }

    public float Gain
    {
        get => _gain;
        set
        {
            _gain = Math.Max(0f, value);
            var handle = Volatile.Read(ref _handle);
            if (handle != 0) ApplyGain(handle);
        }
    }

    public bool SupportsEffects { get; }

    public bool KeepPitch
    {
        get => _keepPitch;
        set
        {
            lock (_gate)
            {
                if (_keepPitch == value) return;
                _keepPitch = value;
                if (_handle == 0 || _tempoStage == (value && SupportsEffects)) return;
            }

            Rebuild();
        }
    }

    public bool Reverb
    {
        get => _reverb;
        set
        {
            lock (_gate)
            {
                _reverb = value;
                if (_handle != 0) ApplyReverb(_handle);
            }
        }
    }

    public IReadOnlyList<double> Equalizer
    {
        get => _equalizer;
        set
        {
            lock (_gate)
            {
                _equalizer = [.. value.Take(EqualizerBands.Frequencies.Count)];
                if (_handle == 0) return;
                ApplyEqualizer(_handle);
                ApplyGain(_handle);
            }
        }
    }

    public IReadOnlyList<OutputDevice> GetDevices() =>
        [.. Devices().Select(d => new OutputDevice(IdOf(d.Info), d.Info.Name))];

    public bool UseDevice(string id)
    {
        lock (_gate)
        {
            var wanted = id.Length == 0
                ? _defaultDevice
                : Devices().Where(d => IdOf(d.Info) == id).Select(d => (int?)d.Index).FirstOrDefault();
            var found = wanted is { } index && Open(index);
            _device = found ? wanted!.Value : _defaultDevice;

            // A playing channel moves over and plays on.
            if (_handle != 0 && Bass.ChannelGetDevice(_handle) != _device && !Bass.ChannelSetDevice(_handle, _device))
                return false;

            return found;
        }
    }

    public OutputInfo? GetOutput()
    {
        lock (_gate)
        {
            if (!Select(_device) || !Bass.GetInfo(out var info) || !Bass.GetDeviceInfo(_device, out var device))
                return null;

            // BASS's "Default" entry has no name of its own: show the device it stands for.
            var name = IsDefaultEntry(_device, device)
                ? Devices().Where(d => d.Info.IsDefault).Select(d => d.Info.Name).FirstOrDefault() ?? device.Name
                : device.Name;
            return new OutputInfo(name, info.SampleRate, info.SpeakerCount);
        }
    }

    public SourceInfo? GetSource()
    {
        lock (_gate)
        {
            if (_handle == 0) return null;
            var source = _tempoStage ? BassFx.TempoGetSource(_handle) : _handle; // the file's own facts
            if (!Bass.ChannelGetInfo(source, out var info)) return null;

            var format = info.ChannelType switch
            {
                ChannelType.MP1 => "MP1",
                ChannelType.MP2 => "MP2",
                ChannelType.MP3 => "MP3",
                ChannelType.OGG => "Ogg Vorbis",
                ChannelType.Wave or ChannelType.WavePCM or ChannelType.WaveFloat => "WAV",
                ChannelType.AIFF => "AIFF",
                ChannelType.FLAC or ChannelType.FLAC_OGG => "FLAC",
                ChannelType.ALAC => "ALAC",
                ChannelType.AAC => "AAC",
                ChannelType.MP4 => "MP4",
                ChannelType.OPUS => "Opus",
                _ => null, // e.g. whatever Windows Media Foundation decodes for BASS
            };
            var depth = (int)info.OriginalResolution & 0xFFFF; // the high bit says "floating point"
            int? bitrate = Bass.ChannelGetAttribute(source, ChannelAttribute.Bitrate, out var kbps) && kbps > 0
                ? (int)Math.Round(kbps)
                : null;
            return new SourceInfo(format, info.Frequency, info.Channels, depth > 0 ? depth : null, bitrate);
        }
    }

    // Device 0 is "no sound", and the "Default" entry is what the id "" stands for.
    private static IEnumerable<(int Index, DeviceInfo Info)> Devices()
    {
        for (var i = 1; Bass.GetDeviceInfo(i, out var info); i++)
            if (info.IsEnabled && !IsDefaultEntry(i, info)) yield return (i, info);
    }

    private static bool IsDefaultEntry(int index, DeviceInfo info) =>
        index == 1 && string.IsNullOrEmpty(info.Driver) && info.Name == "Default";

    private static string IdOf(DeviceInfo info) => string.IsNullOrEmpty(info.Driver) ? info.Name : info.Driver;

    // ManagedBass throws here for a device that is not open (any more).
    private static bool Select(int device)
    {
        try
        {
            Bass.CurrentDevice = device;
            return true;
        }
        catch (BassException)
        {
            return false;
        }
    }

    // Caller holds _gate.
    private bool Open(int device)
    {
        if (!Bass.Init(device) && Bass.LastError != Errors.Already) return false;
        _opened.Add(device);
        return true;
    }

    // Asking for its version is what loads BASS_FX and makes its effects known to BASS.
    private static bool LoadEffects()
    {
        try
        {
            return BassFx.Version.Major > 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    // With the tempo stage the speed is a tempo change (pitch kept), without it a sample rate change (pitch follows).
    private void ApplySpeed(int handle)
    {
        if (!Bass.ChannelGetInfo(handle, out var info)) return;

        Bass.ChannelSetAttribute(handle, ChannelAttribute.Frequency, info.Frequency * (_tempoStage ? 1 : _speed));
        if (_tempoStage) Bass.ChannelSetAttribute(handle, ChannelAttribute.Tempo, (_speed - 1) * 100);
    }

    // Boosting a band turns everything down by the largest boost, so that the equaliser does not clip.
    // shortcut: neighbouring boosted bands add up to a little more than the largest one, a limiter if that is ever heard
    private void ApplyGain(int handle)
    {
        var headroom = SupportsEffects ? Math.Pow(10, -Math.Max(0, _equalizer.DefaultIfEmpty().Max()) / 20) : 1;
        Bass.ChannelSetAttribute(handle, SampleGain, (float)(_gain * headroom));
    }

    // Caller holds _gate.
    private void ApplyReverb(int handle)
    {
        if (!SupportsEffects) return;

        if (_reverb && _reverbEffect == 0)
        {
            _reverbEffect = Bass.ChannelSetFX(handle, EffectType.Freeverb, 0);
            // The echo has to be this strong to be heard under music (the test measures it). Both parts are kept low
            // because the sum peaks far above the dry signal: loud songs get about 5 dB quieter instead of clipping.
            Bass.FXSetParameters(_reverbEffect, new ReverbParameters
            {
                fDryMix = 0.4f, fWetMix = 1f, fRoomSize = 0.8f, fDamp = 0.45f, fWidth = 1,
                lChannel = FXChannelFlags.All,
            });
        }
        else if (!_reverb && _reverbEffect != 0)
        {
            Bass.ChannelRemoveFX(handle, _reverbEffect);
            _reverbEffect = 0;
        }
    }

    // Caller holds _gate.
    private void ApplyEqualizer(int handle)
    {
        if (!SupportsEffects) return;

        if (_equalizer.All(gain => gain == 0))
        {
            if (_equalizerEffect != 0) Bass.ChannelRemoveFX(handle, _equalizerEffect);
            _equalizerEffect = 0;
            return;
        }

        if (_equalizerEffect == 0) _equalizerEffect = Bass.ChannelSetFX(handle, EffectType.PeakEQ, 0);
        for (var band = 0; band < _equalizer.Length; band++)
        {
            // Fails, harmlessly, for a band above what the file's sample rate can hold.
            Bass.FXSetParameters(_equalizerEffect, new PeakEQParameters
            {
                lBand = band,
                fCenter = (float)EqualizerBands.Frequencies[band],
                fBandwidth = 1,
                fGain = (float)Math.Clamp(_equalizer[band], -EqualizerBands.MaxGain, EqualizerBands.MaxGain),
                lChannel = FXChannelFlags.All,
            });
        }
    }

    // A new BASS stream over the source, not yet playing. 0 when BASS cannot read it.
    private int CreateStream(StreamSource source, bool tempoStage)
    {
        Select(_device); // a new stream belongs to the current device

        var flags = tempoStage ? BassFlags.Float | BassFlags.Decode : BassFlags.Float;
        var handle = Bass.CreateStream(StreamSystem.NoBuffer, flags, source.Procedures);
        if (handle == 0 || !tempoStage) return handle;

        var tempo = BassFx.TempoCreate(handle, BassFlags.FxFreeSource);
        if (tempo == 0) Bass.StreamFree(handle);
        return tempo;
    }

    // Caller holds _gate. Makes a new stream the current one, with every setting applied to it.
    private void Adopt(int handle, StreamSource source, bool tempoStage)
    {
        _source = source;
        _tempoStage = tempoStage;
        _reverbEffect = _equalizerEffect = 0;
        _handle = handle;
        if (Bass.ChannelGetDevice(handle) != _device) Bass.ChannelSetDevice(handle, _device); // changed meanwhile

        ApplySpeed(handle);
        ApplyReverb(handle);
        ApplyEqualizer(handle);
        ApplyGain(handle);
        Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, _volume);
        Bass.ChannelSetSync(handle, SyncFlags.End, 0, _onEnd);
    }

    // The tempo stage can only be put in or taken out by making the stream again, at the same place.
    private void Rebuild()
    {
        var failed = false;
        lock (_gate)
        {
            if (_handle == 0 || _source is not { } old) return;

            var position = Position;
            var playing = _state == PlaybackState.Playing;
            var stream = old.Detach(); // freeing the old BASS stream must leave the file open
            FreeCurrent();

            var tempoStage = _keepPitch && SupportsEffects;
            stream.Position = 0;
            var source = new StreamSource(stream);
            var handle = CreateStream(source, tempoStage);
            if (handle == 0)
            {
                stream.Dispose();
                failed = true;
            }
            else
            {
                Adopt(handle, source, tempoStage);
                Seek(position);
                if (playing) Bass.ChannelPlay(handle);
            }
        }

        if (failed) Transition(PlaybackState.Stopped);
    }

    public async Task LoadAsync(IMediaSource source, CancellationToken cancellationToken = default)
    {
        var stream = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        var streamSource = new StreamSource(stream);
        lock (_gate)
        {
            // The chosen device may have been unplugged since: the default takes over.
            if (!Bass.GetDeviceInfo(_device, out var device) || !device.IsEnabled) _device = _defaultDevice;
        }

        var tempoStage = _keepPitch && SupportsEffects;
        var handle = CreateStream(streamSource, tempoStage);
        if (handle == 0)
        {
            var error = Bass.LastError;
            stream.Dispose();
            throw new InvalidOperationException($"BASS cannot play this media: {error}");
        }

        lock (_gate)
        {
            FreeCurrent();
            Adopt(handle, streamSource, tempoStage);
        }

        Transition(PlaybackState.Paused);
        if (tempoStage != (_keepPitch && SupportsEffects)) Rebuild(); // switched while loading
    }

    public void Release()
    {
        lock (_gate) FreeCurrent();
        Transition(PlaybackState.Stopped);
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
        lock (_gate)
        {
            FreeCurrent();
            foreach (var device in _opened)
                if (Select(device))
                    Bass.Free();

            _opened.Clear();
        }
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
        private bool _owns = true;

        public StreamSource(Stream stream)
        {
            _stream = stream;
            Procedures = new FileProcedures
            {
                Close = _ =>
                {
                    if (_owns) _stream.Dispose();
                },
                Length = _ => Guarded(() => _stream.Length, 0),
                Read = (buffer, length, _) => Guarded(() => Read(buffer, length), 0),
                Seek = (offset, _) => Guarded(() => { _stream.Position = offset; return true; }, false),
            };
        }

        public FileProcedures Procedures { get; }

        /// <summary>Hands the .NET stream over: closing this BASS stream no longer closes it.</summary>
        public Stream Detach()
        {
            _owns = false;
            return _stream;
        }

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
