using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class GridAutoTrackTests
{
    private static LayoutBox Layout(string body) =>
        LayoutTestHelper.Layout("<html><body style='margin:0'>" + body + "</body></html>", 600, 800);

    [Fact]
    public void AutoColumnBesideFr_TakesItsContentWidthAndFrTakesTheRest()
    {
        var root = Layout("<div style='display:grid;grid-template-columns:auto 1fr;width:400px'>" +
                          "<div id='label' style='width:80px'>Label</div><div id='value'>Value</div></div>");

        var label = root.FindById("label")!;
        var value = root.FindById("value")!;
        label.Width.Should().BeApproximately(80, 0.5f);
        value.X.Should().BeApproximately(80, 0.5f);
        value.Width.Should().BeApproximately(320, 0.5f);
    }

    [Fact]
    public void AutoColumnBesideFr_WithTextContent_IsNotZeroWide()
    {
        var root = Layout("<div style='display:grid;grid-template-columns:auto 1fr;width:400px'>" +
                          "<div id='label'>Invoice number</div><div id='value'>INV-001</div></div>");

        root.FindById("label")!.Width.Should().BeGreaterThan(40);
        root.FindById("value")!.X.Should().BeGreaterThan(40);
    }

    [Fact]
    public void AutoColumnsWithLongText_StayInsideTheContainer()
    {
        const string longText = "lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore et dolore magna aliqua";
        var root = Layout("<div style='display:grid;grid-template-columns:auto auto;width:400px'>" +
                          "<div id='a'>" + longText + "</div><div id='b'>" + longText + "</div></div>");

        var b = root.FindById("b")!;
        (b.X + b.Width).Should().BeLessThanOrEqualTo(400.5f);
        root.FindById("a")!.Width.Should().BeLessThanOrEqualTo(200.5f);
    }

    [Fact]
    public void AutoColumnsOnly_ShareTheFreeSpaceOnTopOfTheirContent()
    {
        var root = Layout("<div style='display:grid;grid-template-columns:auto auto;width:400px'>" +
                          "<div id='a' style='width:100px'>A</div><div id='b' style='width:50px'>B</div></div>");

        root.FindById("b")!.X.Should().BeApproximately(225, 0.5f);
    }
}
