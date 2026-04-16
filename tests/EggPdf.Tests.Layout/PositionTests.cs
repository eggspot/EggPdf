using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class PositionTests
{
    private LayoutBox Layout(string html) => LayoutTestHelper.Layout(html);

    [Fact]
    public void Sticky_TopOffset_AppliedLikeRelative()
    {
        // position:sticky with top:20px should shift the element down by 20px
        // compared to its normal-flow position (same as position:relative for PDF)
        var root = Layout(
            "<div style='position:relative; height:200px'>" +
            "<div style='height:40px; background:blue'></div>" +
            "<div style='position:sticky; top:20px; height:30px; background:red'></div>" +
            "</div>");

        var outer = root.FindAllByTag("div")[0];
        outer.Children.Count.Should().BeGreaterOrEqualTo(2);

        var normal = outer.Children[0];  // non-sticky: at Y=outer.Y
        var sticky = outer.Children[1];  // sticky: should be offset by 20px from normal position

        // Sticky element's normal-flow position would be at normal.Y + normal.Height
        float expectedNormalY = normal.Y + normal.Height;
        // With top:20px sticky offset, Y should be expectedNormalY + 20px
        sticky.Y.Should().BeApproximately(expectedNormalY + 20f, 1f);
    }

    [Fact]
    public void Sticky_DoesNotCrash()
    {
        var act = () => Layout(
            "<div style='position:sticky; top:0; background:white; height:50px'>Header</div>" +
            "<p>Content below</p>");
        act.Should().NotThrow();
    }

    [Fact]
    public void Sticky_StoredInStyle()
    {
        var root = Layout("<div style='position:sticky; top:10px'>text</div>");
        var div = root.FindAllByTag("div")[0];
        div.Style.Get("position").Should().Be("sticky");
        div.Style.Get("top").Should().Be("10px");
    }
}
