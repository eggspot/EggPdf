using System.Linq;
using System.Text;
using EggPdf.Layout;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class PageAwareLayoutTests
{
    private static string? FirstText(LayoutBox column) =>
        column.FindAll(b => !string.IsNullOrEmpty(b.Text)).First().Text?.Trim();

    [Fact]
    public void LongMulticol_FillsColumnsAPageAtATime()
    {
        var body = new StringBuilder("<div id='m' style='column-count:2;column-gap:0'>");
        for (int i = 0; i < 120; i++) body.Append("<p style='margin:0;height:20px'>P").Append(i).Append("</p>");
        body.Append("</div>");
        var root = LayoutTestHelper.Layout("<html><body style='margin:0'>" + body + "</body></html>", 600, 800);

        var columns = root.FindById("m")!.Children.Where(c => c.Element == null && c.Text == null).ToList();
        columns.Should().HaveCountGreaterThan(2, "2400px of content over 800px pages needs more than one set of columns");
        columns.Should().OnlyContain(c => c.Height <= 800.5f);
        FirstText(columns[0]).Should().Be("P0");
        FirstText(columns[1]).Should().Be("P40", "the second column continues after a page-tall first column");
    }

    [Fact]
    public void ShortMulticol_IsStillBalanced()
    {
        var body = new StringBuilder("<div id='m' style='column-count:2;column-gap:0'>");
        for (int i = 0; i < 10; i++) body.Append("<p style='margin:0;height:20px'>P</p>");
        body.Append("</div>");
        var root = LayoutTestHelper.Layout("<html><body style='margin:0'>" + body + "</body></html>", 600, 800);

        var columns = root.FindById("m")!.Children.Where(c => c.Element == null && c.Text == null).ToList();
        columns.Should().HaveCount(2);
        columns[0].Height.Should().BeApproximately(100, 0.5f);
    }

    [Fact]
    public void LineHeightNormal_UsesTheFontsAscentDescentAndLineGap()
    {
        var font = new FontData { UnitsPerEm = 1000, Ascent = 1000, Descent = -300, LineGap = 200, AdvanceWidths = new ushort[] { 500 } };
        TextMeasurer.FontDataProvider = (family, weight, style) => family == "Tall" ? font : null;
        try
        {
            var root = LayoutTestHelper.Layout("<html><body style='margin:0'><p id='p' style='margin:0;font-family:Tall;font-size:20px'>Text</p></body></html>", 600, 800);

            root.FindById("p")!.Height.Should().BeApproximately(30, 0.5f, "(1000 + 300 + 200) / 1000 × 20px");
        }
        finally
        {
            TextMeasurer.FontDataProvider = null;
        }
    }

    [Fact]
    public void GridAutoColumnsThatOverflow_NeverShrinkBelowTheirLongestWord()
    {
        var root = LayoutTestHelper.Layout("<html><body style='margin:0'><div style='display:grid;grid-template-columns:auto auto;width:300px'>" +
            "<div id='a'>Supercalifragilisticexpialidocious</div>" +
            "<div>lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore</div></div></body></html>", 600, 800);

        float word = TextMeasurer.MeasureWidth("Supercalifragilisticexpialidocious", 16, null);
        root.FindById("a")!.Width.Should().BeGreaterThanOrEqualTo(word - 0.5f);
    }
}
