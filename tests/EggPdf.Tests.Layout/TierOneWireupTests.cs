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
        const string sixWords = "word1 word2 word3 word4 word5 word6";
        var htmlBalanced = LayoutTestHelper.Layout(
            "<p style='text-wrap:balance; width:200px; font-size:12px'>" + sixWords + "</p>", 400, 600);
        var htmlNormal = LayoutTestHelper.Layout(
            "<p style='width:200px; font-size:12px'>" + sixWords + "</p>", 400, 600);

        var balancedBoxes = htmlBalanced.FindByTag("p")!.Children.FindAll(b => !string.IsNullOrEmpty(b.Text));
        var normalBoxes = htmlNormal.FindByTag("p")!.Children.FindAll(b => !string.IsNullOrEmpty(b.Text));

        normalBoxes.Should().HaveCount(2, "five of the six words fill a 200px line at 12px, and the sixth wraps alone");
        balancedBoxes.Should().HaveCount(2, "balancing evens the lines out without adding one");
        balancedBoxes[0].ContentWidth.Should().BeLessThan(normalBoxes[0].ContentWidth,
            "text-wrap:balance shortens the first line to even it out with the next");
    }

    [Fact]
    public void TextWrap_Balance_PlainText_KeepsTheGreedyLineCount()
    {
        const string heading = "A moderately long heading that should be balanced";
        var balanced = LayoutTestHelper.Layout(
            "<h2 style='text-wrap:balance; width:300px'>" + heading + "</h2>", 400, 600);
        var greedy = LayoutTestHelper.Layout(
            "<h2 style='width:300px'>" + heading + "</h2>", 400, 600);

        var balancedLines = balanced.FindByTag("h2")!.Children.FindAll(b => !string.IsNullOrEmpty(b.Text));
        var greedyLines = greedy.FindByTag("h2")!.Children.FindAll(b => !string.IsNullOrEmpty(b.Text));

        greedyLines.Should().HaveCount(3);
        balancedLines.Should().HaveCount(3,
            "balancing narrows the lines only as far as the text still fits the greedy line count, never adding a line");
        balancedLines[2].ContentWidth.Should().BeGreaterThan(greedyLines[2].ContentWidth,
            "the greedy short last line ('balanced') takes words from the line above");
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
