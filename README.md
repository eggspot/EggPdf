# EggPdf

[![CI](https://github.com/eggspot/EggPdf/actions/workflows/ci.yml/badge.svg)](https://github.com/eggspot/EggPdf/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/EggPdf.svg)](https://www.nuget.org/packages/EggPdf)
[![NuGet Downloads](https://img.shields.io/nuget/dt/EggPdf.svg)](https://www.nuget.org/packages/EggPdf)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**Pure C# HTML/CSS to PDF rendering engine.** Zero dependencies. Chrome-quality output.

Write normal HTML and CSS. Get a perfect PDF. No WebKit, no Chromium, no native binaries.

## Why EggPdf?

| Feature | EggPdf | SelectPdf | wkhtmltopdf | Puppeteer |
|---------|--------|-----------|-------------|-----------|
| Pure C# | Yes | No | No | No |
| Dependencies | **Zero** | ~50MB WebKit | ~40MB Qt | ~300MB Chrome |
| .NET Framework | 4.6.2+ | Limited | N/A | N/A |
| .NET Core/5+ | All | Some | N/A | Yes |
| CSS Flexbox | Yes | Yes | No | Yes |
| CSS Grid | Yes | Yes | No | Yes |
| SVG Support | Yes (vector) | Yes | Partial | Yes |
| PDF/A | Yes | No | No | No |
| PDF/UA | Yes | No | No | No |
| Tagged PDF | Yes | No | No | No |
| Digital Signatures | Yes | No | No | No |
| License | MIT | Commercial | LGPL | Apache 2 |

## Quick Start

```bash
dotnet add package EggPdf
```

```csharp
// One-liner
byte[] pdf = await EggPdf.HtmlToPdf.RenderAsync("<h1>Hello World</h1>");
File.WriteAllBytes("output.pdf", pdf);
```

## Standard Usage

```csharp
var converter = new HtmlToPdfConverter(new PdfOptions
{
    PageSize = PageSize.A4,
    Orientation = PageOrientation.Portrait,
    Margins = new PageMargins(top: 20, right: 15, bottom: 20, left: 15, unit: Unit.Mm),
    DefaultFont = "Arial",
    Title = "My Document"
});

// To byte[]
byte[] pdf = await converter.RenderAsync(htmlString);

// To file
await converter.RenderToFileAsync(htmlString, "report.pdf");

// To HTTP response (streaming, no buffering)
await converter.RenderAsync(htmlString, Response.Body, HttpContext.RequestAborted);
```

## ASP.NET Core Integration

```bash
dotnet add package EggPdf.AspNetCore
```

```csharp
// DI Registration
services.AddEggPdf(options =>
{
    options.PageSize = PageSize.A4;
    options.DefaultFont = "Arial";
});

// Controller
[HttpGet("invoice/{id}/pdf")]
public async Task<IActionResult> GetInvoice(int id)
{
    var model = await _invoiceService.GetAsync(id);
    string html = await _viewRenderer.RenderAsync("Invoice", model);
    return new PdfResult(html) { FileName = $"invoice-{id}.pdf" };
}
```

## Razor Templates

```bash
dotnet add package EggPdf.Razor
```

```csharp
services.AddEggPdfRazor();

// Render .cshtml template directly to PDF
public class InvoiceService(IRazorToPdfConverter pdf)
{
    public async Task<byte[]> Generate(InvoiceModel model)
        => await pdf.RenderViewAsync("Invoice", model);
}
```

## Features

### HTML & CSS
- Full HTML5 parsing (WHATWG spec-compliant)
- CSS 2.1 complete + CSS3 (Flexbox with auto margins & baseline alignment, Grid, Multi-column)
- CSS Custom Properties (`var()`)
- `@media print` support
- Webfonts: remote `<link>` stylesheets (Google Fonts) and `@font-face` over http(s), data: URIs, or files
- SVG rendering (vector output, not rasterized)
- All image formats (JPEG, PNG incl. 1-bit QR codes, GIF, WebP, SVG, Base64)
- Cloudflare email obfuscation (`data-cfemail`) decoded automatically

### PDF
- PDF 1.4 / 1.5 / 1.7 / 2.0
- Clickable hyperlinks and internal links
- Auto-generated bookmarks from headings
- Table of contents with page numbers
- Running headers/footers
- Page numbers (Page X of Y)
- Repeating table headers across pages
- Mixed page orientations (portrait + landscape)
- Watermarks

### Typography
- TrueType/OpenType font embedding with subsetting
- Weight-accurate faces: `font-weight: 300–900` each select their own variant
- Font fallback chain + per-codepoint symbol-font fallback (⚠ ✔ …)
- Full Unicode: Vietnamese and extended Latin out of the box
- Browser-parity metrics: text measured with the real font, baselines like Chrome
- Automatic hyphenation

### Business
- Digital signatures — one-call X.509 signing (`PdfSigner.Sign(pdf, cert)`, detached CMS/PKCS#7) or external-CMS two-step flow for HSMs
- Password protection with permission flags — view-only PDFs that block editing/copying (RC4 40/128-bit)
- AcroForm fields (fillable forms from HTML form elements)
- PDF merging
- QR codes and barcodes

### Performance
- Streaming output (constant memory for large documents)
- Font caching across renders
- Thread-safe converter (one instance per app)
- Streaming table layout for 10,000+ row tables

<!-- BENCHMARK_START -->
| Scenario | Mean | Memory |
|----------|------|--------|
| Simple page (h1 + p) | **13 µs** | 20 KB |
| Invoice (table + styles) | **86 µs** | 85 KB |
| Large table (100 rows) | **1.2 ms** | 939 KB |

*Benchmarks run on every PR (results posted as comment) and on every merge to `main` (artifacts uploaded). Targets: simple < 50ms, invoice < 100ms, large table < 5s.*
<!-- BENCHMARK_END -->

## Target Frameworks

| Target | Coverage |
|--------|----------|
| `netstandard2.0` | .NET Framework 4.6.2+, .NET Core 2.0+, Mono, Xamarin, Unity |
| `netstandard2.1` | .NET Core 3.0+ |
| `net6.0` | .NET 6+ |
| `net8.0` | .NET 8+ |
| `net9.0` | .NET 9+ |
| `net10.0` | .NET 10+ |

## Use EggPdf Your Way

### NuGet (for .NET developers)

| Package | Description | Dependencies |
|---------|-------------|--------------|
| [EggPdf](https://www.nuget.org/packages/EggPdf) | Core library | None |
| [EggPdf.Razor](https://www.nuget.org/packages/EggPdf.Razor) | Razor template integration | ASP.NET Core |
| [EggPdf.AspNetCore](https://www.nuget.org/packages/EggPdf.AspNetCore) | ASP.NET Core middleware | ASP.NET Core |

### Docker (for any language / DevOps)

```bash
# REST API service (with Web UI)
docker run -p 8080:8080 eggspot/eggpdf:latest
# Open http://localhost:8080 for Web UI, or call REST API from any language

# CLI (convert files)
docker run -v $(pwd):/work eggspot/eggpdf:latest eggpdf /work/input.html -o /work/output.pdf
```

### CLI Binary (standalone, no .NET needed)

Download a single executable for your platform -- no installation required:

| Platform | Download |
|----------|----------|
| Windows x64 | [eggpdf-win-x64.exe](https://github.com/eggspot/EggPdf/releases/latest) |
| Windows ARM64 | [eggpdf-win-arm64.exe](https://github.com/eggspot/EggPdf/releases/latest) |
| Linux x64 | [eggpdf-linux-x64](https://github.com/eggspot/EggPdf/releases/latest) |
| Linux ARM64 | [eggpdf-linux-arm64](https://github.com/eggspot/EggPdf/releases/latest) |
| macOS x64 (Intel) | [eggpdf-osx-x64](https://github.com/eggspot/EggPdf/releases/latest) |
| macOS ARM64 (Apple Silicon) | [eggpdf-osx-arm64](https://github.com/eggspot/EggPdf/releases/latest) |

```bash
./eggpdf input.html -o output.pdf
./eggpdf input.html -o output.png --format png
./eggpdf input.html --watch    # live reload
./eggpdf --serve               # start REST API server
```

### Web UI (for anyone)

Open `http://localhost:8080` after starting the Docker service. Paste HTML, get PDF. No coding required.

## Documentation

See the [Wiki](https://github.com/eggspot/EggPdf/wiki) for full documentation:

- [Getting Started](https://github.com/eggspot/EggPdf/wiki/Getting-Started)
- [Configuration](https://github.com/eggspot/EggPdf/wiki/Configuration)
- [Page Layout & CSS](https://github.com/eggspot/EggPdf/wiki/Page-Layout)
- [Headers, Footers & Page Numbers](https://github.com/eggspot/EggPdf/wiki/Headers-Footers)
- [Images & SVG](https://github.com/eggspot/EggPdf/wiki/Images-SVG)
- [Tables](https://github.com/eggspot/EggPdf/wiki/Tables)
- [Fonts & Typography](https://github.com/eggspot/EggPdf/wiki/Fonts-Typography)
- [PDF Features](https://github.com/eggspot/EggPdf/wiki/PDF-Features)
- [Performance](https://github.com/eggspot/EggPdf/wiki/Performance)
- [API Reference](https://github.com/eggspot/EggPdf/wiki/API-Reference)

## Contributing

Contributions are welcome! We follow strict TDD — **write the test before the code**:

1. Fork the repository
2. Create a feature branch: `git checkout -b feat/my-feature`
3. **Write the failing test first** — run it, confirm it fails
4. Write minimal code to make the test pass
5. **Run the test** — if it fails, fix code and run again; repeat until it passes
6. **Run ALL tests** — fix any regressions and repeat until the full suite passes
7. **Check performance** if touching hot paths
8. Commit with conventional prefixes: `feat:`, `fix:`, `perf:`, `test:`
9. Push and create a PR

See [CLAUDE.md](CLAUDE.md) for detailed development guidelines.

## Sponsoring

EggPdf is free and open source. If you find it useful, please consider sponsoring:

[![Sponsor](https://img.shields.io/badge/Sponsor-%E2%9D%A4-pink?style=for-the-badge)](https://github.com/sponsors/eggspot)

Your sponsorship helps us:
- Maintain and improve the library
- Add new CSS features and PDF capabilities
- Keep the documentation up to date
- Respond to issues and PRs

## License

MIT License. See [LICENSE](LICENSE) for details.

Copyright (c) 2025 Eggspot
