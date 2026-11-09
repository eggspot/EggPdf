using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class BorderShadeTests
{
    private static float Byte(int value) => value / 255f;

    [Theory]
    [InlineData((int)BorderSide.Top, true, true)]
    [InlineData((int)BorderSide.Left, true, true)]
    [InlineData((int)BorderSide.Bottom, true, false)]
    [InlineData((int)BorderSide.Right, true, false)]
    [InlineData((int)BorderSide.Top, false, false)]
    [InlineData((int)BorderSide.Right, false, true)]
    public void DarkensSide_FollowsInsetAndTheSide(int side, bool inset, bool expected)
    {
        BorderShade.DarkensSide((BorderSide)side, inset).Should().Be(expected);
    }

    [Fact]
    public void InsetOutsetColor_MidGrey_DarkensAndLightensByAThird()
    {
        float grey = Byte(128);

        BorderShade.InsetOutsetColor(true, grey, grey, grey).Should().Be((Byte(44), Byte(44), Byte(44)));
        BorderShade.InsetOutsetColor(false, grey, grey, grey).Should().Be((Byte(212), Byte(212), Byte(212)));
    }

    [Fact]
    public void InsetOutsetColor_Black_IsLightenedOnBothSides()
    {
        BorderShade.InsetOutsetColor(true, 0f, 0f, 0f).Should().Be((Byte(84), Byte(84), Byte(84)));
        BorderShade.InsetOutsetColor(false, 0f, 0f, 0f).Should().Be((Byte(168), Byte(168), Byte(168)));
    }

    [Fact]
    public void InsetOutsetColor_White_DarkensButIsNotLightened()
    {
        BorderShade.InsetOutsetColor(true, 1f, 1f, 1f).Should().Be((Byte(171), Byte(171), Byte(171)));
        BorderShade.InsetOutsetColor(false, 1f, 1f, 1f).Should().Be((1f, 1f, 1f));
    }

    [Fact]
    public void InsetOutsetColor_SaturatedColour_KeepsItsHue()
    {
        var (r, g, b) = BorderShade.InsetOutsetColor(true, 1f, 0f, 0f);

        r.Should().Be(Byte(171));
        g.Should().Be(0f);
        b.Should().Be(0f);
    }
}
