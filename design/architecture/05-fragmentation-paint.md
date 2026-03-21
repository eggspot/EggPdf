# 05 - Fragmentation & Paint Architecture

## Part 1: Fragmentation Engine

### Overview

Splits the continuous layout tree into discrete pages. Implements CSS Fragmentation Level 3 and CSS Paged Media Level 3.

```
LayoutTree (continuous, unbounded height)
    |
    v
Fragmenter (applies @page rules, break properties, orphans/widows)
    |
    v
PagedFrames (list of Frame objects, one per page)
```

### Region-Based Pagination (from Typst)

Instead of a separate "page breaking" pass, pagination is integrated into layout. Each layout element receives a sequence of **regions**:

```csharp
interface IFragmentable
{
    // Layout this element across the given regions
    // Returns one Frame per region consumed
    FragmentResult Fragment(FragmentContext ctx, Region[] regions);
}

class FragmentResult
{
    Frame[] Frames { get; }              // one frame per page this element spans
    IFragmentable? Remainder { get; }    // null if fully laid out, otherwise the leftover
}

struct Region
{
    float Width;
    float AvailableHeight;    // remaining height on this page
    PageContext Page;          // which page (for @page rules, named pages)
}
```

### Break Properties

```
break-before: auto | avoid | page | left | right | recto | verso
break-after:  auto | avoid | page | left | right | recto | verso
break-inside: auto | avoid | avoid-page | avoid-column
```

Decision algorithm for each potential break point:

```
1. If break-before: page on the next element -> FORCE BREAK
2. If break-after: page on the current element -> FORCE BREAK
3. If break-inside: avoid on the parent -> AVOID BREAK (try to fit)
4. If orphans/widows would be violated -> AVOID BREAK
5. If content fits in remaining space -> NO BREAK
6. Otherwise -> BREAK HERE
```

### Orphans and Widows

```
orphans: 3  -> at least 3 lines must remain on the page before a break
widows: 3   -> at least 3 lines must appear on the new page after a break

If breaking a paragraph would violate either constraint:
- Try to move the break point earlier (leave more lines on current page)
- If still can't satisfy: move the entire paragraph to the next page
- If paragraph is taller than a page: break anyway (can't satisfy constraint)
```

### Repeating Table Headers/Footers

```
When a <table> fragments across pages:
1. On each new page, re-render <thead> at the top of the table fragment
2. On each page (except the last), re-render <tfoot> at the bottom
3. Adjust available height for body rows: page_height - thead_height - tfoot_height
```

### @page Rules

```csharp
class PageContext
{
    PageSize Size { get; }           // from @page { size: A4 }
    EdgeSizes Margins { get; }       // from @page { margin: 2cm }
    string? PageName { get; }        // from CSS page property: page: chapter
    int PageNumber { get; }          // 1-based
    bool IsFirst { get; }            // matches :first
    bool IsLeft { get; }             // matches :left (even page numbers)
    bool IsRight { get; }            // matches :right (odd page numbers)
    bool IsBlank { get; }            // matches :blank (forced blank page)
    MarginBoxContent[16] MarginBoxes; // @top-left, @bottom-center, etc.
}
```

### Named Pages and Auto-Breaks

```css
@page { size: A4; }
@page chapter { size: A4; margin: 3cm; }
@page landscape-data { size: A4 landscape; }

h1 { page: chapter; }
.data-table { page: landscape-data; }
```

When an element's `page` property differs from the current page's name, a page break is automatically inserted.

### Page Margin Boxes

```
+--top-left-corner--+-----top-left-----+----top-center----+----top-right-----+--top-right-corner--+
|                   |                  |                  |                  |                    |
+-------------------+------------------+------------------+------------------+--------------------+
|    left-top       |                                                        |    right-top       |
+-------------------+                                                        +--------------------+
|    left-middle    |                    PAGE CONTENT AREA                    |    right-middle    |
+-------------------+                                                        +--------------------+
|    left-bottom    |                                                        |    right-bottom    |
+-------------------+------------------+------------------+------------------+--------------------+
|                   |                  |                  |                  |                    |
+bottom-left-corner-+---bottom-left----+--bottom-center---+---bottom-right---+bottom-right-corner-+
```

Each margin box can contain: `content`, `counter(page)`, `counter(pages)`, `string(chapter-title)`, etc.

### Running Headers (string-set / string())

```css
h1 { string-set: chapter-title content(); }
@page { @top-center { content: string(chapter-title); } }
```

Implementation:
1. During layout, when an element with `string-set` is encountered, record the string value and the page it's on
2. When rendering margin boxes, `string(name)` resolves to the appropriate value:
   - `first`: value of first assignment on this page
   - `last`: value of last assignment on this page (default)
   - `first-except`: value from previous page on pages where the element appears

### Introspection Loop (Page Counters)

`counter(page)` and `counter(pages)` create circular dependencies:

```
Iteration 1: layout with counter(pages) = "?" -> 45 pages
Iteration 2: layout with counter(pages) = "45" -> 46 pages (text got wider)
Iteration 3: layout with counter(pages) = "46" -> 46 pages (STABLE)
```

Max 5 iterations. If not stable, use last result and emit warning.

---

## Part 2: Paint Layer

### Overview

Converts the layout tree into an ordered list of abstract paint commands. Multiple backends consume these commands.

```
PagedFrames (positioned boxes with styles)
    |
    v
Painter (walks box tree in paint order)
    |
    v
PaintCommandList (per page)
    |
    +-- PdfPaintBackend     -> PDF content streams
    +-- RasterPaintBackend  -> in-memory bitmap (for testing)
```

### Paint Order (CSS 2.1 Appendix E)

For each stacking context, paint in this order:

```
1. Background and borders of the stacking context root
2. Child stacking contexts with negative z-index (sorted by z-index)
3. In-flow block-level descendants (non-inline, non-positioned)
4. Float descendants
5. In-flow inline-level descendants (text, inline boxes)
6. Positioned descendants with z-index: auto or z-index: 0
7. Child stacking contexts with positive z-index (sorted by z-index)
```

### Paint Commands

```csharp
abstract record PaintCommand;

// Shapes
record FillRect(RectF Rect, Color Color) : PaintCommand;
record StrokeRect(RectF Rect, Color Color, float Width, BorderStyle Style) : PaintCommand;
record FillRoundedRect(RectF Rect, Color Color, CornerRadii Radii) : PaintCommand;
record FillPath(PathData Path, Color Color) : PaintCommand;
record StrokePath(PathData Path, Color Color, float Width) : PaintCommand;

// Text
record DrawText(float X, float Y, GlyphRun Glyphs, FontFace Font, float FontSize, Color Color) : PaintCommand;

// Images
record DrawImage(RectF Dest, ImageData Image) : PaintCommand;

// Gradients
record FillLinearGradient(RectF Rect, Angle Angle, GradientStop[] Stops) : PaintCommand;
record FillRadialGradient(RectF Rect, PointF Center, float Radius, GradientStop[] Stops) : PaintCommand;

// Effects
record DrawBoxShadow(RectF Rect, BoxShadow Shadow) : PaintCommand;

// State
record PushClip(RectF Rect) : PaintCommand;
record PushClipRoundedRect(RectF Rect, CornerRadii Radii) : PaintCommand;
record PopClip() : PaintCommand;
record PushOpacity(float Opacity) : PaintCommand;
record PopOpacity() : PaintCommand;
record PushTransform(Matrix3x2 Transform) : PaintCommand;
record PopTransform() : PaintCommand;

// Links / annotations
record AddLink(RectF Rect, string Url) : PaintCommand;
record AddInternalLink(RectF Rect, string AnchorId) : PaintCommand;
record AddBookmark(string Title, int Level, float Y) : PaintCommand;

// Structures (for tagged PDF)
record BeginStructureElement(string Tag, string? AltText) : PaintCommand;  // <P>, <H1>, <Table>, <Figure>
record EndStructureElement() : PaintCommand;
```

### GlyphRun

```csharp
class GlyphRun
{
    ushort[] GlyphIds { get; }      // glyph IDs (not codepoints -- after shaping)
    float[] Advances { get; }       // advance width per glyph
    float[] XOffsets { get; }       // x offset adjustments (kerning)
    int[] Codepoints { get; }       // original Unicode codepoints (for ToUnicode)
}
```

### Painter Walk

```csharp
class Painter
{
    PaintCommandList Paint(PagedFrames pages)
    {
        var commands = new PaintCommandList();

        foreach (var page in pages)
        {
            commands.BeginPage(page.Size);

            // Paint page background
            PaintBackground(commands, page);

            // Paint margin boxes (headers, footers, page numbers)
            PaintMarginBoxes(commands, page);

            // Paint content in stacking order
            PaintStackingContext(commands, page.RootBox);

            // Paint overlays (watermarks, bates numbers)
            PaintOverlays(commands, page);

            commands.EndPage();
        }

        return commands;
    }

    void PaintStackingContext(PaintCommandList cmds, LayoutBox box)
    {
        // Follow CSS 2.1 Appendix E paint order
        PaintBackgroundAndBorders(cmds, box);
        PaintNegativeZIndexChildren(cmds, box);
        PaintBlockChildren(cmds, box);
        PaintFloatChildren(cmds, box);
        PaintInlineChildren(cmds, box);
        PaintPositionedChildren(cmds, box);  // z-index: auto/0
        PaintPositiveZIndexChildren(cmds, box);
    }
}
```

### Paint Backends

```csharp
interface IPaintBackend
{
    void Execute(PaintCommandList commands);
}

class PdfPaintBackend : IPaintBackend
{
    // Converts paint commands to PDF content stream operators
    // BT/ET for text, re/m/l/c for paths, Do for images, q/Q for state
}

class RasterPaintBackend : IPaintBackend
{
    // Converts paint commands to pixel operations on an in-memory bitmap
    // Used for visual regression testing (ASCII pixel art, golden images)
    // Output: byte[] (PNG or raw RGBA)
}
```

## Testing

| Test Area | Approach |
|-----------|----------|
| Page breaking | HTML with known break points -> verify page boundaries |
| Orphans/widows | Paragraphs near page end -> verify minimum lines |
| Repeating thead | Multi-page table -> verify header on each page |
| @page rules | Named pages, margin boxes -> verify page properties |
| Margin boxes | Running headers with string-set -> verify correct text per page |
| Counter(page/pages) | Multi-page doc -> verify correct page numbers |
| Paint order | Overlapping positioned elements -> verify z-order |
| Stacking contexts | Complex z-index scenarios -> verify paint order |
| Paint commands | Known HTML -> verify exact paint command sequence |
| Raster backend | ASCII pixel-art tests (WeasyPrint-style) |
