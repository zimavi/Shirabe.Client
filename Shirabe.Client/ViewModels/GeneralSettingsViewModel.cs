using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using Shirabe.Client.Localization;
using Shirabe.Client.Services;

namespace Shirabe.Client.ViewModels;

public sealed class GeneralSettingsViewModel : SettingsPageViewModel
{
    public GeneralSettingsViewModel(AppSettings settings) : base("General_Title", "General_Description")
    {
        Settings = settings;
        Languages = [new LanguageOption(""), .. Loc.Available.Select(culture => new LanguageOption(culture.Name))];
        // shortcut: never unsubscribed, fine while the settings pages live as long as the app
        Loc.Instance.LanguageChanged += (_, _) => OnUiThread(() =>
        {
            foreach (var language in Languages) language.Refresh();
        });
    }

    public AppSettings Settings { get; }

    /// <summary>"System default" first, then every language the app ships with.</summary>
    public IReadOnlyList<LanguageOption> Languages { get; }

    public LanguageOption SelectedLanguage
    {
        get => Languages.FirstOrDefault(language => language.Tag == Settings.Language) ?? Languages[0];
        set
        {
            if (value is not null) Settings.Language = value.Tag; // null comes from the combo box being reset
        }
    }
}

/// <summary>One entry of the language list. An empty tag means "follow the system".</summary>
public sealed class LanguageOption(string tag) : ObservableObject
{
    public string Tag { get; } = tag;

    // Languages are named in themselves so a user who cannot read the current one can still find theirs.
    public string Name => Tag.Length == 0 ? Loc.Get("Language_System") : NativeName(CultureInfo.GetCultureInfo(Tag));

    public void Refresh() => OnPropertyChanged(nameof(Name));

    private static string NativeName(CultureInfo culture) =>
        culture.TextInfo.ToUpper(culture.NativeName[..1]) + culture.NativeName[1..];
}