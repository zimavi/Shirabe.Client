using Avalonia.Media;
using Shirabe.Client.Services;
using Xunit;

namespace Shirabe.Tests;

public class CoverAccentTests
{
    private static byte[] Pixels(params (byte R, byte G, byte B, byte A, int Count)[] groups) =>
        groups.SelectMany(g => Enumerable.Repeat(new[] { g.B, g.G, g.R, g.A }, g.Count)).SelectMany(p => p).ToArray();

    [Fact]
    public void Takes_the_dominant_colourful_hue_and_ignores_greys()
    {
        // Lots of grey and a little more blue than red: blue wins.
        var accent = CoverAccent.FromBgra(Pixels((128, 128, 128, 255, 500), (40, 60, 220, 255, 60), (220, 40, 40, 255, 30)));

        Assert.NotNull(accent);
        Assert.True(accent.Value.B > accent.Value.R && accent.Value.B > accent.Value.G);
    }

    [Theory]
    [InlineData(255, 230, 0)]   // yellow: too bright for white text
    [InlineData(0, 0, 255)]     // blue: too dark for a dark background
    [InlineData(230, 70, 110)]
    public void The_accent_stays_readable_on_the_dark_ui_and_under_white_text(byte r, byte g, byte b)
    {
        var accent = CoverAccent.FromBgra(Pixels((r, g, b, 255, 100)))!.Value;

        var luminance = CoverAccent.Luminance(accent);
        Assert.InRange(luminance, 0.11, 0.31);
    }

    [Fact]
    public void Greyscale_and_transparent_covers_have_no_accent()
    {
        Assert.Null(CoverAccent.FromBgra(Pixels((128, 128, 128, 255, 100), (0, 0, 0, 255, 100), (255, 255, 255, 255, 100))));
        Assert.Null(CoverAccent.FromBgra(Pixels((255, 0, 0, 0, 100))));
    }

    [Fact]
    public void A_cover_with_only_a_few_coloured_pixels_has_no_accent()
    {
        Assert.Null(CoverAccent.FromBgra(Pixels((128, 128, 128, 255, 990), (255, 0, 0, 255, 10))));
    }
}
