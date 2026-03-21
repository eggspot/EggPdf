using System;
using EggPdf.Html;
using EggPdf.Layout;
using Xunit;
using Xunit.Abstractions;

namespace EggPdf.Tests.Unit.EndToEnd;

public class LayoutDebugTest
{
    private readonly ITestOutputHelper _output;
    public LayoutDebugTest(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void DebugInvoiceLayout()
    {
        var html = @"<h1>Invoice #2024-001</h1><p>Date: 2024-01-15</p>
            <table><thead><tr><th>Item</th><th>Price</th></tr></thead>
            <tbody><tr><td>Web Dev</td><td>$5000</td></tr></tbody></table>
            <p><strong>Total: $7,000</strong></p>";

        var root = LayoutTestHelper.Layout(html, 595, 842);
        PrintBox(root, 0);
    }

    private void PrintBox(LayoutBox box, int depth)
    {
        var indent = new string(' ', depth * 2);
        var tag = box.Element?.TagName ?? "(anon)";
        var text = box.Text ?? "";
        if (text.Length > 40) text = text.Substring(0, 40) + "...";
        _output.WriteLine($"{indent}{tag} x={box.X:F1} y={box.Y:F1} w={box.Width:F1} h={box.Height:F1} '{text}'");
        foreach (var child in box.Children)
            PrintBox(child, depth + 1);
    }
}
