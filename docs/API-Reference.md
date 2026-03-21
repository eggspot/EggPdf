# API Reference

## Core

### HtmlToPdf (static convenience)

```csharp
// Quick render
byte[] pdf = await HtmlToPdf.RenderAsync(html);
await HtmlToPdf.RenderToFileAsync(html, "output.pdf");
```

### IHtmlToPdfConverter

```csharp
public interface IHtmlToPdfConverter
{
    // String input
    Task<byte[]> RenderAsync(string html, CancellationToken ct = default);
    Task RenderAsync(string html, Stream output, CancellationToken ct = default);
    Task RenderToFileAsync(string html, string filePath, CancellationToken ct = default);

    // Stream input (large HTML)
    Task<byte[]> RenderAsync(Stream htmlInput, CancellationToken ct = default);

    // Image output
    Task<byte[]> RenderToImageAsync(string html, ImageOptions options, CancellationToken ct = default);

    // Page range
    Task<byte[]> RenderPagesAsync(string html, PageRange pages, CancellationToken ct = default);

    // With warnings and diagnostics
    Task<RenderResult> RenderWithResultAsync(string html, CancellationToken ct = default);

    // Sync variants
    byte[] Render(string html);
    void RenderToFile(string html, string filePath);

    // Warm-up
    Task WarmUpAsync(CancellationToken ct = default);
}
```

### PdfOptions

See [Configuration](Configuration) for all options.

### RenderResult

```csharp
public class RenderResult
{
    public byte[]? PdfBytes { get; }
    public int PageCount { get; }
    public IReadOnlyList<RenderWarning> Warnings { get; }
    public RenderTimings Timings { get; }
}
```

## Razor (EggPdf.Razor)

### IRazorToPdfConverter

```csharp
public interface IRazorToPdfConverter
{
    Task<byte[]> RenderViewAsync<T>(string viewName, T model, CancellationToken ct = default);
    Task RenderViewAsync<T>(string viewName, T model, Stream output, CancellationToken ct = default);
    Task<byte[]> RenderStringAsync<T>(string template, T model, CancellationToken ct = default);
}
```

## ASP.NET Core (EggPdf.AspNetCore)

### PdfResult

```csharp
return new PdfResult(html) { FileName = "report.pdf" };
return new RazorPdfResult("ViewName", model) { FileName = "invoice.pdf" };
```

### DI Registration

```csharp
services.AddEggPdf(options => { ... });
services.AddEggPdfRazor(options => { ... });
```

## PDF Utilities

### PdfMerger

```csharp
var merger = new PdfMerger();
merger.Add(pdf1);
merger.Add(pdf2, label: new PageLabel(style: Roman));
byte[] merged = merger.Build();
```

### PdfSigner

```csharp
byte[] signed = PdfSigner.Sign(pdfBytes, certificate, signOptions);
```
