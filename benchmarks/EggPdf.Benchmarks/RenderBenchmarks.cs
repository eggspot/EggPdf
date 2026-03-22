using BenchmarkDotNet.Attributes;

namespace EggPdf.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class RenderBenchmarks
{
    private string _simpleHtml = null!;
    private string _invoiceHtml = null!;
    private string _largeTableHtml = null!;

    [GlobalSetup]
    public void Setup()
    {
        _simpleHtml = "<h1>Hello World</h1><p>Simple paragraph.</p>";

        _invoiceHtml = @"<html><head><style>
            body { font-family: Arial; }
            table { width: 100%; border-collapse: collapse; }
            th, td { border: 1px solid #ddd; padding: 8px; }
            th { background: #6c5ce7; color: white; }
        </style></head><body>
            <h1>Invoice #001</h1>
            <p>Date: 2024-01-15 | Customer: Acme Corp</p>
            <table>
                <thead><tr><th>Item</th><th>Qty</th><th>Price</th><th>Total</th></tr></thead>
                <tbody>
                    <tr><td>Widget A</td><td>10</td><td>$5</td><td>$50</td></tr>
                    <tr><td>Widget B</td><td>5</td><td>$12</td><td>$60</td></tr>
                    <tr><td>Service</td><td>1</td><td>$25</td><td>$25</td></tr>
                </tbody>
            </table>
            <p><strong>Total: $135</strong></p>
        </body></html>";

        var sb = new System.Text.StringBuilder();
        sb.Append("<table><thead><tr><th>ID</th><th>Name</th><th>Value</th></tr></thead><tbody>");
        for (int i = 0; i < 100; i++)
            sb.Append($"<tr><td>{i}</td><td>Item {i}</td><td>${i * 10}</td></tr>");
        sb.Append("</tbody></table>");
        _largeTableHtml = sb.ToString();
    }

    [Benchmark(Description = "Simple page (h1 + p)")]
    public byte[] SimplePage() => HtmlToPdf.Render(_simpleHtml);

    [Benchmark(Description = "Invoice (table + styles)")]
    public byte[] InvoicePage() => HtmlToPdf.Render(_invoiceHtml);

    [Benchmark(Description = "Large table (100 rows)")]
    public byte[] LargeTable() => HtmlToPdf.Render(_largeTableHtml);
}
