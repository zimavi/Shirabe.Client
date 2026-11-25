using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Shirabe.Client.Converters;

/// <summary>Turns the name of an icon in Styles/Icons.axaml into its geometry, so a view model can pick one by name.</summary>
public sealed class IconKeyConverter : IValueConverter
{
    public static readonly IconKeyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && Application.Current!.TryFindResource(key, out var icon) ? icon : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}