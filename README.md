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
| PDF/A | 1b/1a/2b/2u/2a/3b/3u/3a | No | No | No |
| PDF/UA | UA-1 | No | No | No |
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

Page size, margins, and orientation are set with regular CSS `@page` rules in the HTML itself:

```csharp
string html = @"
    <html><head><style>@page { size: A4; margin: 20mm 15mm; }</style></head>
    <body><h1>My Document</h1></body></html>";

// To byte[]
byte[] pdf = await EggPdf.HtmlToPdf.RenderAsync(html);

// To a file
await EggPdf.HtmlToPdf.RenderToFileAsync(html, "report.pdf");

// To a stream (e.g. an HTTP response body), with cancellation
await EggPdf.HtmlToPdf.RenderAsync(html, Response.Body, HttpContext.RequestAborted);
```

Prefer C# properties over writing CSS? `PdfRenderOptions` is translated into the equivalent
`@page` rule internally — it's not a separate layout path, just a convenience wrapper:

```csharp
byte[] pdf = await EggPdf.HtmlToPdf.RenderAsync(html, new EggPdf.PdfRenderOptions
{
    PageSize = "Letter",
    Orientation = "landscape",
    MarginTop = 40, MarginBottom = 40,   // CSS pixels
    Title = "Q4 Report",                 // PDF document metadata
});
```

## ASP.NET Core Integration

```bash
dotnet add package EggPdf.AspNetCore
```

```csharp
// Plain HTML -> PDF download, no DI registration needed
[HttpGet("report/pdf")]
public IActionResult GetReport()
    => new PdfResult("<h1>Report</h1>", "report.pdf");

// Render a .cshtml Razor view directly -> PDF download, one line.
// Requires services.AddEggPdfRazor() at startup (EggPdf.AspNetCore already
// references EggPdf.Razor, so installing just this package is enough).
[HttpGet("invoice/{id}/pdf")]
public async Task<IActionResult> GetInvoice(int id)
{
    var model = await _invoiceService.GetAsync(id);
    return new RazorPdfResult("Invoice", model, $"invoice-{id}.pdf");
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
- CSS 2.1 complete + CSS3 (Flexbox with auto margins & baseline alignment, Grid incl.
  `grid-auto-flow: dense` and `grid-auto-rows`/`grid-auto-columns`, Multi-column)
- `float: left`/`right` (on blocks and `<img>`) narrows sibling inline content per line (real text
  wrap-around, not just positioning), including `shape-outside: circle()`/`ellipse()`/`polygon()`/
  `inset()` (rounded corners included) and `url()` image shapes (the image's alpha above
  `shape-image-threshold`; JPEG has no alpha, so it falls back to the float's rectangular bounds)
- `overflow: hidden`/`clip` and `contain: paint`/`contain: strict` actually clip descendant
  painted content to the element's bounds, not just its own background/border
- `direction: rtl` / the `dir` attribute: default `text-align` follows the direction; logical properties (margin/padding/border-width/border-color/
  border-style/inset/border-radius corners, `float: inline-start`/`inline-end`) resolve to their
  mirrored physical values; table columns lay out right-to-left and list markers hang on the
  right, matching a browser
- CSS Custom Properties (`var()`)
- `@media print` support
- 2D and 3D CSS transforms (`translate`/`rotate`/`scale`/`matrix` and their `X`/`Y`/`Z`/`3d`
  variants, `perspective()`) -- PDF has no 3D rendering, so 3D functions are intentionally
  flattened to an equivalent 2D matrix (e.g. `rotateX`/`rotateY` become an orthographic
  Y/X compression, `translateZ`/`scaleZ`/`perspective()` have no 2D effect), not skipped
- `content-visibility: hidden` skips laying out and painting descendants (the element's own
  box, background and border still render, sized as if it had no content); `contain: paint`/
  `contain: strict` clip the element's own painting to its bounds. `contain`'s other values
  (`layout`, `style`, `content` alone) are recalculation-isolation hints with no analog in a
  single-pass renderer and are accepted as a no-op rather than rejected
- Webfonts: remote `<link>` stylesheets (Google Fonts) and `@font-face` over http(s), data: URIs, or files
- SVG rendering (vector output, not rasterized) -- `<circle>`/`<ellipse>`/`<rect>`/`<polygon>`/
  `<polyline>`/`<path>`/`<line>` (arcs flattened as true curves), plus SVG filter graphs:
  `filter="url(#id)"` (attribute or `style`) with feGaussianBlur, feOffset, feFlood,
  feColorMatrix, feComponentTransfer, feMerge, feBlend, feComposite, feMorphology, feDropShadow,
  feTurbulence, feConvolveMatrix, feDisplacementMap, feTile, feImage (element or data: bitmap) and
  feDiffuse/SpecularLighting (distant/point/spot lights), evaluated in linearRGB/sRGB per
  `color-interpolation-filters`, with filter regions, primitive subregions and named `in`/`result`
  wiring. A filtered shape, `<text>` (glyph outlines from the installed font), `<use>` or `<g>` is
  rasterized (fills incl. linear/radial gradients, strokes), filtered and re-embedded as an image,
  since PDF has no vector filter primitive. Filtered `<image>` elements and pattern paints paint unfiltered
- All image formats (JPEG, PNG incl. 1-bit QR codes, GIF, WebP -- lossy VP8 incl. alpha, lossless VP8L, extended VP8X containers, first frame of animations --, SVG, Base64)
- Responsive images: `<img srcset>`/`<picture>` and CSS `image-set()` resolve to their best candidate (PDF is treated as a fixed 1x print context)
- CSS Images Level 4 `image()`: resolves `ltr`/`rtl`-tagged candidates against the element's
  computed direction and paints a trailing `<color>` fallback when no image resolves. `paint()`
  (the CSS Houdini Paint API) is not supported and never will be by this engine -- it requires
  running an author-supplied JS paint worklet, and EggPdf has no JavaScript engine by design
- Cloudflare email obfuscation (`data-cfemail`) decoded automatically

### PDF
- PDF 1.4 / 1.5 / 1.7 / 2.0
- Clickable hyperlinks and internal links
- Auto-generated bookmarks from headings
- Table of contents with page numbers
- Running headers/footers
- Page numbers (Page X of Y)
- Tables spanning any number of pages without row loss, with `<thead>` repeating on every continuation page
- Mixed page sizes/orientations via named pages (`page: name` on top-level blocks + `@page name { size; margin; margin boxes }`)
- Watermarks
- PDF/A-1b / PDF/A-1a / PDF/A-2b / PDF/A-2u / PDF/A-2a / PDF/A-3b / PDF/A-3u / PDF/A-3a archival
  conformance (`HtmlToPdf.Render(html, PdfAConformance.PdfA2b)`, `PdfRenderOptions.Conformance`,
  the CLI's `--pdfa` flag, or the REST API's `options.conformance` field): embedded ICC output
  intent, XMP conformance metadata, every font embedded (including the standard 14) with a
  correct ToUnicode mapping. PDF/A-1b/1a writes a PDF 1.4 header and throws if the document uses
  transparency (opacity, blend modes, image alpha) -- PDF/A-1 forbids it outright (ISO 19005-1 has
  no `u` level, only `a`/`b`). The `a` levels require `tagged: true` (level A is PDF/A + full
  accessibility tagging) and throw otherwise
- PDF/UA-1 tagged PDF (`HtmlToPdf.Render(html, tagged: true)`, `PdfRenderOptions.Tagged`, the CLI's
  `--tagged` flag, or the REST API's `options.tagged` field, combinable with PDF/A conformance and
  with named page groups): a structure tree (headings, paragraphs, tables with `<th scope>`, lists,
  landmark regions (`nav`/`header`/`footer`/`aside`/`main`/`article`/`section`, via custom types +
  a `/RoleMap` fallback), figures with `alt` text, links cross-referenced to their annotation via
  `OBJR`, correctly covering every word of a multi-word link, not just the first) linked to page
  content via marked content, plus `/MarkInfo`, `/Lang` and the required XMP identification. A link
  that wraps across lines gets one Link element and one annotation per line (a PDF rectangle can't
  itself wrap)
- PDF/UA-2 (ISO 14289-2:2024, `HtmlToPdf.Render(html, tagged: true, PdfUaVersion.Ua2)` or
  `PdfRenderOptions.UaVersion`): the same tagging machinery as PDF/UA-1, plus PDF 2.0's own
  requirements -- a `%PDF-2.0` header, a declared PDF 2.0 structure namespace every element
  references via `/NS`, and XMP `pdfuaid:part`/`pdfuaid:rev`. Landmark regions resolve straight to
  `Div`/`Sect` under UA-2 rather than a custom type + `/RoleMap`. Cannot combine with PDF/A
  conformance (no defined joint standard); throws rather than silently claim one
- Pin content (e.g. a signature/acceptance box) to the bottom of whichever page dynamic content ends on (`-eggpdf-pin-bottom: page`)

### Typography
- TrueType/OpenType font embedding with subsetting
- Weight-accurate faces: `font-weight: 300–900` each select their own variant
- Font fallback chain + per-codepoint symbol-font fallback (⚠ ✔ …)
- Full Unicode: Vietnamese and extended Latin out of the box
- Arabic contextual shaping: letters take their isolated/initial/medial/final joining forms
  (standard Arabic plus Persian peh/tcheh/jeh/keheh/gaf/yeh) with lam-alef ligatures, harakat
  ignored for joining. Arabic-script letters outside that set (e.g. Urdu ٹ ڈ ڑ) are not shaped
- Complex-script shaping through the font's own GSUB/GPOS tables: Arabic (joining forms via the
  font's presentation-form glyphs or, for modern fonts without them, its own init/medi/fina
  features; diacritics; cursive attachment), Thai/Lao, Tibetan, Khmer, Myanmar, Sinhala and the
  Indic scripts (Devanagari, Bengali, Gujarati, Gurmukhi, Oriya, Tamil, Telugu, Kannada,
  Malayalam). That covers mark-to-base / mark-to-ligature / mark-to-mark positioning, Thai SARA AM,
  Indic and Khmer/Myanmar syllable reordering (pre-base vowel signs, reph placed per script,
  coeng-ro, kinzi) and the font's half forms, conjuncts and subscripts. Needs a font that has the
  script (Nirmala UI, Leelawadee UI, Myanmar Text, Noto Sans ..., or your `@font-face`); such text is
  embedded in its own script-capable font, so Latin text keeps its requested typeface. Verified
  against Chrome's rendering for Devanagari, Bengali, Gujarati, Gurmukhi, Oriya, Tamil, Telugu,
  Kannada, Malayalam, Sinhala, Khmer, Myanmar, Tibetan, Thai and Arabic. Long Thai, Lao, Khmer
  and Myanmar paragraphs wrap at syllable boundaries (a heuristic -- true word breaking needs a
  dictionary, so lines may end mid-word), and `line-height: normal` follows the shaping font's
  ascent + descent + line gap (tall fonts such as Myanmar Text no longer collide). Text runs are
  reordered with the full Unicode Bidirectional Algorithm (UAX #9: explicit embeddings, overrides
  and isolates, weak-type and neutral resolution, paired brackets, mirroring), using the CSS
  `direction` as the paragraph direction; character classes come from a compact table that is exact
  for Latin, Hebrew, Arabic, Syriac, Thaana and NKo and category-derived elsewhere. Scripts beyond
  those listed above are not shaped
- Browser-parity metrics: text measured with the real font, baselines like Chrome
- Automatic hyphenation
- `font-feature-settings` (e.g. `"zero" 1`, `"smcp" 1`) applies single-glyph OpenType
  features (stylistic sets, small caps, oldstyle/tabular figures) from the font's GSUB
  table -- ligature/contextual substitution is not applied
- Variable fonts (TrueType `glyf` outlines with fvar/gvar/avar/HVAR, e.g. Bahnschrift, Inter, Roboto Flex):
  `font-weight` drives the `wght` axis, so `@font-face` with `font-weight: 100 900` (or an installed
  variable font) renders intermediate weights as real instances, verified against Chrome's outlines;
  `font-stretch` (`wdth`), `font-style: oblique <angle>` (`slnt`) and `font-variation-settings`
  (any axis, e.g. `"opsz"`, `"GRAD"`) drive the matching axes and the text is measured with them;
  CFF2 variable fonts (e.g. Source Sans 3 VF) work too -- stems match Chrome at 200/400/650/900
- OpenType fonts with PostScript outlines (`.otf`, CFF and CFF2) are converted to TrueType outlines
  (Type 2 charstrings, subroutines, blend/vsindex, cubic to quadratic), so they measure, shape and embed like any other font
- Color/emoji fonts (COLR v0 + CPAL) render each glyph's real color layers -- COLRv1
  (gradients, paint graphs -- e.g. current Segoe UI Emoji) is not supported and falls
  back to the glyph's outline in the current text color

### Business
- Digital signatures — one-call X.509 signing (`PdfSigner.Sign(pdf, cert)`, detached CMS/PKCS#7) or external-CMS two-step flow for HSMs
- Password protection with permission flags — view-only PDFs that block editing/copying (RC4 40/128-bit)
- AcroForm fields (fillable forms from HTML form elements)
- PDF merging
- QR codes and barcodes
- ZUGFeRD/Factur-X e-invoicing (`HtmlToPdf.Render(html, PdfAConformance.PdfA3b, new FacturXInvoice {...})`,
  `PdfRenderOptions.Invoice`, the CLI's `--invoice <path>` flag, or the REST API's `options.invoice`
  field): embeds the UN/CEFACT CII invoice XML as a PDF/A-3 attachment with the Factur-X XMP
  extension schema. MINIMUM profile when `FacturXInvoice.LineItems` is empty; adding line items
  automatically produces EN 16931 (Comfort)-level output instead -- per-line tax detail, a grouped
  header tax breakdown, and computed (not caller-supplied) monetary totals, with
  `/AFRelationship /Alternative` for German legal validity. BASIC and EXTENDED are not modeled as
  distinct profiles -- populating line items always targets EN 16931, a superset of BASIC's
  requirements but without EXTENDED-only fields (allowances/charges, multiple deliveries, etc.)

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
./eggpdf https://example.com -o page.pdf   # fetch and render a URL
echo "<h1>Hi</h1>" | ./eggpdf - -o output.pdf
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
