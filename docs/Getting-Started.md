# Getting Started

## Installation

```bash
dotnet add package EggPdf
```

## Your First PDF

```csharp
using EggPdf;

// One-liner
byte[] pdf = await HtmlToPdf.RenderAsync("<h1>Hello World</h1>");
File.WriteAllBytes("hello.pdf", pdf);
```

## With Options

```csharp
var converter = new HtmlToPdfConverter(new PdfOptions
{
    PageSize = PageSize.A4,
    Margins = new PageMargins(20, 15, 20, 15, Unit.Mm),
    DefaultFont = "Arial",
    Title = "My Document"
});

string html = @"
<html>
<head>
    <style>
        body { font-family: Arial, sans-serif; }
        h1 { color: #333; }
        table { width: 100%; border-collapse: collapse; }
        td, th { border: 1px solid #ddd; padding: 8px; }
    </style>
</head>
<body>
    <h1>Invoice #1234</h1>
    <table>
        <thead><tr><th>Item</th><th>Amount</th></tr></thead>
        <tbody>
            <tr><td>Widget</td><td>$10.00</td></tr>
            <tr><td>Gadget</td><td>$25.00</td></tr>
        </tbody>
    </table>
</body>
</html>";

byte[] pdf = await converter.RenderAsync(html);
```

## Output Modes

```csharp
// To byte[]
byte[] pdf = await converter.RenderAsync(html);

// To file
await converter.RenderToFileAsync(html, "output.pdf");

// To stream (HTTP response, cloud storage, etc.)
await converter.RenderAsync(html, outputStream, cancellationToken);

// To PNG image (thumbnail, preview)
byte[] png = await converter.RenderToImageAsync(html, new ImageOptions { Dpi = 150 });
```

## ASP.NET Core

```bash
dotnet add package EggPdf.AspNetCore
```

```csharp
// Startup
services.AddEggPdf(options => {
    options.PageSize = PageSize.A4;
});

// Controller
[HttpGet("report/pdf")]
public async Task<IActionResult> GetReport()
{
    string html = BuildReportHtml();
    return new PdfResult(html) { FileName = "report.pdf" };
}
```

## Next Steps

- [Configuration](Configuration) -- All PdfOptions explained
- [Page Layout](Page-Layout) -- Page size, margins, @page rules
- [Tables](Tables) -- Multi-page tables with repeating headers
