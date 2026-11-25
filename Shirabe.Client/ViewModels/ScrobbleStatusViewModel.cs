using CommunityToolkit.Mvvm.ComponentModel;

using Shirabe.Client.Localization;
using Shirabe.Client.Services;

namespace Shirabe.Client.ViewModels;

/// <summary>
/// The short message in the title bar about the last scrobble: it shows for a few seconds and goes away by itself.
/// </summary>
/// <param name="wait">How long a message stays. Null: 5 seconds.</param>
public sealed partial class ScrobbleStatusViewModel(Func<Task>? wait = null) : ViewModelBase
{
    private readonly Func<Task> _wait = wait ?? (() => Task.Delay(TimeSpan.FromSeconds(5)));
    private int _latest;

    [ObservableProperty] public partial bool IsOpen { get; private set; }

    /// <summary>"" before the first message; it stays while the message fades away.</summary>
    [ObservableProperty]
    public partial string Message { get; private set; } = "";

    /// <summary>The song did not reach the history.</summary>
    [ObservableProperty]
    public partial bool IsProblem { get; private set; }

    /// <summary>Call on the UI thread; the task ends when this message's time is over.</summary>
    public async Task ShowAsync(ScrobbleReport report)
    {
        var number = ++_latest;
        var (title, artist) = (report.Song.Title, report.Song.Artist);
        Message = report.Result switch
        {
            ScrobbleResult.Scrobbled => Loc.Get("Scrobbler_Done", title, artist),
            ScrobbleResult.Ignored => Loc.Get("Scrobbler_Ignored", title, artist),
            _ => Loc.Get("Scrobbler_Failed", title, artist),
        };
        IsProblem = report.Result != ScrobbleResult.Scrobbled;
        IsOpen = true;

        await _wait();
        if (number == _latest) IsOpen = false; // a newer message keeps its own time
    }
}
