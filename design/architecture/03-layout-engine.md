# 03 - Layout Engine Architecture

## Overview

The layout engine is **60% of the total project effort**. It transforms a styled box tree into a layout tree where every box has concrete x, y, width, height coordinates.

```
StyledTree (elements with ComputedStyle)
    |
    v
BoxGenerator (creates formatting boxes)
    |
    v
BoxTree (block, inline, flex, grid, table boxes)
    |
    v
LayoutEngine (positions and sizes every box)
    |
    v
LayoutTree (every box has x, y, width, height)
```

## Box Generation

### DOM Element -> Formatting Box

```csharp
static class BoxGenerator
{
    static BoxTree Generate(StyledTree styledTree)
    {
        // For each element:
        // 1. Check display property -> determine box type
        // 2. Handle display: none -> no box
        // 3. Handle display: contents -> promote children
        // 4. Generate ::before / ::after pseudo-element boxes
        // 5. Create anonymous boxes where needed
    }
}
```

| display value | Box created |
|--------------|-------------|
| `block` | `BlockBox` |
| `inline` | `InlineBox` |
| `inline-block` | `InlineBlockBox` (inline-level, block container) |
| `flex` | `FlexContainerBox` |
| `inline-flex` | `InlineFlexContainerBox` |
| `grid` | `GridContainerBox` |
| `inline-grid` | `InlineGridContainerBox` |
| `table` | `TableBox` (wrapped in anonymous table wrapper) |
| `table-row` | `TableRowBox` |
| `table-cell` | `TableCellBox` |
| `list-item` | `BlockBox` + `MarkerBox` |
| `none` | No box generated |
| `contents` | No box; children promoted to parent |

### Anonymous Box Generation

CSS 2.1 Section 9.2.1.1: When inline content is mixed with block content inside a block container, anonymous block boxes are created to wrap the inline content.

```html
<div>
  Text before           <!-- wrapped in anonymous block box -->
  <p>Block element</p>  <!-- its own block box -->
  Text after            <!-- wrapped in anonymous block box -->
</div>
```

### Generated Content (::before, ::after)

```csharp
// CSS: .price::before { content: "$"; }
// Creates an InlineBox with text "$" as the first child of .price's box
```

Content property values:
- `"string"` -> text node
- `counter(name)` / `counters(name, separator)` -> generated text
- `attr(attribute-name)` -> attribute value text
- `url(image)` -> replaced inline image
- `open-quote` / `close-quote` -> quote characters from `quotes` property
- `leader('.')` -> dot leader (repeating fill)
- `target-counter(url, counter)` -> page number of linked element

## Box Types

```csharp
abstract class LayoutBox
{
    ComputedStyle Style { get; }
    HtmlElement? Element { get; }       // null for anonymous boxes
    LayoutBox? Parent { get; }
    List<LayoutBox> Children { get; }

    // Geometry (set during layout)
    float X { get; set; }
    float Y { get; set; }
    float Width { get; set; }
    float Height { get; set; }

    // Box model
    EdgeSizes Margin { get; }
    EdgeSizes Border { get; }
    EdgeSizes Padding { get; }
    float ContentWidth => Width - Padding.Left - Padding.Right - Border.Left - Border.Right;
    float ContentHeight => Height - Padding.Top - Padding.Bottom - Border.Top - Border.Bottom;

    // Computed from style
    float? SpecifiedWidth { get; }      // null = auto
    float? SpecifiedHeight { get; }     // null = auto
    float? MinWidth { get; }
    float? MaxWidth { get; }
    float? MinHeight { get; }
    float? MaxHeight { get; }

    // Layout interface (region-based pagination)
    abstract LayoutResult Layout(LayoutContext ctx, Region[] regions);
}

struct Region
{
    float Width;
    float Height;               // available height in this region
    bool IsFirstRegion;         // is this the first region (top of first page)?
}

struct EdgeSizes
{
    float Top, Right, Bottom, Left;
}
```

## Formatting Contexts

Each layout algorithm is a **formatting context**. The layout engine dispatches to the correct context based on the box type.

### Block Formatting Context (BFC)

```
Children stack vertically, top-to-bottom.
Each child's width is resolved from the containing block.

+------ containing block width ------+
|  [child 1: full width]             |
|  [child 2: full width]             |
|  [child 3: width: 50%]             |
|                                     |
+-------------------------------------+
```

Key responsibilities:
- Lay out block children top-to-bottom
- Resolve `width: auto` to containing block width
- **Margin collapsing** (most complex part of BFC)
- Contain floats (floats don't escape the BFC)
- Establish new BFC for: `overflow: hidden`, `display: flow-root`, flex/grid items, floats, absolutely positioned elements

#### Margin Collapsing Rules

Adjacent vertical margins collapse (the larger wins, not the sum):

```
Case 1: Adjacent siblings
  div { margin-bottom: 20px }
  p   { margin-top: 30px }
  -> collapsed margin = 30px (not 50px)

Case 2: Parent-child (no border/padding between them)
  div { margin-top: 20px }
  div > p:first-child { margin-top: 30px }
  -> parent's top margin = 30px (child's margin "escapes")

Case 3: Empty block
  div { margin-top: 20px; margin-bottom: 30px; height: 0; }
  -> collapsed to 30px

Case 4: Negative margins
  max(positive margins) + min(negative margins)

Case 5: Through multiple empty blocks
  Margins collapse through empty blocks with no border/padding/height
```

### Inline Formatting Context (IFC)

```
Inline content flows left-to-right, wrapping into line boxes.

+------ containing block width ------+
| [inline1][inline2][inline3]        |  <- line box 1
| [inline4][inline5]                 |  <- line box 2
+-------------------------------------+
```

Key responsibilities:
- Collect inline boxes and text runs into **line boxes**
- **Text measurement**: measure glyph widths using font metrics
- **Line breaking**: Unicode Line Break Algorithm (UAX #14) + CSS `word-break` / `overflow-wrap`
- **Vertical alignment**: `vertical-align` within line boxes (baseline, top, middle, etc.)
- Handle replaced inline elements (`<img>`)
- `text-align` for horizontal alignment within line box
- `text-indent` for first line
- **Whitespace collapsing** per `white-space` property

#### Line Breaking Algorithm

```
1. Collect inline content into a flat sequence of "items":
   - Text runs (with font metrics for width measurement)
   - Inline boxes (start edge, end edge)
   - Replaced elements (img with intrinsic width)
   - Forced breaks (<br>)

2. For each potential break point (per UAX #14):
   - Measure width of content from line start to break point
   - If width <= available width: continue
   - If width > available width: break here, start new line

3. Special rules:
   - CJK characters: break allowed between any two CJK chars
   - word-break: break-all: break allowed between any two chars
   - overflow-wrap: break-word: break within word if no other break point
   - hyphens: auto: insert hyphens at dictionary break points
   - white-space: nowrap: no wrapping at all
```

### Flex Layout

Implements the full CSS Flexible Box Layout Level 1 algorithm (9 steps):

```
1. Generate flex items (each child of flex container)
2. Determine available main space and cross space
3. Determine flex base size for each item (from flex-basis, width, or content)
4. Collect items into flex lines (flex-wrap)
5. Resolve flexible lengths:
   a. Calculate total flex grow/shrink factors
   b. Distribute free space (grow) or negative space (shrink)
   c. Clamp to min/max-width/height
   d. Re-distribute if any item was clamped (loop)
6. Determine hypothetical cross sizes
7. Calculate cross size of each flex line
8. Align items within flex line (align-items, align-self)
9. Align flex lines (align-content)
10. Apply order property for visual reordering
```

### Grid Layout

Implements CSS Grid Layout Level 1:

```
1. Build the explicit grid from grid-template-rows/columns/areas
2. Place explicitly positioned items (grid-row, grid-column, grid-area)
3. Auto-place remaining items (grid-auto-flow: row/column/dense)
4. Size grid tracks:
   a. Initialize to min/max track sizing functions
   b. Resolve intrinsic track sizes (min-content, max-content)
   c. Maximize tracks within available space
   d. Expand flexible tracks (fr units)
5. Resolve gaps (gap, row-gap, column-gap)
6. Position items within their grid areas
7. Align items (justify-items, align-items, justify-self, align-self)
8. Align tracks (justify-content, align-content)
```

### Table Layout

Implements CSS 2.1 Section 17:

```
1. Build table structure (table, row groups, rows, cells)
2. Handle missing elements (anonymous table wrappers, rows, cells)
3. Calculate column widths:
   a. Fixed layout: use column widths from first row or <col>
   b. Auto layout: intrinsic sizing (min-content, max-content per column)
4. Distribute available width to columns
5. Calculate row heights from cell content
6. Handle spanning cells (colspan, rowspan)
7. Position all cells
8. Apply border collapsing or separated borders
9. Place caption
10. Handle repeating <thead>/<tfoot> across pages
```

### Float Layout

```
1. Remove floated element from normal flow
2. Position at the current line's edge (left or right)
3. Shorten subsequent line boxes to avoid the float
4. Clear: move below all previous floats on the specified side
5. Floats interact with BFC boundaries (contained within BFC)
```

### Positioned Layout

```
Relative: offset from normal flow position (doesn't affect other elements)
Absolute: positioned relative to nearest positioned ancestor
Fixed: positioned relative to the page box (in paged media)
Sticky: treated as relative for layout purposes (no scroll in print)

Stacking contexts created by:
- position: relative/absolute/fixed with z-index != auto
- opacity < 1
- transform != none
- isolation: isolate
- mix-blend-mode != normal
```

## Intrinsic Sizing

Critical for flex, grid, and table layout. Every box can report:

```csharp
interface IIntrinsicSizable
{
    float MinContentWidth { get; }   // narrowest without overflow
    float MaxContentWidth { get; }   // widest (no wrapping)
    float MinContentHeight(float width);  // height at given width
}
```

These are computed recursively: parent intrinsic size depends on children's intrinsic sizes.

## Layout Context

```csharp
class LayoutContext
{
    float ContainingBlockWidth { get; }
    float ContainingBlockHeight { get; }
    FontCache FontCache { get; }
    IResourceResolver ResourceResolver { get; }
    WarningCollector Warnings { get; }
    CancellationToken CancellationToken { get; }
    int RecursionDepth { get; }         // guard against infinite recursion
    int MaxRecursionDepth { get; }      // default: 100
}
```

## Testing

| Test Area | Approach |
|-----------|----------|
| Block layout | Assert exact x, y, width, height for known HTML+CSS |
| Margin collapsing | All 5+ cases with exact gap assertions |
| Inline layout | Line break positions, line box heights, text alignment |
| Flex layout | All 9 algorithm steps with various configurations |
| Grid layout | Track sizing, auto-placement, spanning, alignment |
| Table layout | Column widths, spanning cells, border collapse |
| Float layout | Float positioning, line shortening, clear |
| Intrinsic sizing | min-content, max-content for nested structures |
| WPT conformance | CSS 2.1, Flexbox, Grid test suites |
