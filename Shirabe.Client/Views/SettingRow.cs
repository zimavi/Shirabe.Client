using Avalonia;
using Avalonia.Controls;

namespace Shirabe.Client.Views;

/// <summary>
/// One line of a settings page: a title and a description on the left, the control as content on the right.
/// <c>&lt;views:SettingRow Title="..." Description="..."&gt;&lt;ToggleSwitch IsChecked="{Binding ...}" /&gt;&lt;/views:SettingRow&gt;</c>.
/// The template is in Styles/Controls.axaml.
/// </summary>
public class SettingRow : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
