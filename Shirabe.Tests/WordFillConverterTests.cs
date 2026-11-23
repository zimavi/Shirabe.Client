using System.Globalization;
using Avalonia.Media;
using Shirabe.Client.Converters;
using Xunit;

namespace Shirabe.Tests;

public class WordFillConverterTests
{
    private static object? Mask(object? progress) =>
        WordFillConverter.Instance.Convert(progress, typeof(IBrush), null, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-0.2)]
    [InlineData(1.5)]
    [InlineData(null)]
    [InlineData("not a number")]
    public void Before_the_word_after_it_and_for_nonsense_there_is_no_mask(object? progress)
    {
        Assert.Null(Mask(progress)); // the view hides the bright copy before the word, and shows it whole after
    }

    [Fact]
    public void Part_way_through_the_word_the_mask_is_opaque_up_to_the_front_and_transparent_after_it()
    {
        var brush = Assert.IsType<LinearGradientBrush>(Mask(0.5));

        var stops = brush.GradientStops;
        Assert.Equal(4, stops.Count);
        Assert.True(stops.Select(s => s.Offset).SequenceEqual(stops.Select(s => s.Offset).Order())); // offsets never go backwards
        Assert.Equal([255, 255, 0, 0], stops.Select(s => (int)s.Color.A));
        Assert.InRange(stops[1].Offset, 0.3, 0.6); // the front is about halfway
    }

    [Fact]
    public void The_front_moves_right_as_the_word_goes_on()
    {
        var early = Assert.IsType<LinearGradientBrush>(Mask(0.2)).GradientStops[2].Offset;
        var late = Assert.IsType<LinearGradientBrush>(Mask(0.8)).GradientStops[2].Offset;

        Assert.True(early < late);
    }
}
