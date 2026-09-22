using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>
/// direction:rtl support beyond the box-model/text-align logical properties already covered
/// by LogicalPropertyTests: table column visual order and list marker positioning.
/// </summary>
public class RtlTableAndListTests
{
    [Fact]
    public void Table_LTR_FirstColumnAtLeftEdge()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'><table style='width:300px'><tr><td>A</td><td>B</td><td>C</td></tr></table></body>",
            300, 600);
        var cells = root.FindAllByTag("td");
        cells.Should().HaveCount(3);
        // Regression guard: DOM order A,B,C stays left-to-right in LTR.
        cells[0].X.Should().BeLessThan(cells[1].X);
        cells[1].X.Should().BeLessThan(cells[2].X);
    }

    [Fact]
    public void Table_RTL_FirstColumnAtRightEdge()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0; direction:rtl'><table style='width:300px'><tr><td>A</td><td>B</td><td>C</td></tr></table></body>",
            300, 600);
        var cells = root.FindAllByTag("td");
        cells.Should().HaveCount(3);
        // In RTL, DOM order A,B,C must lay out right-to-left: A rightmost, C leftmost.
        cells[0].X.Should().BeGreaterThan(cells[1].X,
            "the first <td> in DOM order must render at the right edge in an RTL table");
        cells[1].X.Should().BeGreaterThan(cells[2].X);
    }

    [Fact]
    public void ListMarker_LTR_HangsToLeftOfContent()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'><ul style='margin:0; padding-left:40px'><li>Item</li></ul></body>", 400, 600);
        var li = root.FindByTag("li");
        li.Should().NotBeNull();
        var marker = li!.Children.FirstOrDefault(c => c.IsListMarker);
        marker.Should().NotBeNull();
        marker!.X.Should().BeLessThan(li.X, "an outside marker in LTR hangs to the left of the principal box");
    }

    [Fact]
    public void ListMarker_RTL_HangsToRightOfContent()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0; direction:rtl'><ul style='margin:0; padding-right:40px'><li>Item</li></ul></body>", 400, 600);
        var li = root.FindByTag("li");
        li.Should().NotBeNull();
        var marker = li!.Children.FirstOrDefault(c => c.IsListMarker);
        marker.Should().NotBeNull();
        marker!.X.Should().BeGreaterThanOrEqualTo(li.X + li.Width,
            "an outside marker in RTL must hang to the right of the principal box, not the left");
    }
}
