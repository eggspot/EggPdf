using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>shape-outside: polygon() and inset() exclusion shapes.</summary>
public class ShapeOutsidePolygonTests
{
    [Fact]
    public void Polygon_Triangle_WidensFromApexToBase()
    {
        // Apex at the top centre, base along the bottom: a right-pointing wedge for a left float
        var shape = ShapeOutsideParser.Parse("polygon(50% 0, 100% 100%, 0 100%)", 100, 100, 16);
        shape.Should().NotBeNull();

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
        ctx.AddRightFloat(x: 200, y: 0, width: 100, height: 100, shape: shape); // container right = 300

        // Left edge at y=50 is x=25 local -> the float consumes 300 - (200 + 25) = 75
        ctx.GetRightOffset(y: 50, lineHeight: 1, containerRight: 300).Should().BeApproximately(75f, 1f);
    }

    [Fact]
    public void Polygon_LineBoxSpanningAVertex_SeesTheVertexNotJustTheSamples()
    {
        // A spike at y=40 reaches x=100 between the top/middle/bottom sample points of a 30px line
        // box at y=30..60 -> sampling top(30)/mid(45)/bottom(60) alone would miss most of it.
        var shape = ShapeOutsideParser.Parse("polygon(0 0, 10px 0, 10px 39px, 100px 40px, 10px 41px, 10px 100%, 0 100%)", 100, 100, 16);
        var ctx = new FloatContext();
        ctx.AddLeftFloat(0, 0, 100, 100, shape);

        ctx.GetLeftOffset(y: 30, lineHeight: 30).Should().BeApproximately(100f, 0.5f,
            "the whole line box must clear the spike that pokes into it");
    }

    [Fact]
    public void Polygon_FillRuleKeyword_IsAcceptedAndIgnored()
    {
        var shape = ShapeOutsideParser.Parse("polygon(evenodd, 0 0, 50px 0, 50px 50px)", 100, 100, 16);
        shape.Should().NotBeNull();
        var ctx = new FloatContext();
        ctx.AddLeftFloat(0, 0, 100, 100, shape);
        // Right triangle (0,0)-(50,0)-(50,50): the vertical edge at x=50 is its right side
        ctx.GetLeftOffset(y: 1, lineHeight: 1).Should().BeApproximately(50f, 0.5f);
        ctx.GetLeftOffset(y: 60, lineHeight: 1).Should().Be(0f);
    }

    [Fact]
    public void Polygon_RowOutsideTheShape_DoesNotIndentContent()
    {
        // The shape only covers the top half; below it lines run at full width even though the
        // float box itself extends to y=100.
        var shape = ShapeOutsideParser.Parse("polygon(0 0, 100% 0, 100% 50%, 0 50%)", 100, 100, 16);
        var ctx = new FloatContext();
        ctx.AddLeftFloat(0, 0, 100, 100, shape);

        ctx.GetLeftOffset(y: 20, lineHeight: 10).Should().BeApproximately(100f, 0.5f);
        ctx.GetLeftOffset(y: 70, lineHeight: 10).Should().Be(0f);
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
