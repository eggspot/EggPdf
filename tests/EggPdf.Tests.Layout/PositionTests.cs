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
        var root = Layout(
            "<div style='position:relative; height:200px'>" +
            "<div style='height:40px; background:blue'></div>" +
            "<div style='position:sticky; top:20px; height:30px; background:red'></div>" +
            "</div>");

        var outer = root.FindAllByTag("div")[0];
        outer.Children.Should().HaveCount(2);

        var normal = outer.Children[0];
        var sticky = outer.Children[1];

        float stickyNormalFlowY = normal.Y + normal.Height;
        sticky.Y.Should().BeApproximately(stickyNormalFlowY + 20f, 1f,
            "a printed page never scrolls, so sticky with top:20px acts like relative and shifts the box down 20px");
    }

    [Fact]
    public void Sticky_TopZero_StaysInNormalFlow_AndContentFollowsIt()
    {
        var root = Layout(
            "<div style='position:sticky; top:0; background:white; height:50px'>Header</div>" +
            "<p>Content below</p>");

        var header = root.FindAllByTag("div")[0];
        var p = root.FindByTag("p")!;

        header.Height.Should().BeApproximately(50f, 0.5f);
        p.Y.Should().BeGreaterOrEqualTo(header.Y + 50f - 0.5f, "sticky keeps its space in flow, so the paragraph starts below it");
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
