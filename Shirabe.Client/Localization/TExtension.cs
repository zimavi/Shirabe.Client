using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace Shirabe.Client.Localization;

/// <summary>
/// A translated text in XAML: <c>Text="{loc:T Nav_Home}"</c>. It binds to <see cref="Loc"/>, so the text changes
/// when the language does. Works on any property, including <c>ToolTip.Tip</c> and <c>AutomationProperties.Name</c>.
/// </summary>
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new ReflectionBindingExtension($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(
            serviceProvider);
}