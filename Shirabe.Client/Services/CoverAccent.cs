using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Shirabe.Client.Services;

/// <summary>Picks an accent colour from a cover: the most prominent colourful hue, adjusted so it works on the dark UI.</summary>
public static class CoverAccent
{
    private const int HueBins = 24;

    // Share of the picture the winning hue must cover, else the cover counts as colourless. Counted in pixels, not in
    // weight: a dull colour (steel blue on a mostly black cover) is still the colour of the cover.
    private const double MinColourShare = 0.02;

    // The accent is a background under white text (accent buttons) and a foreground on near-black surfaces
    // (progress fill, icons), so its luminance has to sit between those two. WCAG relative luminance.
    private const double MinLuminance = 0.12;
    private const double MaxLuminance = 0.30;

    /// <returns>Null when the cover has no real colour (greyscale, mostly transparent).</returns>
    public static Color? FromBitmap(Bitmap bitmap)
    {
        using var copy = new WriteableBitmap(bitmap.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var frame = copy.Lock();
        bitmap.CopyPixels(frame);

        var width = bitmap.PixelSize.Width * 4;
        var pixels = new byte[width * bitmap.PixelSize.Height];
        for (var row = 0; row < bitmap.PixelSize.Height; row++)
            System.Runtime.InteropServices.Marshal.Copy(frame.Address + row * frame.RowBytes, pixels, row * width, width);

        return FromBgra(pixels);
    }

    /// <param name="bgra">Tightly packed 4-byte pixels, blue first.</param>
    public static Color? FromBgra(ReadOnlySpan<byte> bgra)
    {
        Span<double> weight = stackalloc double[HueBins];
        Span<double> hueSum = stackalloc double[HueBins];
        Span<double> satSum = stackalloc double[HueBins];
        Span<int> pixels = stackalloc int[HueBins];

        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < 128) continue;

            var (hue, sat, value) = ToHsv(bgra[i + 2] / 255.0, bgra[i + 1] / 255.0, bgra[i] / 255.0);
            if (sat < 0.25 || value < 0.25) continue; // grey, black and white say nothing about the colour

            var w = sat * value; // vivid pixels count more than washed out ones
            var bin = Math.Min((int)(hue / 360 * HueBins), HueBins - 1);
            pixels[bin]++;
            weight[bin] += w;
            hueSum[bin] += w * hue;
            satSum[bin] += w * sat;
        }

        var best = 0;
        for (var bin = 1; bin < HueBins; bin++)
            if (weight[bin] > weight[best]) best = bin;

        if (pixels[best] < MinColourShare * (bgra.Length / 4)) return null;

        return Fit(hueSum[best] / weight[best], satSum[best] / weight[best]);
    }

    private static Color Fit(double hue, double saturation)
    {
        saturation = Math.Clamp(saturation, 0.5, 0.9);
        var value = 0.9;

        while (value > 0.4 && Luminance(FromHsv(hue, saturation, value)) > MaxLuminance) value -= 0.02;
        while (saturation > 0.25 && Luminance(FromHsv(hue, saturation, value)) < MinLuminance) saturation -= 0.02; // blues: wash towards white

        return FromHsv(hue, saturation, value);
    }

    private static (double Hue, double Saturation, double Value) ToHsv(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var delta = max - Math.Min(r, Math.Min(g, b));
        if (delta == 0) return (0, 0, max);

        var sector = max == r ? (g - b) / delta % 6 : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
        var hue = sector * 60;
        return (hue < 0 ? hue + 360 : hue, delta / max, max);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        double Channel(int n)
        {
            var k = (n + hue / 60) % 6;
            return value - value * saturation * Math.Clamp(Math.Min(k, 4 - k), 0, 1);
        }

        return Color.FromRgb((byte)Math.Round(Channel(5) * 255), (byte)Math.Round(Channel(3) * 255), (byte)Math.Round(Channel(1) * 255));
    }

    public static double Luminance(Color c)
    {
        static double Linear(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }
}
