using System.Text;
using System.Text.RegularExpressions;
using EggPdf.Css.Cascade;
using EggPdf.Html;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class TableGapDiagnosticTest
{
    [Fact]
    public void Invoice_TableCells_NoGaps()
    {
        var html = @"<html>
<head>
  <style>
    body { font-family: Arial, sans-serif; margin: 40px; }
    h1 { color: #2d3436; border-bottom: 2px solid #6c5ce7; padding-bottom: 8px; }
    table { width: 100%; border-collapse: collapse; margin: 20px 0; }
    th, td { border: 1px solid #ddd; padding: 10px; text-align: left; }
    th { background: #6c5ce7; color: white; }
    .total { font-size: 18px; font-weight: bold; margin-top: 10px; }
  </style>
</head>
<body>
  <h1>Invoice #2024-001</h1>
  <p>Date: 2024-01-15 | Customer: Acme Corporation</p>
  <table>
    <thead><tr><th>Item</th><th>Qty</th><th>Price</th><th>Total</th></tr></thead>
    <tbody>
      <tr><td>Web Development</td><td>40h</td><td>$150</td><td>$6,000</td></tr>
    </tbody>
  </table>
</body></html>";

        var document = HtmlParser.Parse(html);
        var stylesheets = HtmlToPdf.ExtractStyleSheets(document);
        var pageSettings = PageRuleResolver.Resolve(stylesheets);
        var cascade = new CascadeResolver(stylesheets, "print");
        var root = BlockLayout.LayoutDocument(document, pageSettings.ContentWidthPx, pageSettings.ContentHeightPx, cascade);

        root.FindByTag("table").Should().NotBeNull("table should exist");
        var headerRow = root.FindByTag("tr");
        headerRow.Should().NotBeNull("header row should exist");

        var headerCells = root.FindAllByTag("th");
        headerCells.Should().HaveCount(4, "should have 4 header cells");

        for (int i = 1; i < headerCells.Count; i++)
        {
            var gap = headerCells[i].X - (headerCells[i - 1].X + headerCells[i - 1].Width);
            gap.Should().BeApproximately(0, 0.5f, $"no gap between cell {i - 1} and {i}");
        }

        var coverage = headerCells[3].X + headerCells[3].Width - headerCells[0].X;
        coverage.Should().BeApproximately(headerRow!.ContentWidth, 1f, "cells should fill the row");
    }

    [Fact]
    public async Task AnonymousTextBox_NoPaintBackgroundOrBorder()
    {
        var html = @"<div style='background: red; border: 2px solid black; padding: 10px; width: 200px'>Hello</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        Regex.Matches(text, @"re f").Count.Should().Be(2,
            "only the white page canvas and the div paint backgrounds, not the anonymous text box inside the div");
        Regex.Matches(text, @"re S").Count.Should().BeLessOrEqualTo(1,
            "only the element box should paint borders, not anonymous text boxes");
    }

    [Fact]
    public async Task TableBorderCollapse_NoDoubleInternalBorders()
    {
        var html = @"<html><head><style>
            table { width: 100%; border-collapse: collapse; }
            th, td { border: 1px solid #ddd; padding: 10px; }
            th { background: #6c5ce7; }
        </style></head><body>
        <table><tr><th>A</th><th>B</th></tr></table>
        </body></html>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        Regex.Matches(text, @"re f").Count.Should().Be(3,
            "the white page canvas and one background per th; anonymous text boxes add no phantom fills");
    }
}
