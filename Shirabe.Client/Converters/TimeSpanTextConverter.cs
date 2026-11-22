using System.Globalization;
using Avalonia.Data.Converters;

namespace Shirabe.Client.Converters;

/// <summary>Shows a duration as m:ss (h:mm:ss from one hour); empty when unknown.</summary>
public sealed class TimeSpanTextConverter : IValueConverter
{
    public static readonly TimeSpanTextConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TimeSpan time
            ? time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss")
            : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
