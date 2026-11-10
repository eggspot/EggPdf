using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class TierOneWireupTests
{
    [Fact]
    public void WritingMode_Vertical_StoredInStyle()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='writing-mode:vertical-rl; width:50px; height:200px'>Hello</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("writing-mode").Should().Be("vertical-rl");
    }

    [Fact]
    public void WritingMode_VerticalLr_StoredInStyle()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='writing-mode:vertical-lr'>Text</div>", 400, 600);
        var div = root.FindByTag("div");
        div!.Style.Get("writing-mode").Should().Be("vertical-lr");
    }

    [Fact]
    public void TextWrap_Balance_StoredInStyle()
    {
        var root = LayoutTestHelper.Layout(
            "<h2 style='text-wrap:balance; width:300px'>Heading text</h2>", 400, 600);
        var h2 = root.FindByTag("h2");
        h2.Should().NotBeNull();
        h2!.Style.Get("text-wrap").Should().Be("balance");
    }

    [Fact]
    public void TextWrap_Balance_FirstLineNotFullWidth()
    {
        const string tenWords = "word1 word2 word3 word4 word5 word6 word7 word8 word9 word10";
        var htmlBalanced = LayoutTestHelper.Layout(
            "<p style='text-wrap:balance; width:200px; font-size:12px'>" + tenWords + "</p>", 400, 600);
        var htmlNormal = LayoutTestHelper.Layout(
            "<p style='width:200px; font-size:12px'>" + tenWords + "</p>", 400, 600);

        var balancedBoxes = htmlBalanced.FindByTag("p")!.Children.FindAll(b => !string.IsNullOrEmpty(b.Text));
        var normalBoxes = htmlNormal.FindByTag("p")!.Children.FindAll(b => !string.IsNullOrEmpty(b.Text));

        normalBoxes.Should().HaveCountGreaterOrEqualTo(2, "ten words at 12px do not fit on one 200px line");
        balancedBoxes.Should().HaveCountGreaterOrEqualTo(2, "ten words at 12px do not fit on one 200px line");
        balancedBoxes[0].ContentWidth.Should().BeLessThan(normalBoxes[0].ContentWidth,
            "text-wrap:balance shortens the first line to even it out with the next");
    }

    [Fact]
    public void Caption_RendersAsBox()
    {
        var root = LayoutTestHelper.Layout(
            "<table><caption>Title</caption><tr><td>Cell</td></tr></table>", 400, 600);
        var caption = root.FindByTag("caption");
        caption.Should().NotBeNull("caption element should produce a layout box");
        caption!.Style.Get("display").Should().Be("table-caption");
    }

    [Fact]
    public void Caption_Default_AboveTable()
    {
        var root = LayoutTestHelper.Layout(
            "<table><caption>My Caption</caption>" +
            "<tr><td style='height:40px'>Cell</td></tr></table>", 400, 600);

        var caption = root.FindByTag("caption");
        var firstRow = root.FindByTag("tr");
        caption.Should().NotBeNull();
        firstRow.Should().NotBeNull();

        caption!.Y.Should().BeLessOrEqualTo(firstRow!.Y,
            "caption with default caption-side:top should appear above table rows");
    }

    [Fact]
    public void Caption_Bottom_BelowTable()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='caption-side:bottom'>" +
            "<caption>Footer Caption</caption>" +
            "<tr><td style='height:40px'>Cell</td></tr></table>", 400, 600);

        var caption = root.FindByTag("caption");
        var firstRow = root.FindByTag("tr");
        caption.Should().NotBeNull();
        firstRow.Should().NotBeNull();

        caption!.Y.Should().BeGreaterOrEqualTo(firstRow!.Y + firstRow.Height - 1f,
            "caption-side:bottom should place caption below all table rows");
    }
}
