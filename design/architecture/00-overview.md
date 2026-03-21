# Architecture Overview

## Pipeline

EggPdf transforms HTML+CSS into PDF through an 8-stage pipeline. Each stage is a pure transformation with a well-defined input and output type. Stages are testable in isolation.

```
                     +-----------+
   HTML string ----->| 1. PARSE  |-----> DOM Tree (HtmlDocument)
                     +-----------+
                          |
                     +-----------+
   <style> blocks -->| 2. CSS    |-----> CSSOM (CssStyleSheet[])
   <link> refs ------>  PARSE    |
   inline styles --->|           |
                     +-----------+
                          |
                     +-----------+
   DOM + CSSOM ----->| 3. STYLE  |-----> Styled Tree (every element has ComputedStyle)
                     |  RESOLVE  |
                     +-----------+
                          |
                     +-----------+
   Styled Tree ----->| 4. BOX    |-----> Box Tree (formatting boxes with display types)
                     |  GENERATE |
                     +-----------+
                          |
                     +-----------+
   Box Tree -------->| 5. LAYOUT |-----> Layout Tree (every box has x, y, width, height)
                     |           |
                     +-----------+
                          |
                     +-----------+
   Layout Tree ----->| 6. FRAG-  |-----> Paged Frames (one frame per page)
                     |  MENT     |
                     +-----------+
                          |
                     +-----------+
   Paged Frames ---->| 7. PAINT  |-----> Paint Command List (abstract drawing ops)
                     |           |
                     +-----------+
                          |
                     +-----------+
   Paint Commands -->| 8. PDF    |-----> byte[] / Stream (PDF 1.7)
                     |  BACKEND  |
                     +-----------+
```

## Data Flow Types

Each stage produces a concrete type consumed by the next:

| Stage | Input Type | Output Type | Project |
|-------|-----------|-------------|---------|
| 1. Parse | `string` (HTML) | `HtmlDocument` (DOM tree) | EggPdf.Html |
| 2. CSS Parse | `HtmlDocument` + CSS text | `CssStyleSheet[]` | EggPdf.Css |
| 3. Style Resolve | `HtmlDocument` + `CssStyleSheet[]` | `StyledTree` (element -> ComputedStyle map) | EggPdf.Style |
| 4. Box Generate | `StyledTree` | `BoxTree` (formatting boxes) | EggPdf.Layout |
| 5. Layout | `BoxTree` + available width/height | `LayoutTree` (boxes with geometry) | EggPdf.Layout |
| 6. Fragment | `LayoutTree` + page size | `PagedFrames` (list of page frames) | EggPdf.Fragmentation |
| 7. Paint | `PagedFrames` | `PaintCommandList` (per page) | EggPdf.Paint |
| 8. PDF Backend | `PaintCommandList` + fonts + images | `byte[]` or `Stream` | EggPdf.Pdf |

## Cross-Cutting Concerns

These systems are used by multiple stages:

| System | Used By | Project |
|--------|---------|---------|
| Resource Resolver | CSS Parse (external stylesheets), Layout (images), Style (fonts) | EggPdf.Core |
| Font Engine | Style Resolve (font matching), Layout (text measurement), Paint (glyph rendering), PDF (font embedding) | EggPdf.Text |
| SVG Engine | Layout (SVG sizing), Paint (SVG drawing) | EggPdf.Svg |
| Image Decoder | Layout (image intrinsic size), Paint (image drawing), PDF (image embedding) | EggPdf.Core |
| Error/Warning Collector | All stages | EggPdf.Core |

## Project Dependency Graph

```
EggPdf (public API facade)
  |
  +-- EggPdf.Html        (depends on: Core)
  +-- EggPdf.Css         (depends on: Core, Html)
  +-- EggPdf.Style       (depends on: Core, Css, Text)
  +-- EggPdf.Layout      (depends on: Core, Style, Text, Svg)
  +-- EggPdf.Fragmentation (depends on: Core, Layout)
  +-- EggPdf.Paint       (depends on: Core, Layout, Fragmentation, Text, Svg)
  +-- EggPdf.Pdf         (depends on: Core, Paint, Text)
  +-- EggPdf.Text        (depends on: Core)
  +-- EggPdf.Svg         (depends on: Core, Paint)
  +-- EggPdf.Core        (no dependencies)
```

**Rule: No circular dependencies.** Core depends on nothing. Each layer depends only on layers below it.

## Key Design Principles

### 1. Pipeline Isolation

Each stage is a pure function: `Input -> Output`. No stage holds mutable state between renders. This enables:
- Unit testing each stage independently
- Parallel renders on the same converter (thread safety)
- Swapping implementations (e.g., raster backend for testing)

### 2. Region-Based Pagination (from Typst)

Layout and fragmentation are NOT separate passes in the traditional sense. Each layout element receives a **sequence of regions** (remaining space on current page, then full pages). The element decides how to split itself.

```
Layout(box, regions) -> Frame[]

regions[0] = { width: 595, height: 300 }  // remaining space on current page
regions[1] = { width: 595, height: 842 }  // full next page
regions[2] = { width: 595, height: 842 }  // full page after that
...
```

The element returns one Frame per region it occupies. This is compositional -- new layout modes (flex, grid) get pagination for free by implementing the same interface.

### 3. Three-State Layout Response (from QuestPDF)

Every layout element reports one of three results:

```csharp
enum LayoutResult
{
    Fit,    // Element fits entirely in the current region
    Split,  // Element partially fits -- render what fits, continue remainder in next region
    Skip    // Element doesn't fit at all -- move entirely to next region
}
```

### 4. Pluggable Paint Backend (from litehtml)

The paint layer emits abstract commands. Multiple backends consume them:

```
PaintCommandList
  |
  +-- PdfPaintBackend    (production: writes PDF content streams)
  +-- RasterPaintBackend (testing: renders to in-memory bitmap)
  +-- SvgPaintBackend    (future: export to SVG)
```

The layout engine knows nothing about PDF. This separation enables visual testing without PDF round-trips.

### 5. Infallible Parsers (from Typst)

The HTML and CSS parsers never throw exceptions. They produce error nodes / skip invalid input and continue. Every possible input produces a valid output.

```csharp
// This NEVER throws, regardless of input
HtmlDocument doc = HtmlParser.Parse(anyString);
```

### 6. Self-Serializing PDF Objects (from PDFsharp)

Each PDF object knows how to write itself:

```csharp
abstract class PdfObject
{
    int ObjectNumber { get; }
    void WriteTo(PdfWriter writer);
}
```

A central `PdfReferenceTable` assigns object numbers and tracks byte offsets for the cross-reference table. Two-phase process: prepare all objects, then serialize sequentially.

### 7. Introspection Loop (from Typst)

Page counters (`counter(page)`, `counter(pages)`) create circular dependencies: the counter value affects layout (text width of "Page 12 of 47"), and layout affects the counter value (which page is it on).

Solution: iterate layout up to 5 times until counters stabilize.

```
Iteration 1: layout -> page count = 45, but "Page X of 45" text changes widths
Iteration 2: layout -> page count = 46 (text got wider, pushed content to new page)
Iteration 3: layout -> page count = 46 (stable!)
Done.
```

If not stable after 5 iterations, use last result and emit a warning.

## Thread Safety Model

```
HtmlToPdfConverter (singleton, immutable after construction)
  |
  +-- PdfOptions (frozen, immutable)
  +-- FontCache (thread-safe ConcurrentDictionary)
  +-- UserAgentStyleSheet (parsed once, immutable)
  +-- ResourceCache (thread-safe, configurable TTL)
  |
  Per-render (new instance per call, no shared mutable state):
  +-- HtmlParser -> HtmlDocument
  +-- CssParser -> CssStyleSheet[]
  +-- StyleResolver -> StyledTree
  +-- LayoutEngine -> LayoutTree
  +-- Fragmenter -> PagedFrames
  +-- Painter -> PaintCommandList
  +-- PdfWriter -> byte[]
```

The converter is safe to share across threads. Each `RenderAsync()` call creates its own pipeline instances with no shared mutable state. The only shared state (font cache, UA stylesheet, resource cache) is thread-safe.

## Memory Model

```
Small document (1 page):
  HTML string:     ~5 KB
  DOM tree:        ~20 KB
  Styled tree:     ~40 KB
  Box tree:        ~30 KB
  Layout tree:     ~30 KB
  Paint commands:  ~15 KB
  PDF output:      ~50 KB
  Peak memory:     ~200 KB (all stages in memory)

Large document (100 pages):
  HTML string:     ~500 KB
  DOM tree:        ~2 MB
  Styled tree:     ~4 MB
  Box tree:        ~3 MB
  Layout tree:     streamed (only current page in memory)
  Paint commands:  streamed (only current page in memory)
  PDF output:      streamed to output
  Peak memory:     ~20-50 MB (bounded by streaming)
```

For streaming output, only the current page's layout/paint/PDF data is in memory. Previous pages have been written to the output stream and discarded.

## Error Handling Strategy

| Error Type | Behavior | Reported As |
|-----------|----------|-------------|
| Parse error (HTML) | Produce error recovery DOM (per HTML5 spec) | No warning (expected) |
| Parse error (CSS) | Skip invalid rule/declaration, continue | `CSS_PARSE_ERROR` warning |
| Unknown CSS property | Silently ignore (per CSS spec) | `CSS_UNSUPPORTED` (verbose only) |
| Font not found | Fall back to next in stack, then system, then Helvetica | `FONT_NOT_FOUND` warning |
| Image load failed | Render alt text in placeholder box | `IMAGE_LOAD_FAILED` warning |
| External resource timeout | Skip resource, continue rendering | `RESOURCE_TIMEOUT` warning |
| Layout overflow | Clip to page bounds | `LAYOUT_OVERFLOW` warning |
| Render timeout | Cancel via CancellationToken | `OperationCanceledException` |
| Max pages exceeded | Stop rendering, return pages so far | `LIMIT_EXCEEDED` warning |

Warnings are collected in `RenderResult.Warnings`. With standard `RenderAsync()`, warnings go to `ILogger` if configured.
