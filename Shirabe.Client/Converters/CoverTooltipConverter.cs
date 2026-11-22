using System.Globalization;
using Avalonia.Data.Converters;
using Shirabe.Client.Views;

namespace Shirabe.Client.Converters;

/// <summary>Turns a cover path into a large cover for a tooltip; null (no cover) gives no tooltip at all.</summary>
public sealed class CoverTooltipConverter : IValueConverter
{
    public static readonly CoverTooltipConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string path ? new CoverView { Width = 280, Height = 280, ArtworkPath = path } : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
