namespace Shirabe.Client.Services;

/// <summary>Something a key can be bound to. The names are saved in settings.json: do not rename them.</summary>
public enum ShortcutAction
{
    PlayPause,
    SeekBack,
    SeekForward,
    Previous,
    Next,
    VolumeUp,
    VolumeDown,
    Mute,
    Shuffle,
    Repeat,
    Lyrics,
    Queue,
    Home,
    Songs,
    Studio,
    Settings,
}

/// <summary>
/// Which keys do what. A gesture is text such as "Ctrl+Left": the modifiers in the order Ctrl, Alt, Shift, Meta, then
/// the name of an Avalonia <c>Key</c>. "" means no key. Only the user's changes are saved, so a new action gets its
/// default in an existing settings file.
/// </summary>
public static class Shortcuts
{
    public static string Default(ShortcutAction action) => Default(action, OperatingSystem.IsMacOS());

    public static string Default(ShortcutAction action, bool mac)
    {
        var command = mac ? "Meta+" : "Ctrl+";
        return action switch
        {
            ShortcutAction.PlayPause => "", // shortcut: no key for now, Space and Enter belong to navigation
            ShortcutAction.SeekBack => "Left",
            ShortcutAction.SeekForward => "Right",
            ShortcutAction.Previous => command + "Left",
            ShortcutAction.Next => command + "Right",
            ShortcutAction.VolumeUp => command + "Up",
            ShortcutAction.VolumeDown => command + "Down",
            ShortcutAction.Mute => "M",
            ShortcutAction.Shuffle => "S",
            ShortcutAction.Repeat => "R",
            ShortcutAction.Lyrics => command + "L",
            ShortcutAction.Queue => command + (mac ? "U" : "Q"), // Cmd+Q quits an app on macOS
            ShortcutAction.Home => command + "D1",
            ShortcutAction.Songs => command + "D2",
            ShortcutAction.Studio => command + "D3",
            ShortcutAction.Settings => command + "OemComma",
            _ => "",
        };
    }

    /// <summary>The key of Resources/Strings.resx that names the action.</summary>
    public static string LabelKey(ShortcutAction action) => action switch
    {
        ShortcutAction.PlayPause => "Player_PlayPause",
        ShortcutAction.SeekBack => "Shortcuts_SeekBack",
        ShortcutAction.SeekForward => "Shortcuts_SeekForward",
        ShortcutAction.Previous => "Player_Previous",
        ShortcutAction.Next => "Player_Next",
        ShortcutAction.VolumeUp => "Shortcuts_VolumeUp",
        ShortcutAction.VolumeDown => "Shortcuts_VolumeDown",
        ShortcutAction.Mute => "Player_Mute",
        ShortcutAction.Shuffle => "Player_Shuffle",
        ShortcutAction.Repeat => "Player_Repeat",
        ShortcutAction.Lyrics => "Player_Lyrics",
        ShortcutAction.Queue => "Player_Queue",
        ShortcutAction.Home => "Nav_Home",
        ShortcutAction.Songs => "Nav_Songs",
        ShortcutAction.Studio => "Nav_Studio",
        _ => "Nav_Settings",
    };

    public static string Gesture(bool ctrl, bool alt, bool shift, bool meta, string key) =>
        (ctrl ? "Ctrl+" : "") + (alt ? "Alt+" : "") + (shift ? "Shift+" : "") + (meta ? "Meta+" : "") + key;

    public static string Get(AppSettings settings, ShortcutAction action) =>
        settings.Shortcuts.GetValueOrDefault(action.ToString()) ?? Default(action); // null: a hand-edited file

    /// <summary>The action bound to <paramref name="gesture"/>. A key the user chose wins over another action's default.</summary>
    public static ShortcutAction? Find(AppSettings settings, string gesture)
    {
        if (gesture.Length == 0) return null;

        var matches = Enum.GetValues<ShortcutAction>().Where(action => Get(settings, action) == gesture)
            .OrderByDescending(action => settings.Shortcuts.ContainsKey(action.ToString()))
            .ToList();
        return matches.Count > 0 ? matches[0] : null;
    }

    public static void Set(AppSettings settings, ShortcutAction action, string gesture)
    {
        // A new dictionary, because assigning the property is what saves the file.
        var changed = new Dictionary<string, string>(settings.Shortcuts);
        if (gesture == Default(action)) changed.Remove(action.ToString());
        else changed[action.ToString()] = gesture;
        settings.Shortcuts = changed;
    }

    public static void ResetAll(AppSettings settings) => settings.Shortcuts = [];

    /// <summary>The gesture as people read it: "Ctrl+," rather than "Ctrl+OemComma".</summary>
    public static string Display(string gesture) =>
        string.Join('+', gesture.Split('+').Select(part => part switch
        {
            "Meta" => OperatingSystem.IsMacOS() ? "Cmd" : "Win",
            "OemComma" => ",",
            "OemPeriod" => ".",
            "OemMinus" => "-",
            "OemPlus" => "=",
            ['D', var digit] when char.IsAsciiDigit(digit) => digit.ToString(),
            _ => part,
        }));
}