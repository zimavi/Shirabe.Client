namespace Shirabe.Core.Playback;

/// <param name="Id">Stays the same across restarts, to save the choice by.</param>
public sealed record OutputDevice(string Id, string Name);

/// <summary>What the device in use is mixing at.</summary>
public sealed record OutputInfo(string DeviceName, int SampleRate, int Channels);

/// <summary>The loaded source as the engine reads it. Null parts: the engine cannot tell.</summary>
public sealed record SourceInfo(string? Format, int SampleRate, int Channels, int? BitDepth, int? BitrateKbps);

/// <summary>Where the sound goes, for players that can say and change it.</summary>
public interface IAudioOutput
{
    /// <summary>The devices connected now. The system default is not one of them: its id is "".</summary>
    IReadOnlyList<OutputDevice> GetDevices();

    /// <summary>Moves playback to the device with this id; "" is the system's default.</summary>
    /// <returns>False when that device cannot be used now: playback is then on the default.</returns>
    bool UseDevice(string id);

    OutputInfo? GetOutput();

    /// <summary>Null when nothing is loaded.</summary>
    SourceInfo? GetSource();
}
