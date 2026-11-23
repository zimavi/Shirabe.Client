using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Shirabe.Client.Converters;

/// <summary>
/// Turns how much of a word has been sung (0 to 1) into the mask that shows that much of the bright copy of the word: opaque up
/// to the front, transparent after it, with a soft edge. The word is drawn twice, dim below and bright above, and this masks the
/// bright one. (Giving the text itself a gradient brush made Avalonia draw the glyphs with a different, offset rendering and
/// coloured fringes, which showed as doubled characters in Japanese.) At 0 and from 1 on there is no mask: the bright copy is
/// hidden or whole, which the view decides.
/// </summary>
public sealed class WordFillConverter : IValueConverter
{
    public static readonly WordFillConverter Instance = new();

    private const double Edge = 0.12; // the width of the soft edge, as a fraction of the word

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double progress || progress <= 0 || progress >= 1) return null;

        // The edge runs from fully left (progress 0) to fully right (progress 1), so the word is all dim or all bright at the ends.
        var front = progress * (1 + Edge);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Colors.Black, 0),
                new GradientStop(Colors.Black, Math.Clamp(front - Edge, 0, 1)),
                new GradientStop(Colors.Transparent, Math.Clamp(front, 0, 1)),
                new GradientStop(Colors.Transparent, 1),
            ],
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
