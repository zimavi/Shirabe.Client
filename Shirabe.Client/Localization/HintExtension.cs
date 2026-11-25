using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;

using Shirabe.Client.Services;

namespace Shirabe.Client.Localization;

/// <summary>A tooltip that names a key: <c>{loc:Hint Shuffle}</c>, see <see cref="ShortcutHints"/> for the names.</summary>
public sealed class HintExtension(string name) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new ReflectionBindingExtension($"[{name}]") { Source = ShortcutHints.Instance, Mode = BindingMode.OneWay }
            .ProvideValue(serviceProvider);
}