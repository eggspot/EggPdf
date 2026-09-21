using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>
/// Tests for shape-outside on floats: property storage, shape-margin application, and
/// and text wrapping. Per-line wrapping around circle()/ellipse()/polygon()/inset() shapes
/// (the exclusion narrowing near a float's edges) is covered in detail by
/// <see cref="FloatTextWrapTests"/>, <see cref="FloatContextTests"/> and
/// <see cref="ShapeOutsidePolygonTests"/>; url() shapes fall back to the float's plain
/// rectangular exclusion (see ShapeOutsideParser).
/// </summary>
public class ShapeOutsideTests
{
    // ── style storage ────────────────────────────────────────────────────────

    [Fact]
    public void ShapeOutside_Circle_StyleStored()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='float:left; width:100px; height:100px; shape-outside:circle(50%)'>x</div>",
            400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("shape-outside").Should().Be("circle(50%)",
            "shape-outside: circle() should be preserved in computed style");
    }

    [Fact]
    public void ShapeOutside_Polygon_StyleStored()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='float:right; width:80px; shape-outside:polygon(0 0, 100% 0, 0 100%)'>y</div>",
            400, 600);
        var div = root.FindByTag("div");
        div!.Style.Get("shape-outside").Should().Be("polygon(0 0, 100% 0, 0 100%)");
    }

    [Fact]
    public void ShapeOutside_Inset_StyleStored()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='float:left; width:100px; shape-outside:inset(10px)'>z</div>",
            400, 600);
        var div = root.FindByTag("div");
        div!.Style.Get("shape-outside").Should().Be("inset(10px)");
    }

    // ── shape-margin ─────────────────────────────────────────────────────────

    [Fact]
    public void ShapeMargin_ExpandsFloatClearance()
    {
        // A float with shape-margin:20px should clear more space than one without.
        // We test this by comparing where subsequent siblings start (below the float).
        var rootNoMargin = LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<div style='float:left; width:60px; height:40px'>F</div>" +
            "<div style='clear:left; height:10px'>After</div>" +
            "</body>", 300, 600);

        var rootWithMargin = LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<div style='float:left; width:60px; height:40px; shape-margin:20px'>F</div>" +
            "<div style='clear:left; height:10px'>After</div>" +
            "</body>", 300, 600);

        // The "After" div should start lower in the shape-margin version
        var afterNoMargin = rootNoMargin.FindAllByTag("div");
        var afterWithMargin = rootWithMargin.FindAllByTag("div");

        // Find the non-float divs (After sibling)
        var afterBoxNoMargin = afterNoMargin.Find(b => !b.IsFloat);
        var afterBoxWithMargin = afterWithMargin.Find(b => !b.IsFloat);

        afterBoxNoMargin.Should().NotBeNull();
        afterBoxWithMargin.Should().NotBeNull();

        afterBoxWithMargin!.Y.Should().BeGreaterThan(afterBoxNoMargin!.Y,
            "shape-margin should expand the float's clear zone downward");
    }

    // ── text wrapping ────────────────────────────────────────────────────────

    [Fact]
    public void ShapeOutside_WithText_WrapsAroundTheFloatThenReturnsToFullWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<div style='float:left; width:100px; height:80px; shape-outside:circle(50px)'>" +
            "Float content" +
            "</div>" +
            "<p>Text that flows next to the float. It keeps going for several lines so that " +
            "the paragraph reaches well below the bottom of the float box and text starts at the left edge again.</p>" +
            "</body>", 400, 600);

        var words = root.FindByTag("p")!.Children.FindAll(c => !string.IsNullOrEmpty(c.Text));
        words.Should().NotBeEmpty();

        var beside = words.FindAll(w => w.Y < 60f);
        beside.Should().NotBeEmpty("the first lines sit next to the float");
        beside.Should().OnlyContain(w => w.X > 0f, "text beside the float is pushed right of the shape");

        var below = words.FindAll(w => w.Y >= 85f);
        below.Should().NotBeEmpty("the paragraph continues below the float");
        below.Should().Contain(w => w.X < 1f, "text starts at the left edge again once the float ends");
    }
}
