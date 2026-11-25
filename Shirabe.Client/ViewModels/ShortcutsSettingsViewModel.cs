using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Shirabe.Client.Localization;
using Shirabe.Client.Services;

namespace Shirabe.Client.ViewModels;

/// <summary>The list of keys. Picking one waits for the next key press, which the view passes to <see cref="Press"/>.</summary>
public sealed partial class ShortcutsSettingsViewModel : SettingsPageViewModel
{
    private readonly AppSettings _settings;

    public ShortcutsSettingsViewModel(AppSettings settings) : base("Shortcuts_Title", "Shortcuts_Description",
        "Icon.Settings.Shortcuts")
    {
        _settings = settings;
        Rows = [.. Enum.GetValues<ShortcutAction>().Select(action => new ShortcutRowViewModel(settings, action))];
        // shortcut: never unsubscribed, fine while the settings pages live as long as the app
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AppSettings.Shortcuts)) return;
            foreach (var row in Rows) row.Refresh();
        };
    }

    public IReadOnlyList<ShortcutRowViewModel> Rows { get; }

    /// <param name="key">Name of the pressed key; "" modifiers aside, this decides the keys with a fixed meaning.</param>
    /// <param name="gesture">The key with its modifiers, see <see cref="Shortcuts.Gesture"/>.</param>
    /// <returns>Whether the key press was used up; false lets it do its usual job.</returns>
    public bool Press(ShortcutRowViewModel row, string key, string gesture)
    {
        switch (key)
        {
            case "Tab": // never trap the keyboard focus
                row.Cancel();
                return false;
            case "Escape":
                row.Cancel();
                return true;
            case "None" or "LeftCtrl" or "RightCtrl" or "LeftShift" or "RightShift" or "LeftAlt" or "RightAlt"
                or "LWin" or "RWin":
                return true; // a modifier on its own: wait for the key that goes with it
            case "MediaPlayPause" or "MediaNextTrack" or "MediaPreviousTrack":
                return true; // these always control playback
        }

        if (gesture is "Back" or "Delete")
        {
            Shortcuts.Set(_settings, row.Action, "");
        }
        else if (Shortcuts.Find(_settings, gesture) is { } owner && owner != row.Action)
        {
            row.TakenBy = owner; // keeps waiting, so another key can be tried at once
            return true;
        }
        else
        {
            Shortcuts.Set(_settings, row.Action, gesture);
        }

        row.Cancel();
        return true;
    }

    [RelayCommand]
    private void ResetAll()
    {
        foreach (var row in Rows) row.Cancel();
        Shortcuts.ResetAll(_settings);
    }
}

public sealed partial class ShortcutRowViewModel : ViewModelBase
{
    private readonly AppSettings _settings;

    public ShortcutRowViewModel(AppSettings settings, ShortcutAction action)
    {
        _settings = settings;
        Action = action;
        RefreshOnLanguageChange(nameof(Name), nameof(KeyText), nameof(Message));
    }

    public ShortcutAction Action { get; }

    public string Name => Loc.Get(Shortcuts.LabelKey(Action));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyText), nameof(Message))]
    public partial bool IsCapturing { get; set; }

    /// <summary>The action that already has the key just pressed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Message))]
    public partial ShortcutAction? TakenBy { get; set; }

    public string KeyText
    {
        get
        {
            if (IsCapturing) return Loc.Get("Shortcuts_PressKey");
            var gesture = Shortcuts.Get(_settings, Action);
            return gesture.Length == 0 ? Loc.Get("Shortcuts_NotSet") : Shortcuts.Display(gesture);
        }
    }

    /// <summary>What to do while waiting for a key, or why the last one was refused; null otherwise.</summary>
    public string? Message =>
        !IsCapturing ? null
        : TakenBy is { } owner ? Loc.Get("Shortcuts_Taken", Loc.Get(Shortcuts.LabelKey(owner)))
        : Loc.Get("Shortcuts_CaptureHelp");

    [RelayCommand]
    private void StartCapture()
    {
        TakenBy = null;
        IsCapturing = true;
    }

    public void Cancel()
    {
        IsCapturing = false;
        TakenBy = null;
    }

    public void Refresh() => OnPropertyChanged(nameof(KeyText));
}