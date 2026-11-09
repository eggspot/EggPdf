using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class CornerRadiiTests
{
    [Fact]
    public void FittedTo_RadiiOverflowingASide_ScaleBothAxesByOneFactor()
    {
        var radii = new CornerRadii(80f, 20f, 80f, 20f, 0f, 0f, 0f, 0f).FittedTo(100f, 100f);

        radii.TopLeftX.Should().BeApproximately(50f, 0.001f);
        radii.TopLeftY.Should().BeApproximately(12.5f, 0.001f);
        radii.TopRightX.Should().BeApproximately(50f, 0.001f);
    }

    [Fact]
    public void FittedTo_ACornerWithOneZeroRadius_IsSquareAndTakesNoRoom()
    {
        var radii = new CornerRadii(80f, 0f, 80f, 20f, 0f, 0f, 0f, 0f).FittedTo(100f, 100f);

        radii.TopRightX.Should().Be(80f, "the square top-left corner does not share the top side");
    }

    [Fact]
    public void Expanded_GrowsRoundedCornersAndKeepsSquareOnesSquare()
    {
        var radii = CornerRadii.Circular(10f, 0f, 2f, 0f).Expanded(-4f);

        radii.TopLeftX.Should().Be(6f);
        radii.TopRightX.Should().Be(0f);
        radii.BottomRightX.Should().Be(0f);
        CornerRadii.IsRounded(radii.BottomRightX, radii.BottomRightY).Should().BeFalse();
    }

    [Fact]
    public void IsSquare_NoRoundedCorner_IsTrue()
    {
        CornerRadii.Circular(0f, 0f, 0f, 0f).IsSquare.Should().BeTrue();
        new CornerRadii(5f, 0f, 0f, 0f, 0f, 0f, 0f, 0f).IsSquare.Should().BeTrue();
        CornerRadii.Circular(0f, 3f, 0f, 0f).IsSquare.Should().BeFalse();
    }
}
