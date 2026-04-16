using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>
/// Tests for shape-outside on floats.
/// Full per-line shape wrapping requires a more advanced float model; these tests
/// verify property storage, shape-margin application, and no-crash behaviour.
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

    // ── no-crash tests ───────────────────────────────────────────────────────

    [Fact]
    public void ShapeOutside_WithText_DoesNotCrash()
    {
        // Shape-outside on a float with surrounding text should not throw
        var act = () => LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<div style='float:left; width:100px; height:80px; shape-outside:circle(50px)'>" +
            "Float content" +
            "</div>" +
            "<p>Text that flows next to the float. It should not crash even if the " +
            "shape isn't fully rendered yet.</p>" +
            "</body>", 400, 600);

        act.Should().NotThrow("layout with shape-outside should never crash");
    }
}
