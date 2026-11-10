using System;
using EggPdf.Svg;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Svg;

public class FilterPixelsTests
{
    [Fact]
    public void Turbulence_HugeOctaveCount_MatchesTheLastContributingOctave()
    {
        FilterImage Noise(int octaves) => FilterPixels.Turbulence(64, 64, false, 0.05f, 0.05f, octaves, 4, true, false,
            0f, 0f, 1f, 0f, 0f, 64f, 64f);

        Noise(int.MaxValue).Px.Should().Equal(Noise(FilterPixels.MaxContributingOctaves).Px);
    }

    private static FilterImage Pixel(float r, float g, float b, float a)
    {
        var img = new FilterImage(1, 1);
        img.Px[0] = r * a; img.Px[1] = g * a; img.Px[2] = b * a; img.Px[3] = a;
        return img;
    }

    [Fact]
    public void Blend_Luminosity_KeepsTheBackdropHueWithTheSourceLuminosity()
    {
        var result = FilterPixels.Blend(Pixel(0.5f, 0.5f, 0.5f, 1f), Pixel(1f, 0f, 0f, 1f), "luminosity");

        result.Px[0].Should().BeApproximately(1f, 0.001f);
        result.Px[1].Should().BeApproximately(0.2857f, 0.001f);
        result.Px[2].Should().BeApproximately(0.2857f, 0.001f);
    }

    [Fact]
    public void Blend_Saturation_OfAGraySource_DesaturatesTheBackdrop()
    {
        var result = FilterPixels.Blend(Pixel(0.5f, 0.5f, 0.5f, 1f), Pixel(1f, 0f, 0f, 1f), "saturation");

        result.Px[0].Should().BeApproximately(0.3f, 0.001f);
        result.Px[1].Should().BeApproximately(0.3f, 0.001f);
        result.Px[2].Should().BeApproximately(0.3f, 0.001f);
    }

    [Fact]
    public void Blend_Color_TakesTheSourceHueAtTheBackdropLuminosity()
    {
        var result = FilterPixels.Blend(Pixel(0f, 0f, 1f, 1f), Pixel(0.5f, 0.5f, 0.5f, 1f), "color");

        result.Px[0].Should().BeApproximately(0.4382f, 0.001f);
        result.Px[2].Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void Composite_Lighter_AddsBothInputs()
    {
        var result = FilterPixels.Composite(Pixel(1f, 0f, 0f, 0.5f), Pixel(0f, 1f, 0f, 0.5f), "lighter", 0, 0, 0, 0);

        result.Px.Should().Equal(0.5f, 0.5f, 0f, 1f);
    }

    [Theory]
    [InlineData(true, 1, 2)]
    [InlineData(false, 1, 2)]
    [InlineData(true, 3, 0)]
    [InlineData(false, 2, 3)]
    public void Morphology_MatchesTheWindowDefinition(bool dilate, int radiusX, int radiusY)
    {
        var random = new Random(7);
        var src = new FilterImage(13, 9);
        for (int i = 0; i < src.Px.Length; i++) src.Px[i] = (float)random.NextDouble();

        var result = FilterPixels.Morphology(src, dilate, radiusX, radiusY);

        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                for (int c = 0; c < 4; c++)
                    result.Px[(y * src.Width + x) * 4 + c].Should().Be(Reference(src, dilate, radiusX, radiusY, x, y, c),
                        $"pixel ({x},{y}) channel {c}");
    }

    [Fact]
    public void Morphology_HugeRadius_DilatesOnePixelOverTheWholeImage()
    {
        var src = new FilterImage(200, 200);
        int center = (100 * 200 + 100) * 4;
        src.Px[center + 3] = 0.75f;

        var result = FilterPixels.Morphology(src, dilate: true, 100000, 100000);

        result.Px[3].Should().Be(0.75f);
        result.Px[result.Px.Length - 1].Should().Be(0.75f);
    }

    private static float Reference(FilterImage src, bool dilate, int rx, int ry, int x, int y, int c)
    {
        var afterX = new float[src.Height];
        for (int yy = 0; yy < src.Height; yy++) afterX[yy] = Window(src, dilate, rx, x, yy, c, horizontal: true);
        if (ry == 0) return afterX[y];
        float best = dilate ? 0f : 1f;
        for (int k = -ry; k <= ry; k++)
        {
            int yy = y + k;
            float v = yy < 0 || yy >= src.Height ? 0f : rx == 0 ? src.Px[(yy * src.Width + x) * 4 + c] : afterX[yy];
            best = dilate ? Math.Max(best, v) : Math.Min(best, v);
        }
        return best;
    }

    private static float Window(FilterImage src, bool dilate, int radius, int x, int y, int c, bool horizontal)
    {
        if (radius == 0) return src.Px[(y * src.Width + x) * 4 + c];
        float best = dilate ? 0f : 1f;
        for (int k = -radius; k <= radius; k++)
        {
            int xx = horizontal ? x + k : x;
            float v = xx < 0 || xx >= src.Width ? 0f : src.Px[(y * src.Width + xx) * 4 + c];
            best = dilate ? Math.Max(best, v) : Math.Min(best, v);
        }
        return best;
    }
}
