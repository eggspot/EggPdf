using System.Text;
using EggPdf.Css.Cascade;
using EggPdf.Html;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace EggPdf.Tests.Unit.EndToEnd;

public class TableGapDiagnosticTest
{
    private readonly ITestOutputHelper _output;

    public TableGapDiagnosticTest(ITestOutputHelper output)
    {
        _output = output;
    }

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

        // Use the exact same pipeline as HtmlToPdf.RenderInternal
        var document = HtmlParser.Parse(html);
        var stylesheets = HtmlToPdf.ExtractStyleSheets(document);
        var pageSettings = PageRuleResolver.Resolve(stylesheets);
        var cascade = new CascadeResolver(stylesheets, "print");
        var root = BlockLayout.LayoutDocument(document, pageSettings.ContentWidthPx, pageSettings.ContentHeightPx, cascade);

        _output.WriteLine($"Page: {pageSettings.PageWidthPx}x{pageSettings.PageHeightPx}, Margins: L={pageSettings.MarginLeft} R={pageSettings.MarginRight}");
        _output.WriteLine($"Content area: {pageSettings.ContentWidthPx}x{pageSettings.ContentHeightPx}");

        // Dump full hierarchy
        DumpBox(root, 0);

        // Check table
        var table = root.FindByTag("table");
        table.Should().NotBeNull("table should exist");
        _output.WriteLine($"\nTable: X={table!.X:F2}, W={table.Width:F2}, CW={table.ContentWidth:F2}");
        _output.WriteLine($"  border-collapse: {table.Style.Get("border-collapse")}");
        _output.WriteLine($"  border-spacing: {table.Style.Get("border-spacing")}");

        // Check thead
        var thead = root.FindByTag("thead");
        if (thead != null)
        {
            _output.WriteLine($"\nThead: X={thead.X:F2}, W={thead.Width:F2}, CW={thead.ContentWidth:F2}");
            _output.WriteLine($"  PL={thead.PaddingLeft}, PR={thead.PaddingRight}, ML={thead.MarginLeft}, MR={thead.MarginRight}");
            _output.WriteLine($"  border-collapse: {thead.Style.Get("border-collapse")}");
        }

        // Check row
        var headerRow = root.FindByTag("tr");
        headerRow.Should().NotBeNull("header row should exist");
        _output.WriteLine($"\nHeader Row: X={headerRow!.X:F2}, W={headerRow.Width:F2}, CW={headerRow.ContentWidth:F2}");
        _output.WriteLine($"  PL={headerRow.PaddingLeft}, PR={headerRow.PaddingRight}, ML={headerRow.MarginLeft}, MR={headerRow.MarginRight}");
        _output.WriteLine($"  border-collapse: {headerRow.Style.Get("border-collapse")}");

        // Check header cells
        var headerCells = root.FindAllByTag("th");
        headerCells.Should().HaveCount(4, "should have 4 header cells");

        _output.WriteLine("\nHeader cells:");
        float totalCellWidth = 0;
        for (int i = 0; i < headerCells.Count; i++)
        {
            var cell = headerCells[i];
            _output.WriteLine($"  Cell {i} '{cell.Element?.TagName}': X={cell.X:F2}, W={cell.Width:F2}, CW={cell.ContentWidth:F2}, " +
                             $"PL={cell.PaddingLeft}, PR={cell.PaddingRight}, ML={cell.MarginLeft}, MR={cell.MarginRight}");
            _output.WriteLine($"    border-left: {cell.Style.Get("border-left-width")} | border-right: {cell.Style.Get("border-right-width")}");
            _output.WriteLine($"    RightEdge={cell.X + cell.Width:F2}");
            totalCellWidth += cell.Width;
        }
        _output.WriteLine($"\nTotal cell width: {totalCellWidth:F2}, Row ContentWidth: {headerRow.ContentWidth:F2}, Difference: {headerRow.ContentWidth - totalCellWidth:F2}");

        // Check for gaps
        for (int i = 1; i < headerCells.Count; i++)
        {
            var gap = headerCells[i].X - (headerCells[i - 1].X + headerCells[i - 1].Width);
            _output.WriteLine($"Gap between cell {i - 1} and {i}: {gap:F4}");
            gap.Should().BeApproximately(0, 0.5f, $"no gap between cell {i - 1} and {i}");
        }

        // Verify cells fill the row
        var firstX = headerCells[0].X;
        var lastRightEdge = headerCells[3].X + headerCells[3].Width;
        var coverage = lastRightEdge - firstX;
        _output.WriteLine($"\nCell coverage: {coverage:F2}, Row CW: {headerRow.ContentWidth:F2}");
        coverage.Should().BeApproximately(headerRow.ContentWidth, 1f, "cells should fill the row");
    }

    [Fact]
    public async Task AnonymousTextBox_NoPaintBackgroundOrBorder()
    {
        // Regression test: anonymous text boxes inside elements should NOT paint
        // their own backgrounds or borders (those belong to the parent element only).
        var html = @"<div style='background: red; border: 2px solid black; padding: 10px; width: 200px'>Hello</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        // Count filled rectangles: should be exactly 2 (white page canvas + the div's background)
        // Before fix: was 3 (page canvas + div bg + anonymous text box bg)
        int fillCount = System.Text.RegularExpressions.Regex.Matches(text, @"re f").Count;
        fillCount.Should().Be(2, "only the element box should paint its background, not anonymous text boxes");

        // Count stroke rectangles: should be exactly 1 (the div's border)
        int strokeCount = System.Text.RegularExpressions.Regex.Matches(text, @"re S").Count;
        strokeCount.Should().BeLessOrEqualTo(1, "only the element box should paint borders, not anonymous text boxes");
    }

    [Fact]
    public async Task TableBorderCollapse_NoDoubleInternalBorders()
    {
        // Regression test: with border-collapse: collapse, interior borders should not
        // be drawn twice (once per adjacent cell).
        var html = @"<html><head><style>
            table { width: 100%; border-collapse: collapse; }
            th, td { border: 1px solid #ddd; padding: 10px; }
            th { background: #6c5ce7; }
        </style></head><body>
        <table><tr><th>A</th><th>B</th></tr></table>
        </body></html>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        // With 2 header cells and border-collapse, we should have exactly 3 fills (white page canvas + 2 th backgrounds)
        int fillCount = System.Text.RegularExpressions.Regex.Matches(text, @"re f").Count;
        fillCount.Should().Be(3, "each th should paint exactly one background (no phantom text box backgrounds)");
    }

    private void DumpBox(LayoutBox box, int depth)
    {
        if (box.Element != null)
        {
            var indent = new string(' ', depth * 2);
            var tag = box.Element.TagName;
            _output.WriteLine($"{indent}<{tag}> X={box.X:F1} Y={box.Y:F1} W={box.Width:F1} H={box.Height:F1} CW={box.ContentWidth:F1} " +
                            $"PL={box.PaddingLeft:F1} PR={box.PaddingRight:F1} ML={box.MarginLeft:F1} MR={box.MarginRight:F1} " +
                            $"display={box.Style.Display}");
        }
        foreach (var child in box.Children)
        {
            if (child is LayoutBox lb)
                DumpBox(lb, depth + 1);
        }
    }
}
