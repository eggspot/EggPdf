using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class MarginCollapseTests
{
    [Fact]
    public void AdjacentSiblings_MarginsCollapse()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='margin-bottom: 20px; height: 50px; background-color: red'></div>" +
            "<div style='margin-top: 30px; height: 50px; background-color: blue'></div>",
            600, 800);

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCountGreaterOrEqualTo(2);

        float gap = divs[1].Y - (divs[0].Y + divs[0].Height);
        gap.Should().BeApproximately(30, 1f, "the collapsed margin is max(20, 30) = 30, not their sum 50");
    }

    [Fact]
    public void AdjacentSiblings_EqualMargins_Collapse()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='margin-bottom: 20px; height: 50px'></div>" +
            "<div style='margin-top: 20px; height: 50px'></div>",
            600, 800);

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCountGreaterOrEqualTo(2);

        float gap = divs[1].Y - (divs[0].Y + divs[0].Height);
        gap.Should().BeApproximately(20, 1f);
    }

    [Fact]
    public void Paragraphs_DefaultMargins_Collapse()
    {
        var root = LayoutTestHelper.Layout(
            "<p>First paragraph</p><p>Second paragraph</p>",
            600, 800);

        var ps = root.FindAllByTag("p");
        ps.Should().HaveCountGreaterOrEqualTo(2);

        float gap = ps[1].Y - (ps[0].Y + ps[0].Height);
        gap.Should().BeApproximately(16, 1f,
            "the 1em bottom and top margins of adjacent paragraphs collapse to one 16px gap, not 32px");
    }

    [Fact]
    public void NoMargin_NoGap()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='height: 50px'></div>" +
            "<div style='height: 50px'></div>",
            600, 800);

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCountGreaterOrEqualTo(2);

        float gap = divs[1].Y - (divs[0].Y + divs[0].Height);
        gap.Should().BeApproximately(0, 1f);
    }

    [Fact]
    public void ThreeElements_AllMarginsCollapse()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='margin-bottom: 10px; height: 30px'></div>" +
            "<div style='margin-top: 20px; margin-bottom: 15px; height: 30px'></div>" +
            "<div style='margin-top: 25px; height: 30px'></div>",
            600, 800);

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCountGreaterOrEqualTo(3);

        float gap12 = divs[1].Y - (divs[0].Y + divs[0].Height);
        gap12.Should().BeApproximately(20, 1f, "max(10, 20)");

        float gap23 = divs[2].Y - (divs[1].Y + divs[1].Height);
        gap23.Should().BeApproximately(25, 1f, "max(15, 25)");
    }
}
