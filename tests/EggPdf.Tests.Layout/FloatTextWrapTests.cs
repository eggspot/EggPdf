using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class FloatTextWrapTests
{
    private const float BodyMargin = 8f;

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

        firstWord.X.Should().BeGreaterThanOrEqualTo(BodyMargin + 100f - 0.5f,
            "the first line of text must start past the 100px float's right edge, not at the paragraph's own left edge");
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

        float rightFloatLeftEdge = BodyMargin + 300f - 100f;
        (firstWord.X + firstWord.Width).Should().BeLessThanOrEqualTo(rightFloatLeftEdge + 0.5f,
            "text beside a right float must not extend into the float's region on the first line");
    }

    [Fact]
    public void NoFloats_ParagraphUnaffected()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width:300px'><p>Lorem ipsum dolor sit amet</p></div>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        var firstWord = FirstWordBox(p!);
        firstWord.X.Should().BeApproximately(BodyMargin, 0.5f, "with no floats, the first word starts at the paragraph's own left edge as before");
    }

    [Fact]
    public void ClearBoth_StillPushesContentBelowFloats()
    {
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
            "<p style='margin:0'>" + string.Join(" ", Enumerable.Repeat("w", 60)) + "</p>" +
            "</div>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        var firstWord = FirstWordBox(p!);

        firstWord.X.Should().BeLessThan(BodyMargin + 100f - 5f,
            "at the float's top edge the circle has barely begun, so its exclusion is much narrower than the full 100px rectangle");
    }

    [Fact]
    public void ParagraphWithTopMargin_LinesBelowTheFloat_ReturnToTheLeftEdge()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<div style='float:left;width:100px;height:80px'>F</div>" +
            "<p>" + string.Join(" ", Enumerable.Repeat("word", 80)) + "</p>" +
            "</body>", 400, 800);

        var lines = root.FindByTag("p")!.Children.FindAll(c => !string.IsNullOrEmpty(c.Text));
        var beside = lines.FindAll(l => l.Y + l.Height <= 80.5f);
        var below = lines.FindAll(l => l.Y >= 80.5f);

        beside.Should().NotBeEmpty();
        beside.Should().OnlyContain(l => l.X >= 99.5f, "lines wholly beside the float start right of it");
        below.Should().NotBeEmpty();
        below.Should().OnlyContain(l => l.X < 0.5f,
            "lines wholly below the 80px float use the full width, even though the p's top margin moved them down");
    }

    [Fact]
    public void ShapeOutsidePolygon_TriangleLetsFirstLineStartNearTheApex()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width:300px'>" +
            "<div style='float:left;width:100px;height:100px;shape-outside:polygon(0 0, 100% 100%, 0 100%);background:red'></div>" +
            "<p>" + string.Join(" ", Enumerable.Repeat("w", 60)) + "</p>" +
            "</div>", 600, 800);

        var firstWord = FirstWordBox(root.FindByTag("p")!);

        firstWord.X.Should().BeLessThan(BodyMargin + 100f - 20f,
            "the triangle's apex is at the top-left and it widens downward, so near the apex its exclusion is only a sliver");
    }
}
