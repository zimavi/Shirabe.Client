using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Shirabe.Client.Localization;
using Shirabe.Client.Services;
using Shirabe.Core.Playback;

namespace Shirabe.Client.ViewModels;

/// <summary>
/// Output device, playback, equaliser and volume. Choosing the device is done here, the saved one at startup too; the rest
/// are settings the <see cref="PlayerViewModel"/> follows by itself.
/// </summary>
public sealed partial class AudioSettingsViewModel : SettingsPageViewModel
{
    private readonly IAudioOutput? _output;
    private bool _listChanging;

    /// <param name="output">Null: the player cannot say or change where the sound goes.</param>
    public AudioSettingsViewModel(AppSettings settings, IAudioOutput? output = null, PlayerViewModel? player = null)
        : base("Audio_Title", "Audio_Description", "Icon.Settings.Audio")
    {
        Settings = settings;
        _output = output;
        SupportsEffects = player?.SupportsEffects ?? false;
        Bands =
        [
            .. EqualizerBands.Frequencies.Select((frequency, band) =>
                new EqualizerBandViewModel(settings, band, frequency))
        ];
        Refresh();

        // shortcut: never unsubscribed, fine while the settings pages live as long as the app
        if (player is not null) player.TrackLoaded += (_, _) => RefreshInfo();
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AppSettings.EqualizerGains)) return;
            foreach (var band in Bands) band.Refresh();
        };
        Loc.Instance.LanguageChanged += (_, _) => OnUiThread(() =>
        {
            Refresh(); // the "System default" entry and the info lines are translated
            foreach (var option in NormalizationOptions) option.Refresh();
            foreach (var band in Bands) band.Refresh();
        });
    }

    public AppSettings Settings { get; }

    public bool HasOutput => _output is not null;

    /// <summary>False when the effects add-on is missing: keep pitch, reverb and the equaliser are greyed out.</summary>
    public bool SupportsEffects { get; }

    public IReadOnlyList<EqualizerBandViewModel> Bands { get; }

    [RelayCommand]
    private void ResetEqualizer() => Settings.EqualizerGains = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDevice))]
    public partial IReadOnlyList<OutputDevice> Devices { get; set; } = [];

    /// <summary>The saved device is not connected, so the system's default plays.</summary>
    [ObservableProperty]
    public partial bool IsDeviceMissing { get; set; }

    public OutputDevice? SelectedDevice
    {
        get => Devices.FirstOrDefault(device => device.Id == Settings.OutputDevice) ?? Devices.FirstOrDefault();
        set
        {
            // A combo box writes back while its list is being replaced; that is not the user choosing.
            if (_listChanging || value is null || value.Id == Settings.OutputDevice) return;
            Settings.OutputDevice = value.Id;
            // Not Refresh(): the list must stay as it is while the combo box is still applying the choice.
            UseChosenDevice();
        }
    }

    [ObservableProperty] public partial string? OutputText { get; set; }

    [ObservableProperty] public partial string? SourceText { get; set; }

    public IReadOnlyList<NormalizationOption> NormalizationOptions { get; } =
        [.. Enum.GetValues<NormalizationMode>().Select(mode => new NormalizationOption(mode))];

    public NormalizationOption? SelectedNormalization
    {
        get => NormalizationOptions.FirstOrDefault(option => option.Mode == Settings.Normalization);
        set
        {
            if (value is not null) Settings.Normalization = value.Mode;
        }
    }

    public IReadOnlyList<int> ScrollSteps { get; } = [1, 2, 5, 10];

    /// <summary>Reads the devices again and moves to the chosen one, which may have been plugged in since.</summary>
    [RelayCommand]
    private void Refresh()
    {
        if (_output is not null)
        {
            OutputDevice[] devices = [new("", Loc.Get("Audio_SystemDefault")), .. _output.GetDevices()];
            if (!devices.SequenceEqual(Devices))
            {
                _listChanging = true;
                try
                {
                    Devices = devices;
                }
                finally
                {
                    _listChanging = false;
                }

                OnPropertyChanged(nameof(SelectedDevice));
            }
        }

        UseChosenDevice();
    }

    private void UseChosenDevice()
    {
        if (_output is not null)
            IsDeviceMissing = !_output.UseDevice(Settings.OutputDevice) && Settings.OutputDevice.Length > 0;
        RefreshInfo();
    }

    private void RefreshInfo()
    {
        OutputText = _output?.GetOutput() is { } output
            ? Join(output.DeviceName, KHz(output.SampleRate), Loc.Get("Audio_Channels", output.Channels))
            : Loc.Get("Audio_Unknown");

        SourceText = _output?.GetSource() is { } source
            ? Join(source.Format, KHz(source.SampleRate), Loc.Get("Audio_Channels", source.Channels),
                source.BitDepth is { } depth ? Loc.Get("Audio_BitDepth", depth) : null,
                source.BitrateKbps is { } bitrate ? Loc.Get("Audio_Bitrate", bitrate) : null)
            : Loc.Get("Player_NothingPlaying");
    }

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(part => part is not null));

    private static string KHz(int hertz) =>
        Loc.Get("Audio_KHz", (hertz / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
}

/// <summary>One slider of the equaliser.</summary>
public sealed class EqualizerBandViewModel(AppSettings settings, int band, double frequency) : ObservableObject
{
    public string Label => frequency < 1000
        ? Loc.Get("Audio_Hz", frequency.ToString("0", CultureInfo.InvariantCulture))
        : Loc.Get("Audio_KHz", (frequency / 1000).ToString("0", CultureInfo.InvariantCulture));

    public double Gain
    {
        get => settings.EqualizerGains[band];
        set
        {
            if (value == Gain) return;

            // A new array, because assigning the property is what saves the file and tells the player.
            double[] gains = [.. settings.EqualizerGains];
            gains[band] = value;
            settings.EqualizerGains = gains;
        }
    }

    public string GainText => Loc.Get("Audio_Decibels",
        Gain.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture));

    public void Refresh()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Gain));
        OnPropertyChanged(nameof(GainText));
    }
}

/// <summary>One entry of the normalisation list.</summary>
public sealed class NormalizationOption(NormalizationMode mode) : ObservableObject
{
    public NormalizationMode Mode { get; } = mode;

    public string Name => Mode switch
    {
        NormalizationMode.Track => Loc.Get("Audio_NormalizeTrack"),
        NormalizationMode.Album => Loc.Get("Audio_NormalizeAlbum"),
        _ => Loc.Get("Audio_NormalizeOff"),
    };

    public void Refresh() => OnPropertyChanged(nameof(Name));
}