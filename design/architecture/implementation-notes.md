# Implementation notes

Reasons behind code that isn't obvious from the code alone. They used to live in code comments; the
project keeps code comment-free (see CLAUDE.md), so they live here, grouped by file.

## EggPdf.Layout/TextMeasurer.cs

- `MeasureWidth` measures the shaped form the painter will draw: Arabic text first goes through
  `ArabicShaper.Shape` (medial forms are narrower than isolated ones, lam-alef ligatures merge two
  letters), and complex scripts (Thai, Indic, Arabic with marks) are measured as the sum of the
  GSUB/GPOS-shaped advances (conjuncts collapse glyphs, marks are zero-width), not per code point.
- `NonLatinAdjustment` corrects the built-in-font estimate for characters above U+00FF: the AFM table
  charges each such UTF-16 unit one flat width, while the PDF paints the real glyph advance that
  `GlyphAdvanceProvider` reports.
- `ComplexMetrics` gives ascent/descent/line gap only for shaped complex scripts: tall fonts
  (Myanmar, Khmer, Indic) need far more than the 1.2em default line box or lines collide.
- `HyphenateWordToFit` tries break points right to left and keeps the rightmost one that fits.
- The shared English hyphenator is immutable after construction, so one instance serves all threads.

## EggPdf.Css/CssShorthandExpander.cs

- `flex` with one value: a unitless number is `flex-grow` (shrink 1, basis 0); anything else is the
  basis with grow 1 and shrink 1. Two values: `<grow> <shrink>` (basis 0) or `<grow> <basis>`.
- `text-decoration` keeps its original value next to the expanded longhands, for code that still
  reads the shorthand.
- `border-radius` with a slash stores each corner as a two-value "H V" pair.
- In `background`, `url(...)` and gradient functions are extracted before tokenizing by spaces
  (they may contain spaces and commas); the last unrecognised token is the colour.

## EggPdf.Css/BasicStyleResolver.cs

- Checkbox and radio inputs get a fixed 13px native control size with no padding: they are painted
  as a vector square/circle (`BoxPainter.PaintCheckboxOrRadio`), not sized by glyph text.

## EggPdf.Layout/FlexLayout.cs

- An auto-height column container has an indefinite main size: items keep their hypothetical sizes
  (no grow, shrink or justify free space). A column container's main size is its height, else its
  `min-height`, reduced to the content height for `border-box`.
- Row items whose flex-resolved width differs from their first `CreateBox` width are laid out again
  at the resolved width, so their content wraps there instead of overflowing at the container
  width. The containing width passed is the resolved width plus margins, and an explicit `width`
  is overridden by the resolved one.
- Direct text and `<br>` children form an anonymous flex item (CSS Flexbox §4). It carries only the
  inherited text properties: copying the container's borders or backgrounds would paint them twice
  (a second circle inside a round stamp).
- Absolutely and fixed positioned children are not flex items; the container positions them after
  flex layout.
- Items size against the container's content box. An auto basis in a row is the max-content width:
  the larger of the widest laid-out leaf (block descendants fill the container, so their own width
  says nothing) and the unwrapped text width.
- Column items get the automatic minimum size (§4.5): an item with visible overflow and no explicit
  height or basis cannot shrink below its content height, because block content doesn't reflow
  vertically and the next sibling would overlap it. The minimum is capped by `max-height`.
- Free space counts the items' main-axis margins, which `PositionMainAxis` advances past.
- Auto margins absorb positive free space before `justify-content` (§8.1): `margin-top: auto`
  pushes a footer to the bottom.
- `align-items: baseline` in a row approximates each item's ascent as 0.8 em; in a column it falls
  back to flex-start. `align-content` does nothing when the cross size is auto.

## EggPdf.Layout/GridLayout.cs

- `grid-auto-rows` / `grid-auto-columns` size the implicit tracks auto-placement creates; a list
  cycles ("50px 80px" alternates). Without them, implicit rows are `auto` and implicit columns `1fr`.
- With no column template, row flow gets one column; column flow gets as many columns as the
  explicitly placed items need, and `AutoPlaceItems` may grow more (it returns the final count).
- A subgrid gets the parent's resolved track sizes for its span, and the parent's gap on that axis
  unless it declares its own.
- `::before` and `::after` with content are grid items themselves (CSS Display §3), placed before and
  after the real children: `li::before { content: "-" }` in a `14px 1fr` grid is a common custom
  marker. Their box is built directly, since `BlockLayout.CreateBox` needs a real element.
- A spanning item that is taller than its rows shares the extra height equally across its `auto` rows.
- `gap` with two values is `row-gap column-gap`. Absolutely and fixed positioned children are not
  grid items.
- `minmax(min, max)` sizes the track by its max; `repeat(auto-fill | auto-fit, …)` fits as many
  tracks as the container width allows at the min size, and auto-fit caps the count at the number
  of items.

## EggPdf.Layout/StandardFontMetrics.cs

- Widths are the PDF standard 14 AFM advances in 1/1000 em for WinAnsi 32–126; a no-break space
  measures as a space, and characters outside the table measure 500.
- Arial maps to the Helvetica tables: the two are metric-compatible. Arial and Segoe UI resolve to
  "Arial" so the installed TrueType font gets embedded.
- With a measurement provider active (web fonts), numeric weights other than 400 and 700 get their
  own face name (`Arial-W600`), so each weight embeds and paints its matching variant.
- `ResolveBaseFontName` runs for every measured word, so its result is memoized (capped at 512
  entries).

## EggPdf.Layout/BlockLayout.InlineRuns.cs

- `<rt>` and `<rp>` are laid out by `LayoutRubyInline`, not by the normal inline flow.
- `TryResolvePseudoContent` returns false when no cascade is active, no rule matches, or `content`
  yields nothing; in that last case the style is still returned so callers can lay out an empty box.
- One `LayoutInlineRuns` call lays out the runs of one inline child element; the element's span
  rectangle restarts whenever the line (childY) changes.
- With floats, the left inset and right wrap limit are queried per line at that line's absolute Y.
  `atLineStart` records "nothing placed on this line yet", because a line beside a float starts with
  `inlineX > 0`. Without floats every helper reduces to the flat container width.
- A run that normalizes to nothing (the newline and indentation between two inline elements) still
  marks a boundary space for the next run. A boundary space also comes from the previous run's
  trailing whitespace ("đến <strong>bản</strong>").
- Text is measured after `text-transform` (uppercase is wider); the transform is idempotent, so the
  word boxes carry the transformed text.
- A word box's Y is relative to a block whose final position is applied later by its parent, so it
  can be negative for a moment.

## EggPdf.Layout/MultiColumnLayout.cs

- With both `column-count` and `column-width`, the count is the maximum and the width lowers it when
  fewer columns of that width fit.

## EggPdf.Layout/BlockLayout.AbsolutePositioning.cs

- `LayoutAbsoluteChildren` runs after the container's final size is known and appends the
  positioned boxes to the container's children.
- Containing block: a sheet-scoped fixed box (`IsSheetScopedFixed`) uses the whole sheet; any other
  fixed box uses the page area inside the `@page` margins; an absolute box uses the nearest
  positioned box's padding box (CSS 2.1 §10.1, so offsets count from inside the border), else the
  page root.
- With both `left` and `right` (or `top` and `bottom`) and no explicit size, the size comes from the
  offsets.
- X is absolute, so moving the box also shifts its already laid-out descendants; Y is
  parent-relative and resolved in the post-layout pass.

## EggPdf.Layout/BlockLayout.TableColumns.cs

- `table-layout: fixed` takes widths from `<col>` elements first, then from the first row's
  single-column cells; the remaining columns share the leftover width equally. Auto-layout widths
  are cached per table element for the whole layout run.

## EggPdf/PdfRenderer.cs

- Paint order is the z-index-sorted box list, not a tree walk, so a box's clipping ancestors
  (`overflow: hidden | clip`, `contain: paint | strict`) are found through an ancestor map built
  from the real tree. The map is only built when the tree has such a clip at all. Each ancestor's
  clip is placed with the same page-Y shift and clamping as any painted box.
- Fixed-position boxes and `@page` margin boxes are page-local, so they are kept out of the Y-band
  pagination and painted once per page (with `counter(page)` / `counter(pages)` substituted).
- The page band height subtracts the top and bottom `@page` margins separately: asymmetric
  margins are common.
- The body's bottom margin is a snap zone: content does not start inside it at a page bottom (it
  is ignored when larger than half the band).
- A forced break bounds a page's band: a section between forced breaks can be taller than a page
  (a long table), and treating it as one page painted its overflow below the physical page, where
  no viewer shows it.
- `pageCapacityBottom` is where a page's content area really ends (a forced break, a full band or a
  smart break), while the last page's bound is clamped to where the content stops.
- Smart breaks move the page bottom up to the top of: an unsplittable box that would straddle it or
  start inside the body's bottom-margin zone; a `<thead>` whose first body row would be cut off;
  a `break-inside: avoid` box that would be split; and a text block with too few orphan lines (or
  to the line that leaves enough widows).
- `-eggpdf-pin-bottom: page` moves the marked box and everything after it on that page down to the
  page's content bottom, when the marked box fits on one page. It is a paint-time offset: the tree's
  Y values never change, so pagination is unaffected.
- A `<thead>` is repainted at the top of every page its table's body continues onto; every other
  box on such a page shifts down by the header's height (added to any pin-bottom shift).
- Every page first gets a white canvas: viewers otherwise show the transparent page off-white,
  unlike the browser's white canvas.

## EggPdf.Fragmentation/PageFragmenter.cs

- A box with an `id` is collected as paintable even when it draws nothing: it is an internal-link
  destination, and `BoxPainter` registers its page and position when it runs for the box. A
  `column-rule` also needs a paint pass without any background or border.

## EggPdf.Svg/SvgRenderer.Filter.cs

- A filtered element is rasterized in its own local coordinate system (its transform and its
  ancestors' are already on the CTM). `RenderFilteredElement` returns false when the element can't
  be rasterized, so the caller paints it unfiltered; an empty filter region renders nothing.
- Resolution is about 2 pixels per PDF point, lowered when the region would exceed the 2000-pixel
  canvas cap. A small slack keeps float noise (1.2f × 20 = 24.0000005) from adding a whole pixel.
- The image's unit square has y up while local SVG space has y down, so the placement matrix flips it.
- `feImage` bitmaps are decoded from PNG, GIF, BMP and WebP data URIs only; JPEG stays DCT-compressed
  and is not used as a filter input.

## EggPdf.Text/SystemFontLocator.cs

- The font file list is discovered once per process: the directory walk is expensive (hundreds of
  files on Linux with Noto) and installing fonts while the process runs is not supported.
  Unreadable directories are skipped.
- `FindFontIn` ranks file names: exact or space-less matches ("Segoe UI" → segoeui.ttf) beat
  substring matches ("Arial.ttf" over "Arial Black.ttf"), Bold/Italic files the query didn't ask
  for are demoted (so "LiberationSans" finds Regular), and shorter names win ties.

## EggPdf.Pdf/PdfACompliance.cs

- PDF/UA identification has a part only (no conformance letter). UA-2 (ISO 14289-2:2024) also
  needs `pdfuaid:rev` with the year of the referenced ISO 32000-2 edition, per the PDF
  Association's guidance on versioned UA parts.
- PDF/UA-1 requires a title (rules 7.1-8/7.1-9), so an empty one becomes "Untitled Document".
- The XMP packet ends with about 2 KB of padding, as the XMP spec recommends for in-place updates.
- The Factur-X extension schema (prefix `fx`, `urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#`)
  matches the reference schema at github.com/atgp/factur-x (`xmp/Factur-X_extension_schema.xmp`).
- XML escaping also drops C0 controls other than tab, LF and CR, which XML 1.0 forbids: titles and
  authors come from arbitrary HTML and must not break the packet.

## EggPdf.Pdf/PdfPage.cs

- `AppendOpLine` always ends a line with LF: `StringBuilder.AppendLine` writes `Environment.NewLine`,
  which would make Windows output differ from Linux for the same input, and the PDFs get hashed
  and signed.
- `SetOpacity(1)` still emits a reset state: the `gs` operator persists, so skipping it would leak
  the previous alpha into later content.
- Border lines: dashed is 3× the width on and off with butt caps; dotted is a zero-length dash
  with round caps every 2× the width; double is two lines a third of the width wide with a third
  between them.
- Rounded-rectangle radii are clamped to half the width and height; the corners are cubic Béziers
  with the circle kappa 0.5522847498.
- Text outside WinAnsi is written as `?` for the standard 14 fonts.

## EggPdf.Pdf/PdfPrintProduction.cs

- The MediaBox is the trim size plus bleed, plus the crop-mark area when marks are on; the TrimBox is
  the final page size; the BleedBox is the TrimBox grown by the bleed.
