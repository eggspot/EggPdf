using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>Tests for Tier 1 "wire-up" features: infrastructure exists, just needs wiring.</summary>
public class TierOneWireupTests
{
    // ===== writing-mode =====

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

    // ===== text-wrap: balance =====

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
        // With text-wrap:balance on wrapping text, first line should be shorter than
        // container width (balanced down to match second line)
        var htmlBalanced = LayoutTestHelper.Layout(
            "<p style='text-wrap:balance; width:200px; font-size:12px'>" +
            "word1 word2 word3 word4 word5 word6</p>", 400, 600);
        var htmlNormal = LayoutTestHelper.Layout(
            "<p style='width:200px; font-size:12px'>" +
            "word1 word2 word3 word4 word5 word6</p>", 400, 600);

        var pBalanced = htmlBalanced.FindByTag("p");
        var pNormal = htmlNormal.FindByTag("p");

        var balancedBoxes = pBalanced!.Children.FindAll(b => !string.IsNullOrEmpty(b.Text));
        var normalBoxes = pNormal!.Children.FindAll(b => !string.IsNullOrEmpty(b.Text));

        // Only meaningful if text actually wraps in both cases
        if (balancedBoxes.Count >= 2 && normalBoxes.Count >= 2)
        {
            float normalFirst = normalBoxes[0].ContentWidth;
            float balancedFirst = balancedBoxes[0].ContentWidth;
            balancedFirst.Should().BeLessThan(normalFirst,
                "text-wrap:balance should reduce first-line width to balance with subsequent lines");
        }
    }

    // ===== caption / table-caption =====

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
        // With caption-side:top (default), caption should appear above the table rows
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
        // With caption-side:bottom, caption should appear after the table rows
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
