using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>
/// float:left/right must narrow sibling inline content per line (real CSS text-wrap-around-
/// floats), not just be positioned correctly itself while text ignores it. shape-outside
/// circle()/ellipse() refines the exclusion to the shape's actual per-line extent instead of
/// the float's full rectangular bounding box.
/// </summary>
public class FloatTextWrapTests
{
    private static LayoutBox FirstWordBox(LayoutBox p)
        => p.Children.First(c => !string.IsNullOrEmpty(c.Text) && c.Text.Trim().Length > 0);

    [Fact]
    public void ParagraphBesideLeftFloat_FirstLineIndentedPastFloat()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width:300px'>" +
            "<div style='float:left;width:100px;height:100px;background:red'></div>" +
            "<p>Lorem ipsum dolor sit amet consectetur</p>" +
            "</div>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        var firstWord = FirstWordBox(p!);

        // The paragraph box's own left edge is at X=8 (body margin); its first word must
        // start at or past the float's right edge (8 + 100 = 108), not at the plain X=8
        // the pre-fix engine placed it at regardless of the float.
        firstWord.X.Should().BeGreaterThanOrEqualTo(108f - 0.5f,
            "the first line of text must be indented past the float, not ignore it");
    }

    [Fact]
    public void ParagraphBesideLeftFloat_LineBelowFloatUsesFullWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width:200px'>" +
            "<div style='float:left;width:100px;height:20px;background:red'></div>" +
            "<p>" + string.Join(" ", Enumerable.Repeat("word", 40)) + "</p>" +
            "</div>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        // Some word, well past the float's 20px height, should start back near the
        // paragraph's own left edge (X=8) since the float no longer applies there.
        var laterWords = p!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        var belowFloat = laterWords.FirstOrDefault(w => w.Y - p.Y > 25);
        belowFloat.Should().NotBeNull("with 40 repeated words at 100px available width, wrapping must continue past the float's bottom");
        belowFloat!.X.Should().BeLessThan(30f, "once below the float, a line can start back at the paragraph's own left edge");
    }

    [Fact]
    public void ParagraphBesideRightFloat_FirstLineEndsBeforeFloat()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width:300px'>" +
            "<div style='float:right;width:100px;height:100px;background:red'></div>" +
            "<p>" + string.Join(" ", Enumerable.Repeat("wordwordword", 10)) + "</p>" +
            "</div>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        var firstWord = FirstWordBox(p!);

        // Body left margin (8) + container width (300) - float width (100) = 208 is the
        // right float's left edge; the first word must fit entirely to its left.
        (firstWord.X + firstWord.Width).Should().BeLessThanOrEqualTo(208.5f,
            "text beside a right float must not extend into the float's region on the first line");
    }

    [Fact]
    public void NoFloats_ParagraphUnaffected()
    {
        // Regression guard: without any float in the formatting context, wrapping must be
        // completely unchanged (byte-identical to the pre-existing behavior).
        var root = LayoutTestHelper.Layout(
            "<div style='width:300px'><p>Lorem ipsum dolor sit amet</p></div>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        var firstWord = FirstWordBox(p!);
        firstWord.X.Should().BeApproximately(8f, 0.5f, "with no floats, the first word starts at the paragraph's own left edge as before");
    }

    [Fact]
    public void ClearBoth_StillPushesContentBelowFloats()
    {
        // Regression guard for the pre-existing clear:both mechanism, now sharing floatCtx.
        var root = LayoutTestHelper.Layout(
            "<div style='width:300px'>" +
            "<div style='float:left;width:100px;height:150px;background:red'></div>" +
            "<div style='clear:both' id='after'>After</div>" +
            "</div>", 600, 800);

        var afterDiv = root.FindById("after");
        afterDiv.Should().NotBeNull();
        (afterDiv!.Y - root.FindByTag("div")!.Y).Should().BeGreaterThanOrEqualTo(149f,
            "clear:both must still push content below the float's bottom");
    }

    [Fact]
    public void ShapeOutsideCircle_TextIndentsLessNearFloatTopThanCenter()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width:300px'>" +
            "<div style='float:left;width:100px;height:100px;shape-outside:circle(50%);background:red'></div>" +
            "<p>" + string.Join(" ", Enumerable.Repeat("w", 60)) + "</p>" +
            "</div>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        var firstWord = FirstWordBox(p!);

        // At the very top of the float (where the circle only just begins), the shape's
        // exclusion is much narrower than the float's full 100px width -- the first line
        // must start noticeably less indented than the plain-rectangle case (108px).
        firstWord.X.Should().BeLessThan(108f - 5f,
            "shape-outside:circle() must narrow the exclusion near the float's top edge below the full rectangular width");
    }

    [Fact]
    public void ShapeOutsidePolygon_TriangleLetsFirstLineStartNearTheApex()
    {
        // Apex at the top-left, widening downward: the first line only needs to clear a sliver
        var root = LayoutTestHelper.Layout(
            "<div style='width:300px'>" +
            "<div style='float:left;width:100px;height:100px;shape-outside:polygon(0 0, 100% 100%, 0 100%);background:red'></div>" +
            "<p>" + string.Join(" ", Enumerable.Repeat("w", 60)) + "</p>" +
            "</div>", 600, 800);

        var firstWord = FirstWordBox(root.FindByTag("p")!);

        firstWord.X.Should().BeLessThan(108f - 20f,
            "the polygon's exclusion is a few pixels wide near its apex, far below the float's full width");
    }
}
