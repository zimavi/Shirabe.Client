namespace Shirabe.Client.ViewModels;

/// <param name="player">Null when the audio engine could not start.</param>
/// <param name="startupError">Why the audio engine could not start, shown instead of the player.</param>
public class MainViewModel(PlayerViewModel? player, string? startupError) : ViewModelBase
{
    public PlayerViewModel? Player { get; } = player;

    public string? StartupError { get; } = startupError;
}
