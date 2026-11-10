using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class FloatContextTests
{
    [Fact]
    public void GetLeftOffset_RectangularFloat_ReturnsRightEdgeWithinYRange()
    {
        var ctx = new FloatContext();
        ctx.AddLeftFloat(x: 10, y: 0, width: 50, height: 100);

        ctx.GetLeftOffset(y: 50, lineHeight: 20).Should().Be(60f, "the float's right edge is x 10 + width 50");
    }

    [Fact]
    public void GetLeftOffset_RectangularFloat_ReturnsZeroOutsideYRange()
    {
        var ctx = new FloatContext();
        ctx.AddLeftFloat(x: 10, y: 0, width: 50, height: 100);

        ctx.GetLeftOffset(y: 200, lineHeight: 20).Should().Be(0f);
    }

    [Fact]
    public void GetRightOffset_RectangularFloat_ReturnsConsumedWidth()
    {
        var ctx = new FloatContext();
        ctx.AddRightFloat(x: 250, y: 0, width: 50, height: 100);

        ctx.GetRightOffset(y: 50, lineHeight: 20, containerRight: 300).Should().Be(50f,
            "the float occupies x 250..300 at the container's right edge");
    }

    [Fact]
    public void GetAvailableWidth_ReducedByBothLeftAndRightFloats()
    {
        var ctx = new FloatContext();
        ctx.AddLeftFloat(x: 0, y: 0, width: 50, height: 100);
        ctx.AddRightFloat(x: 250, y: 0, width: 50, height: 100);

        ctx.GetAvailableWidth(y: 50, lineHeight: 20, containerWidth: 300, containerX: 0).Should().Be(200f,
            "each float takes 50px of the 300px container");
    }

    [Fact]
    public void GetContentStartX_IndentsPastLeftFloat()
    {
        var ctx = new FloatContext();
        ctx.AddLeftFloat(x: 0, y: 0, width: 50, height: 100);

        ctx.GetContentStartX(y: 50, lineHeight: 20, containerX: 0).Should().Be(50f);
    }

    [Fact]
    public void GetContentStartX_NoFloatAtThisY_StaysAtContainerX()
    {
        var ctx = new FloatContext();
        ctx.AddLeftFloat(x: 0, y: 0, width: 50, height: 100);

        ctx.GetContentStartX(y: 500, lineHeight: 20, containerX: 10).Should().Be(10f);
    }

    [Fact]
    public void GetLeftOffset_CircleShape_NarrowerThanFloatNearTopAndBottom()
    {
        var shape = ShapeOutsideParser.Parse("circle(50%)", 100, 100, 16);
        shape.Should().NotBeNull("circle(50%) on a 100x100 float is centered at (50,50) with radius 50");

        var ctx = new FloatContext();
        ctx.AddLeftFloat(x: 0, y: 0, width: 100, height: 100, shape: shape);

        float centerOffset = ctx.GetLeftOffset(y: 50, lineHeight: 1);
        float topOffset = ctx.GetLeftOffset(y: 1, lineHeight: 1);

        centerOffset.Should().BeApproximately(100f, 1f, "the circle spans its full width at the vertical center");
        topOffset.Should().BeLessThan(centerOffset, "the circle is much narrower near its top edge than at its center");
    }

    [Fact]
    public void GetLeftOffset_CircleShape_NarrowsToCenterXAtItsTopTangentPoint()
    {
        var shape = ShapeOutsideParser.Parse("circle(50%)", 100, 100, 16);
        var ctx = new FloatContext();
        ctx.AddLeftFloat(x: 0, y: 0, width: 100, height: 100, shape: shape);

        float topOffset = ctx.GetLeftOffset(y: 0, lineHeight: 0.01f);
        topOffset.Should().BeApproximately(50f, 1f, "at the circle's exact top tangent point the shape only reaches its center X (50), not the float's full width (100)");
    }

    [Fact]
    public void GetLeftOffset_OutsideFloatYRange_IsZeroRegardlessOfShape()
    {
        var shape = ShapeOutsideParser.Parse("circle(50%)", 100, 100, 16);
        var ctx = new FloatContext();
        ctx.AddLeftFloat(x: 0, y: 0, width: 100, height: 100, shape: shape);

        ctx.GetLeftOffset(y: 500, lineHeight: 20).Should().Be(0f, "a line entirely below the float (shaped or not) is unaffected");
    }

    [Fact]
    public void GetRightOffset_CircleShape_LessConsumedNearTopThanCenter()
    {
        var shape = ShapeOutsideParser.Parse("circle(50%)", 100, 100, 16);
        var ctx = new FloatContext();
        ctx.AddRightFloat(x: 200, y: 0, width: 100, height: 100, shape: shape);

        float centerConsumed = ctx.GetRightOffset(y: 50, lineHeight: 1, containerRight: 300);
        float topConsumed = ctx.GetRightOffset(y: 1, lineHeight: 1, containerRight: 300);

        topConsumed.Should().BeLessThan(centerConsumed, "less width is consumed near the circle's narrow top than its wide center");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("url(shape.png)")]
    public void Parse_UnsupportedOrEmptyValues_ReturnsNull(string? value)
    {
        ShapeOutsideParser.Parse(value, 100, 100, 16).Should().BeNull();
    }

    [Fact]
    public void Parse_CircleNoArgs_DefaultsToClosestSideCenteredCircle()
    {
        var shape = ShapeOutsideParser.Parse("circle()", 100, 60, 16);
        shape.Should().NotBeNull();
        shape!.Value.Cx.Should().BeApproximately(50f, 0.01f);
        shape.Value.Cy.Should().BeApproximately(30f, 0.01f);
        shape.Value.Rx.Should().BeApproximately(30f, 0.01f, "closest-side of a 100x60 box centered is 30 (half the smaller dimension)");
        shape.Value.Ry.Should().BeApproximately(30f, 0.01f);
    }

    [Fact]
    public void Parse_CircleWithExplicitPositionAndPixelRadius()
    {
        var shape = ShapeOutsideParser.Parse("circle(40px at 20px 30px)", 200, 200, 16);
        shape.Should().NotBeNull();
        shape!.Value.Cx.Should().BeApproximately(20f, 0.01f);
        shape.Value.Cy.Should().BeApproximately(30f, 0.01f);
        shape.Value.Rx.Should().BeApproximately(40f, 0.01f);
        shape.Value.Ry.Should().BeApproximately(40f, 0.01f);
    }

    [Fact]
    public void Parse_EllipseWithTwoPercentageRadii()
    {
        var shape = ShapeOutsideParser.Parse("ellipse(50% 25% at center)", 100, 100, 16);
        shape.Should().NotBeNull();
        shape!.Value.Rx.Should().BeApproximately(50f, 0.01f);
        shape.Value.Ry.Should().BeApproximately(25f, 0.01f);
    }

    [Fact]
    public void Parse_CircleWithCenterKeywordPosition()
    {
        var shape = ShapeOutsideParser.Parse("circle(30px at center)", 100, 100, 16);
        shape.Should().NotBeNull();
        shape!.Value.Cx.Should().BeApproximately(50f, 0.01f);
        shape.Value.Cy.Should().BeApproximately(50f, 0.01f);
    }
}
