using System.ComponentModel;

using Shirabe.Client.Localization;

namespace Shirabe.Client.Services;

/// <summary>
/// The texts of tooltips that name a key. Indexed by an action name it gives "Shuffle (S)"; with "Key." in front,
/// only " (S)", for a tooltip whose label comes from elsewhere. Both change with the language and the bindings.
/// </summary>
public sealed class ShortcutHints : INotifyPropertyChanged
{
    public static ShortcutHints Instance { get; } = new();

    private AppSettings _settings = new();

    private ShortcutHints() => Loc.Instance.LanguageChanged += (_, _) => Refresh();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string name]
    {
        get
        {
            var suffixOnly = name.StartsWith("Key.", StringComparison.Ordinal);
            var action = Enum.Parse<ShortcutAction>(suffixOnly ? name[4..] : name);
            var gesture = Shortcuts.Get(_settings, action);
            var suffix = gesture.Length == 0 ? "" : $" ({Shortcuts.Display(gesture)})";
            return suffixOnly ? suffix : Loc.Get(Shortcuts.LabelKey(action)) + suffix;
        }
    }

    /// <summary>Follows the bindings of these settings. Call once, at startup.</summary>
    public void Attach(AppSettings settings)
    {
        _settings = settings;
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.Shortcuts)) Refresh();
        };
        Refresh();
    }

    private void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}