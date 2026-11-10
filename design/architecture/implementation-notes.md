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

- Widths are the PDF standard 14 AFM advances in 1/1000 em for WinAnsi 32–126 (Helvetica and Times
  each have their own regular, bold, italic and bold-italic tables); a no-break space measures as a
  space, an accented Latin-1 letter (U+00C0–U+00FF with a canonical decomposition) measures as its
  base letter, and any other character measures 500.
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

## EggPdf.Layout/BlockLayout.cs

- `_fullPageWidthPx` / `_fullPageHeightPx` are the physical sheet, while the page width and height
  passed to layout are the content area inside the `@page` margins. They default to the content
  area for callers (mostly tests) with no real `@page` margin.
- The table caches are cleared at the start of every layout: a pooled thread would otherwise keep
  every table element of every past render.
- The `<html>` style is resolved first so custom properties on `:root` are inherited.
- After layout, `ResolveAbsolutePositions` turns parent-relative Y values into absolute ones. The
  body's top margin is then applied. It has already collapsed with its first child's top margin (as
  in Chrome: content starts at max(8px, the child's margin) by default); the body's bottom margin does
  not collapse with its last child, because `PdfRenderer` uses it as the bottom snap zone of every page.
- `content-visibility: hidden` keeps the element's own box (size, background, border) and skips
  its descendants, which contribute nothing to its size; its height is resolved there because no
  child layout runs.
- `LayoutNormalFlowChildren` stays one method: childY, inlineX, the float context, the deferred
  absolute children and more thread through the whole child walk, so splitting it would change how
  that state is carried, not just move code.
- Floats: an outer block that doesn't establish a BFC passes its float context down, so its inline
  content and its block children's descendants see floats registered earlier in the same BFC; a
  float is a new BFC root with a fresh context. `clear` is resolved before laying out the child so
  the float origin passed down is the cleared Y; `box.Y` can still be provisional there, so the real
  position travels as `floatOriginY`. The child's predicted top margin (collapsed with the previous
  sibling's bottom margin) is folded into that origin, or lines just below a float would still wrap
  beside it. A float registers with `shape-outside` when set, and `shape-margin` extends its bottom
  for `clear`.
- Table rows: cell column offsets are computed once per row (per-cell rescans were O(cells²)). A
  `display: table` box whose children are table cells with no row wrapper acts as one anonymous row
  (CSS 2.1, simplified): the "table div > cell divs" equal-height-columns pattern. There every direct
  element child counts as a column, because resolving a sibling's style ahead of the normal loop isn't
  reliable for stylesheet-driven selector state. In an RTL table, column 0 is at the right edge.
  `border-spacing` defaults to 2px and is 0 with `border-collapse: collapse`, which also drops each
  cell's left border after the first column and top border after the first row. A moved cell shifts
  its whole subtree, because cell content is usually nested more than one level deep.
- Relative and sticky offsets are applied after the parent sets the child's normal-flow Y, which
  would otherwise overwrite them; sticky behaves like relative in a PDF. `visibility: collapse`
  removes a table row's height; on other elements it behaves like `hidden`.
- An empty `<br>` line (leading or repeated) adds a line height; after text, the line was already
  advanced.
- `<svg>` is a replaced element sized by its width/height attributes (300×150 by default); its
  children are not HTML text the inline path could find.
- With floats active, a text node is placed a word at a time against the per-line float limits
  instead of `TextMeasurer.WrapText`'s single width, skipping first-letter, initial-letter,
  line-clamp, `text-wrap: balance` and hyphenation there. Words on one line are still emitted as one
  box, so a multi-word phrase is one contiguous string in the PDF, like the non-float path.
- `text-wrap: balance` wraps once at full width to count lines, then wraps at the balanced width.
  `text-indent` narrows only the first line; the rest is re-wrapped at full width.
- `line-clamp` keeps N lines and trims the last one by words (characters as a last resort) until it
  fits with "…"; `hanging-punctuation: first` hangs a leading quote or bracket outside the line.
- Rows equalize their cells' heights, and `vertical-align: middle | bottom` shifts the cell's content.
- An outside list marker hangs off the side the text starts from: left in LTR, right in RTL.
- Table cells ignore their own `width` here: the column width from the table algorithm is passed in
  as the containing width, and resolving "20%" against it again would give 4%.
- Auto margins centre a block only when its width is specified; an auto-width block fills the
  container.
- `::marker { content }` replaces the generated marker text.

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

## EggPdf.Paint/BoxPainter.cs

- An element's `id` is registered as a link destination before the visibility check: a
  `visibility: hidden` element still occupies its box and a browser still scrolls to it.
- With PDF/UA tagging, each box's whole paint output is one BDC/EMC span attributed to the
  structure element `StructureTreeBuilder` assigned it; untagged renders skip that entirely.
- Anonymous text boxes inherit the parent's style but paint no background or border: those belong
  to the parent element's box. `empty-cells: hide` suppresses both for a cell with no text or image.
- Arabic is shaped on logical-order text, before the bidi reorder; complex scripts (Thai, Indic,
  Arabic with marks) are shaped from the logical text and painted from their own embedded font,
  registered as `<font>-CX<script>`, falling back to the normal path when it isn't embedded.
- `text-align: justify` treats a line shorter than half the container as a paragraph's last line
  and aligns it by `text-align-last`.
- The baseline sits half the leading above the ascent (real font metrics when available), like a
  browser; a full em below the line top made large fonts with tight line-height overlap the next
  line.
- Text with no colour paints opaque black (the CSS initial colour), so a `filter` still applies.
- A run containing COLR/CPAL colour glyphs (emoji) paints one code point at a time: a PDF text
  operator paints a whole string in one colour, but a colour glyph is several layers in their own
  colours. Each step advances by the base glyph's advance plus letter-spacing; word-spacing and
  cross-font fallback are not applied there.
- Glyph fallback: a code point the main font lacks uses the first fallback font that has it; an
  Arabic presentation form no font has is replaced by its base letters (disconnected but legible);
  a default-ignorable format character is dropped; anything else is `.notdef` in the main font.
- `object-position` Y is mirrored because the image rectangle is already in PDF's bottom-up space.
- Margin boxes: page 1 prefers a `:first` box, even pages `:left`, other odd pages `:right`, each
  falling back to the base `@page` box for that position; untagged fixed boxes always paint.
- `image-set()` picks the candidate whose density is closest to 1x, like `ResolveSrcset`: a PDF is a
  fixed 1x print context.
- An internal `#fragment` href is percent-decoded like a browser (`#sec%20one` → "sec one").
- The checked state of a checkbox is the accent colour over the whole control plus a white check;
  a radio gets an accent dot. The unchecked state is just the control's own border and background.
- 3D transforms are flattened to 2D: `rotateX`/`rotateY` compress one axis by the cosine, Z
  translation and scale are dropped, `matrix3d` keeps its 2D affine part (m0, m1, m4, m5, m12, m13).
- `z-index` sorting is stable, so equal z-indices keep document order.

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

## .github/workflows/publish.yml

- Publish runs are queued, never cancelled (`concurrency: publish`). Two merges seconds apart would
  otherwise both read the same last tag, compute the same next version and race to create it:
  NuGet's `--skip-duplicate` makes the second package push harmless, but `git tag` / `git push` of
  the tag is not, so the second run would fail. A queued run re-reads the tag when its turn comes
  and computes the version after the previous one.
- `site/**/*.html` keeps the version as the literal placeholder `__EGGPDF_VERSION__`, which
  `pages.yml` substitutes at deploy time without committing. Committing the substitution would
  leave nothing to replace in the next release.
- Release notes are generated from merged PR titles. Any `@word` in them except the author credits
  (`by @user in`, `@user made their first contribution`) is wrapped in backticks before the release
  is created: a PR title that mentions a CSS at-rule (`@page`, `@media`) would otherwise mention, and
  notify, the GitHub account of that name and list it as a contributor.

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
- Rounded-rectangle corners are elliptical (`CornerRadii`: a horizontal and a vertical radius per
  corner; a corner with either radius 0 is square). All radii are scaled down together, by one factor,
  when the radii on any side add up to more than that side (CSS Backgrounds 3 §5.5); the corners are
  cubic Béziers with the circle kappa 0.5522847498 applied per axis.
- `inset`, `outset`, `groove` and `ridge` borders follow Chromium's `box_border_painter.cc`
  (`BorderShade`): a side is darkened when (it is the top or left side) equals (the style is inset);
  darkening is `Color::Dark()` (the brightest channel drops by 0.33 and the others scale with it),
  lightening is `Color::Light()` (black becomes rgb(84, 84, 84)), a colour lighter than luminance
  0.83077 is left as is on the light side, and a colour darker than luminance 0.014443844 is lightened
  once on the dark side and twice on the light side. Groove paints the outer half of each side as
  inset and the inner half as outset; ridge the reverse. The public `AddBorderLine` without a side
  shades a 3D line as a top border.
- On a rounded box, a border that isn't one uniform solid stroke is painted side by side
  (`AddRoundedBorderSide`): each side is clipped to the trapezoid from the outer box corners to the
  inner (padding-box) corners, and the whole rounded ring is stroked inside that clip with the side's
  width, colour and style, so the corners split along the diagonal as in Chrome.
- A wavy decoration is a chain of half waves, each one cubic Bézier with control points at a third
  and two thirds of it, raised 4/3 of the amplitude so the crest reaches the amplitude.
- Text outside WinAnsi is written as `?` for the standard 14 fonts.

## EggPdf.Pdf/PdfPrintProduction.cs

- The MediaBox is the trim size plus bleed, plus the crop-mark area when marks are on; the TrimBox is
  the final page size; the BleedBox is the TrimBox grown by the bleed.

## EggPdf.Layout/BlockLayout.FloatedImage.cs

- A floated image registers its margin box as the exclusion; a `shape-outside` shape is defined on the
  border box, so the margin on the content side is left out for shapes.
- The bottom that later `clear` content must start below is relative to the content area and includes
  `shape-margin`.

## EggPdf/ResourceLoader.cs

- The per-render caches are created on first use: most renders load nothing external, and every
  render builds a loader.
- A caller's resolver runs off the calling thread, so a resolver that awaits without
  `ConfigureAwait(false)` can't deadlock against the caller's synchronization context while the
  render thread blocks on it. The timeout's token source is disposed only once the resolver is done
  with it, even when the render stops waiting first. The wait is bounded even if the resolver ignores
  its token, and it throws when the caller cancels.
- A one-letter "scheme" is a Windows drive letter (`C:\...`), not a URL scheme.

## EggPdf.Text/TrueType/TtfParser.cs

- A TrueType Collection is parsed through a standalone copy of its first face, and a CFF/CFF2 ("OTTO")
  font is converted to glyf first, so measuring, shaping, subsetting and embedding all see one ordinary
  TrueType sfnt (collections of CFF faces are common for CJK system fonts).
- cmap: Unicode subtables (platform 0, or 3 with encoding 1/10); format 12 is preferred over format 4.
  A format 12 table maps at most 500,000 code points, a guard against corrupt group counts.
- Kerning comes from the `kern` table (format 0, horizontal, not cross-stream) and from the GPOS `kern`
  feature's pair-adjustment lookups; only the first glyph's x-advance is used.
- GPOS class-based pair kerning (PairPos format 2) and kerning wrapped in extension lookups (type 9)
  are read too: `KernData` checks explicit pairs first (a zero pair is an exception that stops class
  kerning), then the class subtables in order, as OpenType applies them.
- GSUB is read only for single substitutions (lookup type 1) per feature tag, for `font-feature-settings`
  such as `zero`, `smcp`, `tnum`; ligature and contextual lookups belong to the complex-script shaper.
- CPAL palette 0 is stored BGRA. COLRv0 layers use palette index 0xFFFF for "current text colour";
  COLRv1 tables (version 1) are not read.

## EggPdf.Text/TrueType/TtfSubsetter.cs

- Glyphs reached only through GSUB (ligatures, conjuncts, reph forms, marks) are requested by glyph ID
  and painted through `OldToNewGlyphId`, and COLR layer glyphs are pulled in explicitly: neither is a
  composite component, so the subset would otherwise drop their outlines.
- hmtx keeps each glyph's real left side bearing: rasterizers place an outline at (xMin - lsb), so a zero
  lsb shifts glyphs whose outline doesn't start at x = 0, most visibly zero-width combining marks.
- The cmap uses format 4 (delta mapping only, so a segment must be contiguous in both code points and
  glyph IDs) unless a code point is outside the BMP, or the format 4 table would not fit its 16-bit
  length (thousands of separated code points); both cases use format 12.
- The subset keeps the font's `post` header (italic angle, underline, isFixedPitch) as version 3.0,
  without glyph names.

## EggPdf.Text/TrueType/SfntWriter.cs

- A rebuilt simple glyph is written with no hinting instructions: its outline was rebuilt (CFF conversion,
  variable-font instancing), so the original instructions no longer apply.
- `WriteFont` writes tables in ordinal (binary) tag order, so `OS/2` comes first, and sets
  `head.checkSumAdjustment` so the whole file sums to 0xB1B0AFBA. The subsetter, TTC face extraction and
  variable-font instancing all assemble fonts through it.
