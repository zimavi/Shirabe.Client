using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Threading;
using Shirabe.Client.ViewModels;

namespace Shirabe.Client.Services;

/// <summary>
/// Keeps the last <see cref="PlayerSession"/> in a file (session.json beside the settings). It is state, not a
/// preference, and it changes while the user drags the volume, so changes are written after a short pause
/// instead of one write per change; <see cref="Flush"/> writes at once (on exit).
/// </summary>
public sealed class SessionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _delay = new() { Interval = TimeSpan.FromSeconds(1) };
    private PlayerViewModel? _player;

    public SessionStore(string path, AppSettings settings)
    {
        _path = path;
        _settings = settings;
        _delay.Tick += (_, _) => Flush();

        // Switching "Resume last session" off forgets the file at once; on, it starts saving at once.
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.ResumeLastSession)) Flush();
        };
    }

    /// <returns>The saved session, or null when there is none or the file cannot be read.</returns>
    public PlayerSession? Load()
    {
        try
        {
            return JsonSerializer.Deserialize<PlayerSession>(File.ReadAllText(_path), Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts saving the player's session whenever it changes. Call it after restoring, so that restoring does not
    /// overwrite the file with half-restored state.
    /// </summary>
    public void Attach(PlayerViewModel player)
    {
        _player = player;
        player.PropertyChanged += (_, e) =>
        {
            // IsPlaying: pausing saves the position. CurrentTrack: a new song.
            if (e.PropertyName is nameof(PlayerViewModel.Volume) or nameof(PlayerViewModel.IsShuffleOn)
                or nameof(PlayerViewModel.Repeat) or nameof(PlayerViewModel.CurrentTrack) or nameof(PlayerViewModel.IsPlaying))
            {
                _delay.Stop(); // restart the pause
                _delay.Start();
            }
        };
    }

    /// <summary>
    /// Writes the session now, or deletes the file while "Resume last session" is off. Does nothing before
    /// <see cref="Attach"/>.
    /// </summary>
    public void Flush()
    {
        _delay.Stop();
        if (_player is null) return;

        _player.RefreshPosition();
        try
        {
            if (!_settings.ResumeLastSession)
            {
                File.Delete(_path);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp"; // write beside, then swap, so a crash cannot leave half a file
            File.WriteAllText(temp, JsonSerializer.Serialize(_player.Capture(), Options));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the last position is not worth interrupting the user.
        }
    }
}
