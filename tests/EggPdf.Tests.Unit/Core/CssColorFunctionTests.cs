using EggPdf.Core;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Core;

public class CssColorFunctionTests
{
    [Fact]
    public void ColorMix_ArgumentWithCommas_IsParsed()
    {
        var c = Color.TryParse("color-mix(in srgb, rgb(255, 0, 0), rgba(0, 0, 255, 1))");

        c.Should().NotBeNull();
        c!.Value.R.Should().BeInRange(127, 128);
        c.Value.B.Should().BeInRange(127, 128);
    }

    [Fact]
    public void ColorMix_WithTransparent_KeepsTheHueAndHalvesTheAlpha()
    {
        var c = Color.TryParse("color-mix(in srgb, red, transparent)");

        c.Should().Be(Color.FromRgba(255, 0, 0, 128));
    }

    [Fact]
    public void ColorMix_PercentagesSummingBelow100_ScaleTheAlpha()
    {
        var c = Color.TryParse("color-mix(in srgb, red 30%, blue 30%)");

        c.Should().NotBeNull();
        c!.Value.R.Should().BeInRange(127, 128);
        c.Value.B.Should().BeInRange(127, 128);
        c.Value.A.Should().Be(153);
    }

    [Fact]
    public void ColorMix_PercentageBeforeTheColor_IsAccepted()
    {
        var c = Color.TryParse("color-mix(in srgb, 25% red, blue)");

        c.Should().NotBeNull();
        c!.Value.R.Should().BeInRange(63, 65);
        c.Value.B.Should().BeInRange(190, 192);
    }

    [Fact]
    public void ColorMix_InOklab_MixesPerceptually()
    {
        var c = Color.TryParse("color-mix(in oklab, white, black)");

        c.Should().NotBeNull();
        c!.Value.R.Should().BeInRange(98, 100);
        c.Value.G.Should().Be(c.Value.R);
        c.Value.B.Should().Be(c.Value.R);
    }

    [Fact]
    public void ColorMix_PercentagesSummingToZero_IsInvalid()
    {
        Color.TryParse("color-mix(in srgb, red 0%, blue 0%)").Should().BeNull();
    }

    [Theory]
    [InlineData("hsl(0.5turn 100% 50%)")]
    [InlineData("hsl(3.14159265rad 100% 50%)")]
    [InlineData("hsl(200grad 100% 50%)")]
    [InlineData("hsl(180deg 100% 50%)")]
    [InlineData("hsl(180 100% 50%)")]
    public void Hsl_HueInAnyAngleUnit_IsConverted(string css)
    {
        Color.TryParse(css).Should().Be(Color.FromRgb(0, 255, 255));
    }

    [Theory]
    [InlineData("lab(29.2345% 39.3825 20.0664)")]
    [InlineData("lch(29.2345% 44.2 27)")]
    [InlineData("oklch(40.101% 0.12332 21.555)")]
    public void CieLabAndOklab_SpecExample_IsTheSameSrgbColor(string css)
    {
        var c = Color.TryParse(css);

        c.Should().NotBeNull();
        c!.Value.R.Should().BeInRange(124, 126, "CSS Color 4's example is rgb(49.06% 13.62% 16.03%), with lab() relative to D50");
        c.Value.G.Should().BeInRange(34, 36);
        c.Value.B.Should().BeInRange(40, 42);
    }

    [Fact]
    public void Lch_HueInTurns_IsConverted()
    {
        var degrees = Color.TryParse("lch(50 40 90deg)");
        var turns = Color.TryParse("lch(50 40 0.25turn)");

        turns.Should().Be(degrees);
    }

    [Fact]
    public void ColorFunction_DisplayP3_UsesThePerceptualTransferFunction()
    {
        var c = Color.TryParse("color(display-p3 0.6 0.4 0.3)");

        c.Should().NotBeNull();
        c!.Value.R.Should().BeInRange(160, 164);
        c.Value.G.Should().BeInRange(97, 101);
        c.Value.B.Should().BeInRange(69, 73);
    }

    [Fact]
    public void ColorFunction_SrgbLinear_IsGammaEncoded()
    {
        Color.TryParse("color(srgb-linear 0.2 0.2 0.2)").Should().Be(Color.FromRgb(124, 124, 124));
    }

    [Theory]
    [InlineData("color(rec2020 0.5 0.5 0.5)", 139)]
    [InlineData("color(prophoto-rgb 0.5 0.5 0.5)", 146)]
    public void ColorFunction_WideGamutGray_ConvertedThroughItsTransferFunction(string css, int expectedChannel)
    {
        var c = Color.TryParse(css);

        c.Should().NotBeNull();
        c!.Value.R.Should().BeInRange((byte)(expectedChannel - 2), (byte)(expectedChannel + 2));
        c.Value.G.Should().BeInRange((byte)(expectedChannel - 2), (byte)(expectedChannel + 2));
        c.Value.B.Should().BeInRange((byte)(expectedChannel - 2), (byte)(expectedChannel + 2));
    }

    [Theory]
    [InlineData("color(srgb 1 1 1)")]
    [InlineData("color(srgb-linear 1 1 1)")]
    [InlineData("color(display-p3 1 1 1)")]
    [InlineData("color(a98-rgb 1 1 1)")]
    [InlineData("color(rec2020 1 1 1)")]
    [InlineData("color(prophoto-rgb 1 1 1)")]
    [InlineData("color(xyz 0.9505 1 1.089)")]
    [InlineData("color(xyz-d65 0.9505 1 1.089)")]
    [InlineData("color(xyz-d50 0.9642 1 0.8252)")]
    public void ColorFunction_WhiteInEverySpace_IsWhite(string css)
    {
        var c = Color.TryParse(css);

        c.Should().NotBeNull();
        c!.Value.R.Should().BeGreaterOrEqualTo(253);
        c.Value.G.Should().BeGreaterOrEqualTo(253);
        c.Value.B.Should().BeGreaterOrEqualTo(253);
    }

    [Fact]
    public void ColorFunction_PercentComponents_AreFractionsOfOne()
    {
        Color.TryParse("color(srgb 50% 0% 100%)").Should().Be(Color.FromRgb(128, 0, 255));
    }

    [Fact]
    public void ColorFunction_UnknownColorSpace_IsInvalid()
    {
        Color.TryParse("color(--brand 1 0 0)").Should().BeNull();
    }

    [Theory]
    [InlineData("TRANSPARENT")]
    [InlineData("Transparent")]
    public void TryParse_TransparentKeyword_IsCaseInsensitive(string css)
    {
        Color.TryParse(css).Should().Be(Color.Transparent);
    }
}
