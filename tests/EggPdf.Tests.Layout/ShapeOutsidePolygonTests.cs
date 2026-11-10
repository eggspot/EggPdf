using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class ShapeOutsidePolygonTests
{
    [Fact]
    public void Polygon_Triangle_WidensFromApexToBase()
    {
        var shape = ShapeOutsideParser.Parse("polygon(50% 0, 100% 100%, 0 100%)", 100, 100, 16);
        shape.Should().NotBeNull("the triangle has its apex at the top centre and its base along the bottom");

        var ctx = new FloatContext();
        ctx.AddLeftFloat(0, 0, 100, 100, shape);

        float nearApex = ctx.GetLeftOffset(y: 10, lineHeight: 1);
        float nearBase = ctx.GetLeftOffset(y: 90, lineHeight: 1);

        nearApex.Should().BeApproximately(55f, 1f, "at y=10 the right edge runs from (50,0) to (100,100): x = 50 + 5");
        nearBase.Should().BeApproximately(95f, 1f);
    }

    [Fact]
    public void Polygon_RightFloat_UsesTheLeftEdgeOfTheShape()
    {
        var shape = ShapeOutsideParser.Parse("polygon(50% 0, 100% 100%, 0 100%)", 100, 100, 16);
        var ctx = new FloatContext();
        ctx.AddRightFloat(x: 200, y: 0, width: 100, height: 100, shape: shape);

        ctx.GetRightOffset(y: 50, lineHeight: 1, containerRight: 300).Should().BeApproximately(75f, 1f,
            "at y=50 the shape's left edge is at local x=25, so it consumes 300 - (200 + 25) = 75");
    }

    [Fact]
    public void Polygon_LineBoxSpanningAVertex_SeesTheVertexNotJustTheSamples()
    {
        var shape = ShapeOutsideParser.Parse("polygon(0 0, 10px 0, 10px 39px, 100px 40px, 10px 41px, 10px 100%, 0 100%)", 100, 100, 16);
        var ctx = new FloatContext();
        ctx.AddLeftFloat(0, 0, 100, 100, shape);

        ctx.GetLeftOffset(y: 30, lineHeight: 30).Should().BeApproximately(100f, 0.5f,
            "the spike reaches x=100 at y=40, between the 30px line box's top, middle and bottom samples (30, 45, 60), " +
            "and the whole line box must still clear it");
    }

    [Fact]
    public void Polygon_FillRuleKeyword_IsAcceptedAndIgnored()
    {
        var shape = ShapeOutsideParser.Parse("polygon(evenodd, 0 0, 50px 0, 50px 50px)", 100, 100, 16);
        shape.Should().NotBeNull();
        var ctx = new FloatContext();
        ctx.AddLeftFloat(0, 0, 100, 100, shape);
        ctx.GetLeftOffset(y: 1, lineHeight: 1).Should().BeApproximately(50f, 0.5f,
            "the right triangle (0,0)-(50,0)-(50,50) has its vertical edge at x=50 as its right side");
        ctx.GetLeftOffset(y: 60, lineHeight: 1).Should().Be(0f);
    }

    [Fact]
    public void Polygon_RowOutsideTheShape_DoesNotIndentContent()
    {
        var shape = ShapeOutsideParser.Parse("polygon(0 0, 100% 0, 100% 50%, 0 50%)", 100, 100, 16);
        var ctx = new FloatContext();
        ctx.AddLeftFloat(0, 0, 100, 100, shape);

        ctx.GetLeftOffset(y: 20, lineHeight: 10).Should().BeApproximately(100f, 0.5f);
        ctx.GetLeftOffset(y: 70, lineHeight: 10).Should().Be(0f,
            "the shape covers only the top half, so lines below it run at full width though the float box goes on to y=100");
    }

    [Fact]
    public void Polygon_TooFewOrBrokenPoints_ReturnsNull()
    {
        ShapeOutsideParser.Parse("polygon(0 0, 10px)", 100, 100, 16).Should().BeNull();
        ShapeOutsideParser.Parse("polygon()", 100, 100, 16).Should().BeNull();
        ShapeOutsideParser.Parse("polygon(0 0, foo bar, 5 5)", 100, 100, 16).Should().BeNull();
    }

    [Fact]
    public void Inset_UniformAndShorthand_ShrinkTheExclusionRectangle()
    {
        var shape = ShapeOutsideParser.Parse("inset(10px 20px 30px 5px)", 100, 100, 16);
        shape.Should().NotBeNull();
        var ctx = new FloatContext();
        ctx.AddLeftFloat(0, 0, 100, 100, shape);

        ctx.GetLeftOffset(y: 50, lineHeight: 1).Should().BeApproximately(80f, 0.5f, "right edge = width - right inset");
        ctx.GetLeftOffset(y: 2, lineHeight: 1).Should().Be(0f, "the top 10px is inset away");
        ctx.GetLeftOffset(y: 90, lineHeight: 1).Should().Be(0f, "the bottom 30px is inset away");
    }

    [Fact]
    public void Inset_SingleValueAndRoundSuffix_Parse()
    {
        var shape = ShapeOutsideParser.Parse("inset(10% round 8px)", 200, 100, 16);
        shape.Should().NotBeNull();
        var ctx = new FloatContext();
        ctx.AddLeftFloat(0, 0, 200, 100, shape);
        ctx.GetLeftOffset(y: 50, lineHeight: 1).Should().BeApproximately(180f, 0.5f);
    }

    [Fact]
    public void Inset_RightFloat_UsesLeftInset()
    {
        var shape = ShapeOutsideParser.Parse("inset(0 0 0 25px)", 100, 100, 16);
        var ctx = new FloatContext();
        ctx.AddRightFloat(200, 0, 100, 100, shape);
        ctx.GetRightOffset(y: 50, lineHeight: 1, containerRight: 300).Should().BeApproximately(75f, 0.5f);
    }
}
