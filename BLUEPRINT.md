# EggPdf - Pure C# HTML/CSS Rendering Engine for PDF

## Vision

A **general-purpose**, pure C# rendering engine that converts HTML + CSS to PDF with output quality matching **Chrome's Print-to-PDF** (targeting Chrome N-1 feature parity).

**Zero dependencies.** No WebKit, no Chromium, no Skia, no NuGet packages. 100% managed C#. Every byte of this library is ours.

**Target audience:** Any .NET developer who needs HTML-to-PDF. Published as a public NuGet package.

**Target quality:** If Chrome can print it, EggPdf can render it.

**Developer promise:** Write normal HTML and CSS. Use any framework (Bootstrap, Tailwind, custom). Use any language (English, Vietnamese, Japanese, Chinese, Arabic). Use any image format (PNG, JPEG, SVG, GIF, WebP). EggPdf handles it. No "supported subset" to memorize, no workarounds, no surprises.

---

## Why This Library Needs to Exist

The .NET ecosystem has no good pure-C# HTML-to-PDF solution:

| Existing Library | Problem |
|---|---|
| **Select.HtmlToPdf / wkhtmltopdf** | Embeds WebKit -- 50-100 MB native binaries, platform-specific |
| **Puppeteer/Playwright** | Requires Chrome/Chromium installed -- 300+ MB, not embeddable |
| **iTextSharp / iText7** | AGPL license (commercial license $$$). No HTML layout engine built-in |
| **PdfSharp / MigraDoc** | No HTML input. You build layouts in C# code, not from HTML |
| **QuestPDF** | Excellent fluent API, but no HTML input. Different paradigm |
| **Aspose.PDF** | Commercial ($$$). Huge binary. Inconsistent HTML rendering |

**EggPdf fills the gap:** Chrome-quality HTML rendering, pure C#, MIT-licensed, single small DLL, zero dependencies. Works everywhere -- .NET Framework 4.6.2 through .NET 9+.

---

## Reality Check: What We're Building

Chrome's rendering engine (Blink) is ~10 million lines of C++. We are NOT rebuilding a browser. We're building a **print rendering engine** -- a critical distinction:

| Browser Rendering | Print Rendering (EggPdf) |
|---|---|
| JavaScript execution | No JS -- static HTML only |
| Infinite scroll canvas | Fixed-size pages |
| Interactive (hover, click, scroll) | Static output |
| Animations, transitions | Not applicable |
| Video, audio | Not applicable |
| Network requests for resources | Optional: fetch external resources via built-in HTTP client or IResourceResolver |
| 60fps repaint | Single-pass render |

Even with these simplifications, this is a **large-scale systems project**. The layout engine alone is comparable in complexity to a compiler. This blueprint is designed for incremental delivery with value at each phase.

### Reference Projects (for architecture inspiration)

| Project | Language | What We Can Learn |
|---|---|---|
| **WeasyPrint** | Python | Architecture, CSS spec coverage, paged media handling |
| **litehtml** | C++ (~15K LOC) | Lightweight layout engine design, CSS 2.1 implementation |
| **Prince XML** | Commercial | Gold standard for HTML-to-PDF quality, spec coverage target |
| **Typst** | Rust | Modern layout engine design, performance patterns |
| **CsQuery / AngleSharp** | C# | HTML/CSS parsing in .NET (study their approach, but we build our own) |

---

## Lessons Learned from Other Engines

Research across WeasyPrint (Python), litehtml (C++), Prince XML (commercial), Typst (Rust), QuestPDF (.NET), PDFsharp (.NET), AngleSharp (.NET), and Servo (Rust) reveals consistent patterns:

### Architecture Patterns to Adopt

| Pattern | Source | How We Apply It |
|---|---|---|
| **8-stage pipeline** (parse -> style -> box-gen -> layout -> fragment -> paint -> serialize) | WeasyPrint, Servo | Our architecture mirrors this. Each stage is a pure transformation -- testable in isolation |
| **Region-based pagination** | Typst | Layout elements receive a sequence of regions (remaining space on current page, then full pages). Each element handles its own page-breaking by consuming regions. This is compositional -- new layout modes get pagination "for free" |
| **Three-state layout response** | QuestPDF, Typst | Layout boxes report: Fit (renders entirely), Split (renders partially, continue on next page), Skip (move entirely to next page). This drives automatic page breaking cleanly |
| **Self-serializing PDF objects** + central reference table | PDFsharp | Each PdfObject has a `WriteTo(PdfWriter)` method. PdfReferenceTable assigns IDs and tracks byte offsets. Two-phase: prepare, then serialize |
| **Tokenizer/tree-builder bidirectional coupling** | AngleSharp, WHATWG spec | The HTML5 spec requires the tree builder to switch tokenizer states (e.g., `<script>` enters Script Data state). Our tokenizer exposes a `SetState()` method called by the tree builder |
| **Pluggable rendering backend** (document_container pattern) | litehtml | Our paint layer emits abstract commands. PDF backend and raster backend (for testing) both consume them. litehtml proves this works: zero coupling to any graphics library |
| **Font subsetting with composite glyph resolution** | PDFsharp | When subsetting TrueType, composite glyphs (e.g., accented characters) reference component glyphs that must be pulled in transitively |
| **Introspection loop for circular dependencies** | Typst | Page counters (`counter(page)`, `counter(pages)`) depend on layout, but layout depends on counter content (size of "Page 1 of 12"). Solution: iterate layout up to N times until stable (Typst uses max 5 iterations) |
| **Pure infallible parser** | Typst | The HTML parser should never throw -- it produces error nodes in the DOM instead. This makes every input parseable and simplifies error handling throughout the pipeline |

### Hard Problems Others Have Struggled With (and how we prepare)

| Problem | Who Struggled | Impact | Our Mitigation |
|---|---|---|---|
| **Page breaks inside floats** | WeasyPrint (open since 2012, issue #36) | Floated content that exceeds page height gets clipped | Design fragmentation engine to handle all formatting contexts from the start, not just block containers |
| **Fragmentation in flex/grid containers** | WeasyPrint (issues #2076, #2397) | Content overflows and disappears | Implement fragmentation as a cross-cutting concern that each formatting context must support, not an afterthought |
| **Infinite loops from adversarial input** | WeasyPrint | Malicious HTML/CSS causes hangs, memory exhaustion | Add timeout guards and recursion depth limits in layout. Fuzz test from Phase 1 |
| **Table layout performance** | WeasyPrint | Large tables are slow, especially across pages | Profile table layout early. Consider streaming/incremental table layout for very large tables |
| **CSS cascade with large stylesheets** | WeasyPrint (slow with Bootstrap-size CSS) | Minutes-long render times | Use efficient selector matching (Bloom filter for fast rejection, like Servo). Index selectors by rightmost simple selector |
| **PNG alpha channel separation** | PdfSharpCore (issue #41) | Transparency renders as black | Explicitly separate alpha into SMask from day 1. Test with transparent PNGs early |

### What NOT to Do (anti-patterns observed)

| Anti-Pattern | Where Observed | Our Approach Instead |
|---|---|---|
| Delegating text shaping to native C libraries (Pango/HarfBuzz via FFI) | WeasyPrint | Pure C# text engine. Harder to build, but eliminates the native dependency that causes 80% of WeasyPrint's installation issues |
| Adding layout modes (flex, grid) without fragmentation support | WeasyPrint | Every formatting context implements a `Split()` method from the start, even if it's a no-op initially. Typst proves this works: region-based layout makes fragmentation compositional |
| Tight coupling between layout and PDF output | Various | Our paint layer is an explicit intermediate representation -- layout knows nothing about PDF. litehtml's `document_container` abstraction proves this: zero coupling to any graphics library |
| Using a general-purpose image library for all image handling | Various | JPEG is pass-through (DCTDecode). PNG we decode ourselves (it's just zlib + filtering). No need for a full image library |
| Single-pass layout for page counters | Various | Page counters create circular dependencies (counter text size affects layout, which affects page count). Typst solves this with an introspection loop (max 5 iterations). We adopt the same pattern |
| Testing PDF output directly | Typst | Typst explicitly does NOT test PDF export -- most bugs occur in earlier pipeline stages. We test each stage independently, with visual tests using the raster backend, not PDF round-trips |

---

## Architecture Overview

```
                         HTML String + CSS
                              |
                              v
                   +---------------------+
                   |   1. HTML Parser     |  HTML5 spec-compliant tokenizer + tree builder
                   +---------------------+
                              |
                          DOM Tree
                              |
                              v
                   +---------------------+
                   |   2. CSS Parser      |  CSS Syntax Level 3 tokenizer + parser
                   +---------------------+
                              |
                         CSSOM (rules)
                              |
                              v
                   +---------------------+
                   |  3. Style Resolver   |  Cascade, specificity, inheritance, computed values
                   +---------------------+
                              |
                      Styled DOM Tree
                              |
                              v
                   +---------------------+
                   |  4. Box Generation   |  DOM elements --> formatting boxes
                   +---------------------+    (handles display, ::before/::after, anonymous boxes)
                              |
                         Box Tree
                              |
                              v
                   +---------------------+
                   |  5. Layout Engine    |  THE CORE -- positions & sizes every box
                   +---------------------+    Block, Inline, Flex, Grid, Table, Positioned, Float
                              |
                      Layout Tree (with geometry)
                              |
                              v
                   +---------------------+
                   |  6. Fragmentation    |  Splits layout across pages
                   +---------------------+    @page rules, page breaks, orphans/widows
                              |
                      Paged Layout Tree
                              |
                              v
                   +---------------------+
                   |  7. Paint Layer      |  Converts boxes to drawing commands
                   +---------------------+    backgrounds, borders, text, images, shadows, etc.
                              |
                      Paint Command List
                              |
                              v
                   +---------------------+
                   |  8. PDF Backend      |  Serializes to PDF 1.7/2.0
                   +---------------------+    Objects, streams, fonts, images, compression
                              |
                          PDF Bytes
```

This 8-stage pipeline mirrors how real browsers work (Blink has: Parse -> Style -> Layout -> Paint -> Composite, we skip composite since PDF is not rasterized).

---

## CSS Specification Coverage Target

This is the core of what "Chrome Print parity" means. Below is the full matrix of CSS specifications we need to implement, categorized by priority.

### Tier 1: Must Have (Chrome print essentials)

| CSS Specification | Key Features | Complexity |
|---|---|---|
| **CSS 2.1 Visual Formatting Model** | Block/inline formatting contexts, containing blocks, stacking contexts | Very High |
| **CSS Box Model Level 3** | margin, padding, border, width, height, min/max, box-sizing | Medium |
| **CSS Display Level 3** | block, inline, inline-block, flex, grid, table, none, contents | High |
| **CSS Flexbox Level 1** | flex containers/items, direction, wrap, grow/shrink/basis, alignment | Very High |
| **CSS Grid Level 1** | grid template, auto-placement, spanning, named areas, fr unit | Very High |
| **CSS Table Level 3** | table layout algorithm, border-collapse, caption, colspan/rowspan | High |
| **CSS Positioned Layout Level 3** | relative, absolute, fixed, sticky, z-index, inset | High |
| **CSS Floats** | float left/right, clear, float interactions with BFC | High |
| **CSS Text Level 3/4** | text-align, text-indent, word-break, overflow-wrap, white-space, white-space-collapse, text-wrap-mode, text-wrap-style, text-transform, word-spacing, letter-spacing, tab-size, line-break (CJK line break strictness: auto/loose/normal/strict/anywhere), hanging-punctuation (for CJK typographic quality), hyphenate-character, hyphenate-limit-chars | High |
| **CSS Fonts Level 4** | font-family, weight, style, size, stretch, size-adjust, @font-face, system fonts, font-variant (caps, ligatures, numeric, east-asian, position, alternates, emoji), font-feature-settings, font-variation-settings (variable fonts), font-kerning, font-optical-sizing, font-synthesis (weight, style, small-caps, position), font-palette | High |
| **CSS Color Level 4** | hex (3/4/6/8 digit), rgb/rgba, hsl/hsla, hwb(), named colors (148 total), currentColor, transparent, opacity. **Color functions**: lab(), lch(), oklab(), oklch(), color() (sRGB, display-p3, a98-rgb, etc.), color-mix(), light-dark(). **System colors**: Canvas, CanvasText, LinkText, etc. | Medium |
| **CSS Backgrounds Level 3** | background-color, background-image (gradients), background-size/position/repeat, **multiple backgrounds** (layered), background-clip (including `text` for gradient text effects), background-origin, background-attachment (fixed/scroll -- fixed maps to page-fixed in print) | High |
| **CSS Borders Level 3** | border-radius, border-style (solid/dashed/dotted/double), border-image | Medium |
| **CSS Overflow Level 3** | overflow: hidden/visible, text-overflow: ellipsis | Medium |
| **CSS Lists Level 3** | list-style-type, list-style-position, counters, ::marker | Medium |
| **CSS Selectors Level 4** | Full selector engine (combinators, pseudo-classes, pseudo-elements, :nth-child, :not, :is, :where, :has) | High |
| **CSS Cascade Level 5** | !important, specificity, origin, layers (@layer) | Medium |
| **CSS Values Level 4** | **Absolute units**: px, cm, mm, Q, in, pc, pt. **Font-relative**: em, rem, ex, ch, cap, ic, lh, rlh. **Viewport**: vw, vh, vi, vb, vmin, vmax (+ small/large/dynamic variants: svw, lvh, dvw, etc.). **Container query**: cqw, cqh, cqi, cqb, cqmin, cqmax. **Flex**: fr. **%**. **Math functions**: calc(), min(), max(), clamp(), round(), mod(), rem(), abs(), sign(), sin(), cos(), tan(), asin(), acos(), atan(), atan2(), pow(), sqrt(), hypot(), log(), exp() | High |
| **CSS Custom Properties** | var(), inheritance, fallback values | Medium |
| **CSS Paged Media Level 3** | @page, page margins, page breaks, named pages | High |
| **CSS Fragmentation Level 3** | break-before/after/inside, orphans, widows, box-decoration-break | High |
| **CSS Generated Content** | ::before, ::after, content property, counters, quotes | Medium |
| **CSS `@media` (print)** | `@media print { ... }` -- **the single most important at-rule for us**. Our engine applies `@media print` rules by default and ignores `@media screen`-only rules. Also handle `@media (min-width)`, `@media (color)`, etc. | Medium |
| **CSS `@supports`** | Feature queries: `@supports (display: grid) { ... }`. Must evaluate against our own supported properties | Medium |
| **CSS `print-color-adjust`** | `print-color-adjust: exact` / `-webkit-print-color-adjust: exact`. Controls whether backgrounds/colors are preserved in print. Chrome **suppresses backgrounds by default** in print mode. We must respect this property | Low |
| **CSS `hyphens`** | `hyphens: auto` with `lang` attribute enables automatic hyphenation. Critical for justified text in print (avoids whitespace rivers). Requires language-specific hyphenation dictionaries | High |
| **CSS `visibility`** | `visibility: hidden` -- box is laid out but invisible (different from `display: none`). `visibility: collapse` for table rows/columns | Low |
| **CSS `vertical-align` (inline)** | `vertical-align: baseline/middle/super/sub/top/bottom/text-top/text-bottom/<length>/<percentage>` on inline elements and table cells | Medium |
| **CSS `line-height`** | Normal, `<number>`, `<length>`, `<percentage>`. Inherited. Affects inline box height and baseline spacing | Medium |
| **CSS Logical Properties Level 1** | `margin-block-start/end`, `margin-inline-start/end`, `padding-block/inline`, `border-block/inline`, `block-size`, `inline-size`, `min/max-block-size/inline-size`, `inset-block/inline`, `text-align: start/end`. Essential for RTL-friendly code. Chrome 87+ | High |
| **CSS Table (additional)** | `border-spacing`, `caption-side` (top/bottom), `empty-cells` (show/hide), `table-layout` (auto/fixed) | Medium |
| **CSS Text (additional)** | `text-align-last` (last line in justified), `text-justify` (inter-word/inter-character), `text-emphasis` (CJK emphasis dots), `text-underline-offset`, `text-underline-position` | Medium |
| **CSS Outline** | `outline`, `outline-style`, `outline-width`, `outline-color`, `outline-offset`. Does NOT affect layout (unlike border) | Low |
| **CSS `isolation`** | `isolation: isolate` -- creates new stacking context. Used in complex z-index scenarios | Low |
| **CSS `image-rendering`** | `image-rendering: pixelated / crisp-edges / auto`. Controls image scaling quality | Low |
| **CSS `image-orientation`** | `from-image` (respect EXIF) or `none`. Default: `from-image` in Chrome | Low |
| **CSS `paint-order`** | `normal / stroke / fill / markers`. Controls SVG/text paint order (stroke behind fill, etc.) | Low |
| **CSS `text-decoration` (full)** | `text-decoration-skip-ink: auto/none`, `text-decoration-thickness`, `text-decoration-style` (solid/double/dotted/dashed/wavy) | Medium |
| **CSS `shape-margin` / `shape-image-threshold`** | Fine-tune shape-outside float wrapping. Accompanies CSS Shapes Level 1 | Low |
| **CSS `contain-intrinsic-size`** | `contain-intrinsic-width/height/block-size/inline-size`. For content-visibility optimization | Low |
| **CSS `overflow-clip-margin`** | How far clipped content extends beyond the box before being visually clipped | Low |
| **CSS `offset-path`** (Motion Path) | `offset-path: path('M 0 0 L 100 100')`. Position elements along a path. Rare in print but Chrome supports it | Low |

### Tier 2: Should Have (common in modern web)

| CSS Specification | Key Features | Complexity |
|---|---|---|
| **CSS Transforms Level 1** | translate, rotate, scale, matrix, transform-origin | Medium |
| **CSS Box Shadow** | box-shadow (multiple, inset) | Medium |
| **CSS Text Decoration Level 3** | text-decoration (line, style, color), text-shadow, text-underline-offset, text-underline-position | Medium |
| **CSS Writing Modes Level 4** | writing-mode (horizontal-tb, vertical-rl, vertical-lr), direction, unicode-bidi, text-orientation, text-combine-upright | High |
| **CSS Multi-column Level 1** | column-count, column-width, column-gap, column-rule, column-span, column-fill | High |
| **CSS Object Fit/Position** | object-fit, object-position for replaced elements | Low |
| **CSS Shapes Level 1** | shape-outside for float wrapping | Medium |
| **CSS `initial-letter`** | Drop caps: `initial-letter: 3` makes first letter span 3 lines. Classic print typography feature (Chrome 110+) | Medium |
| **CSS `@counter-style`** | Define custom counter styles for list markers and counters. Custom numbering, emoji bullets, regional numbering systems (Arabic-Indic, CJK-decimal, Tamil, Thai, Devanagari, etc. from Unicode CLDR). Also `symbols()` function for simple inline counter definitions | Medium |
| **CSS Color Level 4 (advanced)** | `oklch()`, `oklab()`, `color()`, `color-mix()`, `light-dark()`, `transparent`, `color-scheme` | Medium |
| **CSS Math Functions** | `sin()`, `cos()`, `tan()`, `sqrt()`, `pow()`, `log()`, `exp()`, `round()`, `mod()`, `rem()`, `abs()`, `sign()` (Chrome 111+) | Medium |

### Tier 1.5: Chrome N-1 Modern CSS (added in recent Chrome versions)

These features are present in Chrome 130+ and increasingly used in production. Required for true Chrome N-1 parity.

| CSS Feature | Chrome Version | Key Features | Complexity |
|---|---|---|---|
| **CSS Nesting** | Chrome 120+ | Native nesting without preprocessors: `div { & p { color: red } }` | Medium |
| **CSS Container Queries** | Chrome 105+ | `@container` rules, container-type, container-name. Layout based on parent size, not viewport | High |
| **CSS `color-mix()`** | Chrome 111+ | `color-mix(in srgb, red 50%, blue)` -- blend two colors | Medium |
| **CSS `light-dark()`** | Chrome 123+ | `color: light-dark(black, white)` -- respond to color scheme. For print, always use light mode value | Low |
| **CSS `aspect-ratio`** | Chrome 88+ | `aspect-ratio: 16/9` on any element | Medium |
| **CSS Subgrid** | Chrome 117+ | `grid-template-rows: subgrid` -- child grid aligns to parent grid tracks | High |
| **CSS `@layer`** | Chrome 99+ | Cascade layers for style priority control | Medium (already in Tier 1) |
| **CSS `@scope`** | Chrome 118+ | Scoped styles: `@scope (.card) { p { color: red } }` | Medium |
| **CSS `:has()` selector** | Chrome 105+ | Parent selector: `div:has(> img)` | Medium (already in Tier 1) |
| **CSS `text-wrap: balance`** | Chrome 114+ | Balance text across lines (headings). For print: significant visual improvement | Medium |
| **CSS `lh` / `rlh` units** | Chrome 109+ | Line-height relative units | Low |
| **CSS `@property`** | Chrome 85+ | Typed custom properties with inheritance and initial values | Medium |
| **CSS `accent-color`** | Chrome 93+ | Style form control accents (for rendered form elements) | Low |

### CSS Properties Explicitly Ignored (not applicable to print)

These CSS properties are recognized by our parser (no "unknown property" warning) but produce no effect. They are interactive/screen-only features:

| Ignored Property | Reason |
|---|---|
| `animation`, `animation-*` (all sub-properties) | No animation in static PDF |
| `transition`, `transition-*` (all sub-properties) | No transitions in static PDF |
| `cursor` | No mouse cursor in PDF |
| `resize` | No interactive resizing |
| `scroll-behavior`, `scroll-snap-*`, `overscroll-*` | No scrolling in PDF |
| `user-select` | No text selection interaction (text is always selectable in PDF) |
| `pointer-events` | No pointer interaction |
| `touch-action` | No touch interaction |
| `will-change` | Performance hint for browsers only |
| `caret-color` | No text cursor in PDF |
| `nav-up/down/left/right` | No keyboard navigation |
| `ime-mode` | No input method editor |
| `font-display` | No async font loading (all fonts loaded synchronously) |
| `contain` | Layout containment optimization -- may implement for performance later, but not for correctness |
| `content-visibility` | Rendering optimization -- all content is always rendered in PDF |

### Tier 3: Nice to Have (advanced/rare in print)

| CSS Specification | Key Features | Complexity |
|---|---|---|
| **CSS Filter Effects** | filter: blur, brightness, contrast, grayscale, etc. | High |
| **CSS Masking Level 1** | clip-path, mask | High |
| **CSS Blend Modes** | mix-blend-mode, background-blend-mode | Medium |
| ~~**SVG Rendering**~~ | **Moved to Phase 9 (Must Have)** -- SVG is too common to defer | - |
| **MathML** | Mathematical notation | Very High |

---

## Multilingual and Unicode Support

**This is critical.** A general-purpose library must handle all human languages correctly. Chrome handles this natively -- we must match it.

### Language Coverage Matrix

| Language Group | Script | Key Challenges | Required Features |
|---|---|---|---|
| **Latin** (English, French, German, Spanish) | Latin | Straightforward. Ligatures (fi, fl), accented characters (e, u, o) | Basic font metrics + kerning |
| **Vietnamese** | Latin Extended | Extensive use of combining diacritical marks (a, ?, e, o). Multiple marks per character. Large glyph set | Full Unicode normalization (NFC). Font must contain Vietnamese glyphs. cmap format 4/12 for supplementary code points |
| **Thai** | Thai | **No spaces between words.** Requires dictionary-based word segmentation for line breaking. Complex vowel/tone mark positioning (above/below/around consonants) | Thai word segmentation dictionary (or ICU-based rules). OpenType GPOS for mark positioning. Cannot rely on UAX #14 alone for line breaks |
| **Japanese** | CJK (Kanji + Hiragana + Katakana) | CJK character width (full-width vs. half-width). Line break rules (no break before certain punctuation). Large fonts (~20K glyphs) | CJK line break rules (per JIS X 4051 / UAX #14). CIDFont Type 2 for PDF embedding. Font subsetting must handle large glyph counts efficiently |
| **Chinese** (Simplified + Traditional) | CJK (Hanzi) | Same as Japanese CJK. Plus: simplified vs. traditional variants need correct font | CJK font resolution. `lang` attribute guides font selection |
| **Korean** | CJK (Hangul) | Syllable block composition (~11K Hangul syllables). CJK punctuation rules | Hangul line break rules. CIDFont embedding |
| **Arabic** | Arabic | **RTL + complex shaping.** Characters change form based on position (initial, medial, final, isolated). Ligatures are mandatory (lam-alef) | Full Bidi algorithm (UAX #9). OpenType GSUB for contextual alternates. GPOS for mark positioning. RTL layout direction |
| **Hebrew** | Hebrew | RTL. Simpler shaping than Arabic (no mandatory ligatures) | Bidi algorithm (UAX #9). RTL layout |
| **Hindi / Devanagari** | Devanagari | Complex conjuncts (multiple consonants combine into ligature forms). Vowel marks reorder visually | OpenType GSUB/GPOS for complex shaping. Reordering rules |

### Implementation Strategy

**Phase 3 (Typography):**
- Full Unicode support for Latin, Vietnamese, CJK basic rendering
- Font fallback chain with CJK font detection
- UAX #14 line break algorithm with CJK rules
- cmap format 4 (BMP) and format 12 (supplementary planes)
- CIDFont Type 2 embedding for CJK fonts in PDF

**Phase 10 or dedicated phase:**
- Bidi algorithm (UAX #9) for Arabic, Hebrew, mixed LTR/RTL
- Thai word segmentation (dictionary-based or rule-based)
- Complex script shaping for Arabic, Hindi (requires GSUB/GPOS)
- Vertical writing modes (CJK vertical text)

### What We Must Build for CJK/Vietnamese

| Component | Details |
|---|---|
| **Unicode normalization** | NFC normalization for composed characters (Vietnamese, accented Latin) |
| **CJK line break rules** | No break before closing punctuation (。、）」), no break after opening punctuation (（「). Kinsoku Shori rules |
| **CJK width handling** | Full-width characters take 2x the width of half-width characters in the same line |
| **CJK font auto-detection** | When a CJK codepoint is encountered and the current font lacks the glyph, auto-select a CJK system font (Noto Sans CJK, MS Gothic, PingFang, etc.) |
| **CIDFont embedding** | CJK fonts embedded as CIDFont Type 2 in PDF with Identity-H encoding. CMap for glyph mapping |
| **Large font subsetting** | CJK fonts can be 10-20MB. Subsetting to only used glyphs is essential. Must handle 20K+ glyph fonts efficiently |
| **Thai word segmentation** | Build or embed a Thai word break dictionary (~40K words). Fall back to character-level breaks if dictionary unavailable |

### CJK/Unicode Test Strategy

```
tests/EggPdf.Tests.Unit/Text/
|-- UnicodeNormalizationTests.cs    # NFC normalization
|-- CjkLineBreakTests.cs           # Kinsoku Shori rules
|-- ThaiWordBreakTests.cs           # Dictionary-based segmentation
|-- VietnameseRenderTests.cs        # Combining diacritical marks
|-- BidiAlgorithmTests.cs           # UAX #9 test cases
|-- CjkFontFallbackTests.cs        # Auto-select CJK font

tests/EggPdf.Tests.Integration/E2E/
|-- MultilangTests.cs               # Full render tests for each language
    # - Vietnamese invoice
    # - Japanese article
    # - Chinese document
    # - Arabic paragraph (RTL)
    # - Mixed language (English + Chinese + Thai in one document)
```

---

## HTML Element Coverage

### Full Support Required
- **Document:** html, head, body, title, style, link (stylesheet), meta
- **Sections:** div, section, article, aside, header, footer, nav, main, h1-h6, p, hr
- **Grouping:** ul, ol, li, dl, dt, dd, blockquote, pre, figure, figcaption
- **Text:** span, a, strong/b, em/i, u, s/del, ins, small, sub, sup, br, wbr, code, kbd, samp, var, mark, abbr, q, cite, data, time, address, output, dfn, bdi, bdo, hgroup, search
- **Interactive:** details (render open), summary, dialog (render if `open` attribute)
- **Semantic (display as block/inline per UA stylesheet):** menu (as ul), meter (as inline progress bar), progress (as inline progress bar), datalist (hidden by default)
- **Tables:** table, thead, tbody, tfoot, tr, th, td, caption, colgroup, col
- **Embedded:** img, picture, source (responsive images -- select best source based on media/type)
- **Ruby annotations:** ruby, rt, rp (CJK pronunciation guides above characters)
- **Forms (display only):** input, textarea, select, button, label, fieldset, legend (rendered as static, not interactive)
- **Generated:** ::before, ::after

### Full Support Required (continued)
- **SVG:** Inline `<svg>` elements AND `<img src="file.svg">`. SVG is ubiquitous in modern HTML (icons, charts, logos, diagrams). **This is not optional.**

### Image Format Coverage (all HTML-supported formats)

| Format | HTML Usage | PDF Embedding | Complexity | Phase |
|---|---|---|---|---|
| **JPEG** (.jpg) | `<img>`, `background-image` | Pass-through (DCTDecode) -- no re-encoding | Trivial | Phase 9 |
| **PNG** (.png) | `<img>`, `background-image` | Decode, separate alpha to SMask, FlateDecode RGB | Medium | Phase 9 |
| **GIF** (.gif) | `<img>` (common: badges, icons, legacy) | Decode first frame, palette -> RGB, FlateDecode | Medium | Phase 9 |
| **SVG** (.svg) | `<img src>`, inline `<svg>`, `background-image` | Render SVG to paint commands, then to PDF vector operations. SVG stays as vectors in PDF (no rasterization) | Very High | Phase 9 |
| **WebP** (.webp) | `<img>` (increasingly common, default in Chrome) | Decode to RGB/RGBA, embed as FlateDecode + SMask | High | Phase 10 |
| **AVIF** (.avif) | `<img>` (newer Chrome default) | Decode to RGB/RGBA | Very High | Future |
| **ICO** (.ico) | `<link rel="icon">` (rarely in body, but supported) | Extract largest PNG/BMP frame | Low | Future |
| **BMP** (.bmp) | `<img>` (legacy) | Decode to RGB, FlateDecode | Low | Phase 9 |
| **Base64 data URIs** | `<img src="data:image/png;base64,...">` | Detect format from MIME type, decode accordingly | Medium | Phase 9 |

**SVG rendering** is a sub-engine within EggPdf. Key SVG elements to support:

| SVG Element | Purpose | Priority |
|---|---|---|
| `<svg>`, `<g>`, `<defs>`, `<use>` | Structure and reuse | Must |
| `<rect>`, `<circle>`, `<ellipse>`, `<line>`, `<polyline>`, `<polygon>` | Basic shapes | Must |
| `<path>` | Complex shapes (bezier curves, arcs) | Must |
| `<text>`, `<tspan>` | Text in SVG | Must |
| `<image>` | Embedded images within SVG | Must |
| `<clipPath>`, `<mask>` | Clipping and masking | Should |
| `<linearGradient>`, `<radialGradient>` | Gradient fills | Should |
| `<pattern>` | Pattern fills | Nice to have |
| `<filter>` | SVG filters (blur, etc.) | Nice to have |
| `<foreignObject>` | HTML inside SVG | Nice to have |
| `<symbol>`, `<marker>` | Reusable symbols | Should |
| `viewBox`, `preserveAspectRatio` | Scaling and positioning | Must |
| CSS styling of SVG | `fill`, `stroke`, `stroke-width`, `opacity`, `transform` | Must |

SVG output in PDF should be **vector, not rasterized** -- this preserves quality at any zoom level and keeps file size small.

### HTML Presentational Attributes (Legacy but Common)

Legacy HTML attributes that map to CSS properties. Must be handled as "presentational hints" in the cascade (lower priority than any stylesheet):

| Attribute | Elements | Maps To CSS |
|---|---|---|
| `width`, `height` | `<img>`, `<table>`, `<td>`, `<th>`, `<col>`, `<colgroup>`, `<iframe>` | `width`, `height` |
| `border` | `<table>`, `<img>` | `border-width`, `border-style` |
| `cellpadding` | `<table>` | `padding` on cells |
| `cellspacing` | `<table>` | `border-spacing` |
| `bgcolor` | `<body>`, `<table>`, `<tr>`, `<td>`, `<th>` | `background-color` |
| `color` | `<font>`, `<hr>` | `color` |
| `face` | `<font>` | `font-family` |
| `size` | `<font>` | `font-size` (1-7 scale) |
| `align` | `<p>`, `<div>`, `<h1>`-`<h6>`, `<table>`, `<td>`, `<th>`, `<tr>`, `<img>` | `text-align`, `margin: 0 auto` (center), `float` (left/right on img) |
| `valign` | `<td>`, `<th>`, `<tr>`, `<tbody>`, `<thead>`, `<tfoot>` | `vertical-align` |
| `nowrap` | `<td>`, `<th>` | `white-space: nowrap` |
| `clear` | `<br>` | `clear` |
| `hspace`, `vspace` | `<img>` | `margin` |
| `noshade` | `<hr>` | border style |
| `start` | `<ol>` | `counter-reset` value |
| `type` | `<ol>`, `<ul>` | `list-style-type` |
| `hidden` | Any element | `display: none` -- element MUST NOT be rendered. HTML global attribute |
| `dir` | Any element | `direction` (ltr/rtl/auto). `auto` requires first-strong character detection (UAX #9) |

These attributes are still extremely common in email HTML, CMS output, legacy documents, and WYSIWYG editor output. **100% support means handling them all.**

### Special Element Handling

| Element | Behavior in Print |
|---|---|
| `<script>` | Content NOT rendered (text inside `<script>` is invisible). Tag is parsed for tree-builder state switching but produces no box |
| `<noscript>` | Content IS rendered (we don't execute JS, so noscript content is shown -- same as Chrome with JS disabled) |
| `<template>` | Content NOT rendered (inert element per HTML spec). Template content exists in DOM but generates no boxes |
| `<iframe>` | Renders as empty replaced element with border (like a placeholder box). We do NOT fetch or render iframe content. Optional: show `src` URL as text inside |
| `<dialog>` | Rendered if `open` attribute is present. Otherwise hidden. Chrome print behavior |
| `<video>` / `<audio>` | Render `poster` image if available, otherwise empty placeholder box with controls-like appearance |
| `<canvas>` | Empty box (requires JS to draw content -- we can't execute JS) |
| `<object>` / `<embed>` | Empty placeholder box |

### CSS Pseudo-Class Behavior in Print (Static Document)

Since we render a static document (no user interaction), pseudo-classes behave differently:

| Pseudo-Class | Matches? | Reason |
|---|---|---|
| `:link` | Yes -- all `<a>` with `href` | Unvisited link styling |
| `:visited` | Never | No browsing history. Privacy protection |
| `:hover` | Never | No mouse interaction |
| `:focus` | Never | No keyboard interaction |
| `:active` | Never | No click interaction |
| `:checked` | Based on HTML `checked` attribute | Static state from markup |
| `:disabled` / `:enabled` | Based on HTML `disabled` attribute | Static state from markup |
| `:target` | Never | No URL fragment |
| `:placeholder-shown` | Based on whether `<input>` has a value | Static state |

### Deprecated HTML Elements (Chrome still renders these -- we must too)

Legacy elements still common in real-world HTML (emails, CMS output, old websites):

| Element | Rendering | Maps To |
|---|---|---|
| `<center>` | Block, `text-align: center` | `div` with centered text |
| `<font color="..." size="..." face="...">` | Inline with color/size/font-family | `span` with styles |
| `<big>` | Inline, `font-size: larger` | `span` |
| `<tt>` | Inline, monospace font | `code` |
| `<strike>` | Inline, `text-decoration: line-through` | `s` |
| `<nobr>` | Inline, `white-space: nowrap` | `span` |
| `<marquee>` | Block, static content (no scrolling in print) | `div` |
| `<acronym>` | Same as `<abbr>` | `abbr` |
| `<dir>` | Same as `<ul>` | `ul` |
| `<xmp>` | Preformatted, monospace (like `<pre>`) | `pre` |
| `<plaintext>` | Preformatted, monospace | `pre` |

### Additional CSS Pseudo-Classes (for form state rendering)

| Pseudo-Class | Matches In Print | Usage |
|---|---|---|
| `:any-link` | All `<a>` with `href` (same as `:link` for us) | Styling all links |
| `:read-only` / `:read-write` | Based on `readonly` attribute | Form field styling |
| `:required` / `:optional` | Based on `required` attribute | Form field styling |
| `:valid` / `:invalid` | Based on HTML validation attributes (`type`, `pattern`, `min`, `max`) | Form field styling |
| `:in-range` / `:out-of-range` | Based on `min`/`max` attributes on `<input type="number">` | Form field styling |
| `:default` | First `<button>` in a form, `<option>` with `selected`, `<input>` with `checked` | Default element styling |
| `:indeterminate` | `<input type="checkbox">` with `indeterminate` attribute | Tri-state checkboxes |
| `:placeholder-shown` | `<input>` with no value (showing placeholder text) | Form field styling |
| `:autofill` | Never (no browser autofill in PDF) | Ignored |
| `:dir(ltr)` / `:dir(rtl)` | Based on computed text direction | RTL styling |
| `:lang(en)` / `:lang(vi)` | Based on `lang` attribute | Language-specific styling |
| `:open` | `<details>` with `open` attribute, `<dialog>` with `open` attribute | Interactive element state |
| `:defined` | Always true (all standard elements are defined) | Custom elements |

### Display Only (not interactive)
- **Media:** video, audio (render poster frame or placeholder)
- **Canvas:** Not supported (requires JS)

---

## Core Components Deep Dive

### Component 1: HTML Parser

**Goal:** Full HTML5 parsing per the WHATWG spec. Built from scratch -- zero dependencies.

We implement the complete HTML5 parsing algorithm ourselves:
- **HTML5 tokenizer** -- state machine with ~80 states per WHATWG spec
- **Tree builder** -- insertion modes, foster parenting, formatting element list, adoption agency algorithm
- **Error recovery** -- malformed HTML must still produce a valid DOM (per spec). Parser never throws -- produces error nodes instead (infallible parser pattern from Typst)
- **DOM** -- lightweight DOM tree (Document, Element, Text, Comment nodes)
- **HTML entity decoding** -- all ~2,231 named entities (`&amp;`, `&lt;`, `&hearts;`, etc.), numeric entities (`&#65;`, `&#x41;`), built-in lookup table
- **Encoding detection** -- BOM sniffing, `<meta charset="...">`, `<meta http-equiv="Content-Type">`, default to UTF-8
- **`<style>` in `<body>`** -- handled per HTML5 spec (common in email templates, CMS output)
- **`<base href>`** -- resolve relative URLs in the document
- Estimated ~6,000-10,000 lines of code

This is a significant investment, but gives us:
- Zero supply chain risk (critical for a general-purpose library)
- Full control over memory allocation (reuse buffers, span-based parsing)
- Tight integration with our CSS parser and style resolver
- No version conflicts for consumers who also use AngleSharp or other parsers

### Component 2: CSS Parser

**Goal:** Parse CSS per CSS Syntax Module Level 3.

**Build vs. Buy Decision:** Less mature C# options exist. Worth building ourselves for tight integration with our value system and to control memory allocation.

Key classes:
| Class | Responsibility |
|---|---|
| `CssTokenizer` | CSS Syntax Level 3 tokenizer (ident, function, at-keyword, hash, string, number, dimension, etc.) |
| `CssParser` | Parses token stream into stylesheet structure (rules, declarations, at-rules) |
| `CssStyleSheet` | Ordered collection of rules |
| `CssStyleRule` | Selector list + declaration block |
| `CssAtRule` | @media, @page, @font-face, @layer, @import handling |
| `CssSelectorParser` | Parses selector strings into structured selector trees |
| `CssValueParser` | Parses property values into typed representations (lengths, colors, functions, etc.) |
| `CssShorthandExpander` | Expand all CSS shorthands to longhands (see below) |

**CSS shorthand expansion** -- critical and often underestimated. Every shorthand must be expanded to its constituent longhands:

| Shorthand | Expands To |
|---|---|
| `margin: 10px 20px` | margin-top, margin-right, margin-bottom, margin-left |
| `padding: 5px` | padding-top, padding-right, padding-bottom, padding-left |
| `border: 1px solid red` | border-width, border-style, border-color (x4 sides) |
| `font: bold 14px/1.5 Arial` | font-style, font-variant, font-weight, font-size, line-height, font-family |
| `background: url(...) no-repeat center/cover` | background-image, background-repeat, background-position, background-size, background-color, etc. |
| `flex: 1 0 auto` | flex-grow, flex-shrink, flex-basis |
| `grid-template`, `grid-area`, `grid`, `place-items`, `place-content`, `place-self`, `gap` | Multiple grid longhands |
| `list-style`, `outline`, `overflow`, `text-decoration`, `transition`, `animation` | Respective longhands |

There are 50+ shorthands in CSS. Each has unique parsing rules. This is a discrete subsystem.

**CSS-wide keywords** (apply to every property):
- `inherit` -- use parent's computed value
- `initial` -- use the property's spec-defined initial value
- `unset` -- inherit if inheritable, initial otherwise
- `revert` -- roll back to previous cascade origin (user-agent if in author stylesheet)
- `revert-layer` -- roll back to previous cascade layer
- `all` shorthand -- `all: unset` / `all: initial` / `all: revert` resets all properties at once

**@font-face details**:
- `src: url()` -- resolve via IResourceResolver (local files, data URIs, custom resolvers)
- `src: local()` -- match against system fonts by name
- `unicode-range` -- only use this font for specific Unicode ranges (e.g., `U+4E00-9FFF` for CJK). Critical for multilingual documents that use different fonts per script
- `font-display` -- ignored (not relevant for non-interactive rendering)

**Graceful degradation**: Unknown properties are silently ignored (per CSS error handling spec). Unknown values fall back to the property's initial value. This is mandatory for forward-compatibility -- HTML may contain CSS features we haven't implemented yet.

**External stylesheets**: `<link rel="stylesheet" href="...">` and `@import url(...)` are resolved via `IResourceResolver`. The resolver loads the CSS text, which is then parsed and added to the stylesheet collection. **Circular `@import` detection**: track imported URLs and break cycles (an import that references an already-imported stylesheet is silently skipped).

### Component 3: Selector Engine

Full CSS Selectors Level 4 implementation. This is a discrete subsystem.

| Feature | Examples |
|---|---|
| Type selectors | `div`, `p`, `*` |
| Class/ID | `.foo`, `#bar` |
| Attribute | `[href]`, `[type="text"]`, `[class~="foo"]` |
| Combinators | `A B` (descendant), `A > B` (child), `A + B` (adjacent), `A ~ B` (sibling) |
| Pseudo-classes | `:first-child`, `:last-child`, `:nth-child(An+B)`, `:not()`, `:is()`, `:where()`, `:has()`, `:empty`, `:root` |
| Pseudo-elements | `::before`, `::after`, `::first-line`, `::first-letter`, `::marker`, `::placeholder` (for form inputs showing placeholder text when no value) |

### Component 4: Style Resolution (Cascade)

Implements the full CSS cascade algorithm:

1. **Collect** all rules whose selectors match the element
2. **Sort** by origin (user-agent < author < author !important), @layer order, specificity, source order
3. **Cascade** to find winning value per property
4. **Inherit** inheritable properties from parent (color, font-*, line-height, text-*, etc.)
5. **Default** unset properties to their initial values
6. **Resolve** relative values: em/rem to px, percentages to absolute, calc() evaluation, var() substitution
7. **Compute** final "computed values" for each property

This produces a `ComputedStyle` object per element with ~200+ resolved CSS properties.

**User-agent stylesheet**: We ship a built-in default stylesheet matching Chrome's print defaults. This includes:
- Default margins/padding on `body`, `h1`-`h6`, `p`, `ul`, `ol`, `blockquote`, `pre`, `hr`, etc.
- Default `display` values (block for `div`, inline for `span`, table for `table`, etc.)
- Default font sizes for headings (h1=2em, h2=1.5em, h3=1.17em, h4=1em, h5=0.83em, h6=0.67em)
- Default monospace font for `code`, `pre`, `kbd`, `samp`
- Default list styles (`ul` = disc, `ol` = decimal)
- Default table border-collapse, fieldset border, hr styling
- Form element defaults (input borders, button styling, select appearance)
- `a { color: blue; text-decoration: underline; }` (print default -- no purple for visited)
- `var { font-style: italic; }`, `samp { font-family: monospace; }`, `address { font-style: italic; display: block; }`
- `mark { background-color: yellow; color: black; }`, `abbr[title] { text-decoration: underline dotted; }`
- `::placeholder { color: #757575; opacity: 1; }` (gray placeholder text in form inputs)
- `[hidden] { display: none !important; }` (HTML `hidden` attribute)
- The UA stylesheet is inspectable via `EggPdf.Css.UserAgentStyleSheet.Default` for debugging

### Component 5: Box Generation

Transforms styled DOM into formatting boxes per CSS 2.1 Section 9.2:

- `display: block` --> block-level box
- `display: inline` --> inline-level box
- `display: flex` --> flex container box
- `display: grid` --> grid container box
- `display: table` --> table wrapper box
- `display: none` --> no box generated
- `display: contents` --> children promoted to parent
- **Anonymous box generation** -- wrapping inline content in anonymous block boxes where needed
- **::before / ::after** --> generated content boxes inserted into the tree

### Component 6: Layout Engine

**This is 60% of the total project effort.**

The layout engine is a tree of **formatting contexts**, each implementing a different layout algorithm:

#### 6a. Block Formatting Context (BFC)
- Lay out block-level children top-to-bottom
- Resolve widths from containing block
- Margin collapsing (adjacent, parent-child, empty blocks)
- Contain floats
- Establish new BFC for overflow:hidden, display:flow-root, flex/grid items, etc.

#### 6b. Inline Formatting Context (IFC)
- Collect inline-level boxes and text into **line boxes**
- **HTML whitespace normalization** (per CSS `white-space` property):
  - `normal` / `nowrap`: collapse consecutive whitespace to single space. Strip leading/trailing whitespace in block containers. Convert newlines to spaces
  - `pre` / `pre-wrap` / `pre-line`: preserve whitespace and/or newlines as specified
  - `break-spaces`: like `pre-wrap` but spaces at end of line are not collapsed
  - Tab characters expanded to next tab stop per `tab-size` property (default 8)
- Text shaping: measure glyphs, handle kerning
- Line breaking: Unicode Line Break Algorithm (UAX #14) + CSS word-break/overflow-wrap
- Vertical alignment within line boxes (baseline, top, middle, sub, super, text-top, text-bottom, `<length>`, `<percentage>`)
- Handle replaced inline elements (img)
- `text-align-last` for last line of justified paragraphs

#### 6c. Flex Layout (CSS Flexbox Level 1)
Full implementation of the flex layout algorithm (CSS spec Section 9):
1. Determine available main/cross space
2. Collect flex items, determine flex base size
3. Resolve flexible lengths (grow/shrink)
4. Determine hypothetical main size
5. Collect items into flex lines (wrapping)
6. Resolve cross sizes
7. Align items (align-items, align-self, justify-content)
8. Handle `order` property

#### 6d. Grid Layout (CSS Grid Level 1)
Full implementation of the grid layout algorithm:
1. Establish the explicit grid (grid-template-rows/columns, grid-template-areas)
2. Place explicitly positioned items
3. Auto-place remaining items
4. Size grid tracks (min/max-content, fr units, minmax())
5. Resolve intrinsic track sizes
6. Align items and tracks (align/justify-items, align/justify-content, gaps)

#### 6e. Table Layout
- Table formatting context per CSS 2.1 Section 17
- Fixed and auto table layout algorithms
- Column width distribution
- Row height calculation
- Spanning cells (colspan, rowspan)
- Border collapsing model + separated borders model
- `border-spacing` property
- `caption-side: top | bottom`
- `empty-cells: show | hide`
- Caption placement
- **Repeating table headers/footers across pages**: `<thead>` re-rendered at the top of each page when a table spans multiple pages. `<tfoot>` re-rendered at the bottom. This is one of the most requested features in PDF generation -- essential for any multi-page table (invoices, reports, ledgers). Chrome does this in print
- `<colgroup>` / `<col>` width distribution via `width` attribute and `column-width` CSS

#### 6f. Float Layout
- Float positioning (left/right)
- Line box shortening around floats
- Clear property
- Float interaction with BFC

#### 6g. Positioned Layout
- Relative: offset from normal flow position
- Absolute: positioned relative to containing block (nearest positioned ancestor)
- Fixed: positioned relative to page box (in paged media)
- Stacking contexts and z-index ordering

#### 6h. Intrinsic Sizing
Critical for all layout modes:
- **min-content** width: narrowest an element can be without overflow
- **max-content** width: widest an element wants to be (no wrapping)
- **fit-content**: clamp(min-content, available, max-content)
- Recursive: parent layout depends on children's intrinsic sizes

### Component 7: Text Engine

Text is deceptively complex:

| Capability | Details |
|---|---|
| Font resolution | Match font-family stack against available fonts (system + @font-face) |
| Font fallback chain | When a glyph isn't in the primary font, try next font in stack, then system fallback. Essential for multilingual content (e.g., CJK characters in a Latin document) |
| Bold/italic synthesis | If bold variant doesn't exist, synthesize via stroke width increase. If italic doesn't exist, synthesize via oblique transform (skew). Chrome does this |
| Font metrics | Parse TrueType/OpenType tables (head, hhea, hmtx, OS/2, cmap, kern, GPOS) |
| Glyph mapping | Unicode codepoint --> glyph ID via cmap table |
| Text measurement | Glyph advance widths + kerning pairs for accurate layout |
| Line breaking | Implement UAX #14 (Unicode Line Break Algorithm) + CSS word-break/overflow-wrap overrides |
| Bidi text | Unicode Bidirectional Algorithm (UAX #9) for mixed LTR/RTL content |
| Font subsetting | Extract only used glyphs for PDF embedding (reduces file size). Handle composite glyphs transitively |
| ToUnicode CMap | Map glyph IDs back to Unicode for text extraction/copy from PDF |
| WOFF decoding | WOFF 1.0 is zlib-compressed TrueType/OpenType. Decompress to get raw font data. Required for most @font-face usage |
| WOFF2 decoding | WOFF 2.0 uses Brotli compression. More complex -- defer to later phase or document as limitation |
| OpenType/CFF | Some .otf fonts use CFF (PostScript) outlines. Detect and handle or fall back to another font |

**System font discovery** (cross-platform):

| Platform | Font Directories |
|---|---|
| Windows | `C:\Windows\Fonts`, `%LOCALAPPDATA%\Microsoft\Windows\Fonts` (user-installed) |
| macOS | `/System/Library/Fonts`, `/Library/Fonts`, `~/Library/Fonts` |
| Linux | `/usr/share/fonts`, `/usr/local/share/fonts`, `~/.fonts`, `~/.local/share/fonts` + fontconfig config |

Font resolution order: user-provided fonts (PdfOptions.Fonts) > @font-face > system fonts > built-in PDF fonts (Helvetica, Times, Courier)

**Variable font support:**
- `font-variation-settings` -- direct axis control (e.g., `"wght" 650, "wdth" 80`)
- `font-optical-sizing: auto` -- automatic optical size axis adjustment
- Standard variation axes: wght (weight), wdth (width), ital (italic), slnt (slant), opsz (optical size)
- Variable fonts use a single .ttf file for all weights/widths -- significant file size savings
- Parse `fvar`, `gvar`, `STAT`, `avar` tables for variable font support

**OpenType features to support:**
- Kerning (kern table or GPOS)
- Ligatures (GSUB) -- at minimum fi, fl, ff, ffi, ffl

**Emoji rendering:**

Emoji are increasingly common in business documents (Slack exports, chat transcripts, customer feedback, marketing materials, modern reports). A best-in-class library must render them.

| Emoji Font Format | Details | Priority |
|---|---|---|
| **COLR/CPAL** (vector color) | Modern vector color outlines. Used by Noto Color Emoji, Segoe UI Emoji (Windows 11). Resolution-independent. Renders as colored vector paths in PDF | Must (primary) |
| **CBDT/CBLC** (bitmap color) | Color bitmap glyphs at fixed sizes. Used by older Android emoji fonts. Embed as image XObjects in PDF | Should |
| **sbix** (Apple bitmap) | Apple Color Emoji format. Bitmap glyphs at multiple sizes | Should (macOS) |
| **SVG-in-OpenType** (SVG table) | SVG documents as glyph outlines. Used by some fonts | Nice to have |

Emoji detection and rendering:
1. When text contains emoji codepoints (U+1F600-1F64F, U+1F300-1F5FF, U+2600-26FF, etc.) and the current font lacks those glyphs
2. Auto-fallback to a color emoji font: Noto Color Emoji (cross-platform, open license), Segoe UI Emoji (Windows), Apple Color Emoji (macOS)
3. For COLR/CPAL: render colored vector layers per glyph
4. For bitmap formats: extract bitmap at largest available size, embed as inline image in PDF
5. Ship **Noto Color Emoji** as an optional embedded resource (~10MB) so emoji work even on servers with no emoji fonts installed. Configurable: `PdfOptions.Fonts.EmbedEmojiFont = true`

### Component 8: Fragmentation Engine (Pagination)

Splits the continuous layout into discrete pages:

- **@page rules**: page size, margins, named pages
- **Page margin boxes**: @top-center, @bottom-right, etc. (for headers/footers)
- **Break properties**: break-before, break-after, break-inside (avoid/auto/page)
- **Orphans/widows**: minimum lines before/after a page break
- **Forced breaks**: page-break-before: always
- **Unbreakable content**: images, table rows (avoid splitting mid-row)
- **Fragmented boxes**: box-decoration-break (clone vs. slice) for borders/backgrounds across pages
- **Region-based approach** (learned from Typst): each layout element receives a sequence of available regions (remaining space + subsequent full pages). This makes pagination compositional -- every layout mode handles its own splitting

**Paged media features** (learned from Prince XML -- the gold standard):
- **Named pages**: `page: chapter` with automatic breaks between different page names
- **Page selectors**: `:first`, `:left`, `:right`, `:blank`, `:nth(An+B)`
- **16 page margin boxes**: @top-left, @top-center, @top-right, @bottom-left, etc.
- **Running headers/footers**: `position: running(name)` + `content: element(name)`
- **Page counters**: `counter(page)`, `counter(pages)` -- requires introspection loop (layout iterates until counters stabilize, max 5 passes)
- **Footnotes**: `float: footnote` (CSS GCPM spec) -- element moves to page bottom with auto call marker
- **Page floats**: float elements to page top/bottom

### Component 9: Paint Layer

Converts layout boxes into an ordered list of paint commands:

Paint order per CSS 2.1 Appendix E:
1. Background and borders of the element
2. Negative z-index children
3. Block-level in-flow children (non-inline)
4. Float children
5. Inline-level in-flow children (text, inline boxes)
6. Positioned children (z-index: auto and z-index: 0)
7. Positive z-index children

Paint operations needed:
- **Rectangles**: backgrounds, borders (with radius)
- **Text runs**: positioned glyphs with font, size, color
- **Images**: placed at computed positions/sizes
- **Gradients**: linear-gradient, radial-gradient
- **Shadows**: box-shadow (with blur radius -- may need Gaussian blur in PDF)
- **Clipping**: overflow: hidden regions, clip-path
- **Transforms**: translate/rotate/scale (affects coordinate system)
- **Opacity**: group opacity via PDF transparency groups

### Component 10: PDF Backend

Low-level PDF generation covering all business use cases:

#### 10a. Core PDF Engine

| Subsystem | Details |
|---|---|
| Object model | All PDF object types: dictionaries, arrays, streams, names, strings, numbers, booleans, references |
| Cross-reference table | Byte offsets for random access. Support both classic xref tables and PDF 1.5+ cross-reference streams (compressed) |
| Content streams | Page drawing operators (text, graphics, images, state management) |
| Resource management | Fonts, images, graphics states as shared resources |
| Compression | Flate (zlib) compression for streams. Object streams for smaller file sizes |
| Font embedding | TrueType/OpenType subset embedding with CIDFont + ToUnicode CMap. Subset prefix: `ABCDEF+FontName` |
| Image embedding | JPEG (DCTDecode pass-through), PNG (FlateDecode + SMask), GIF, BMP |
| Color spaces | DeviceRGB, DeviceGray, DeviceCMYK, ICCBased (ICC profiles), Separation (spot colors), DeviceN (multi-spot) |
| Transparency | ExtGState: `/CA` (stroke opacity), `/ca` (fill opacity), `/BM` (blend mode). Transparency group XObjects |
| Vector graphics | Full path operators: `m` (moveto), `l` (lineto), `c` (curveto), `re` (rectangle), `S`/`f`/`B` (stroke/fill/both), `W` (clip) |

#### 10b. Navigation and Structure

| Subsystem | Details |
|---|---|
| Hyperlinks | Link annotations: URI actions for external `<a href="https://...">`, GoTo actions for internal `<a href="#section">` |
| Bookmarks / Outlines | Auto-generate hierarchical outline tree from `<h1>`-`<h6>`. Each outline item has title + destination |
| Named destinations | `/Names` -> `/Dests` name tree for internal cross-references. TOC links, footnote refs, etc. |
| **Page labels** | `/PageLabels` number tree for different numbering per section: lowercase Roman (i, ii, iii), decimal (1, 2, 3), uppercase Roman, letters. Prefix strings (e.g., "A-" for appendix). Configurable via `PdfOptions.PageLabels` or CSS `@page` named pages |
| **Document language** | `/Lang` entry on document catalog (e.g., `en-US`, `vi-VN`, `ja-JP`). Per-element language override on structure elements for multilingual documents. Required for PDF/UA |
| Tab order | `/Tabs /S` on page dictionaries -- tab order follows structure tree for accessibility |
| **Viewer preferences** | `/ViewerPreferences` dict: page layout (single page, two-up, continuous), hide toolbar/menubar, fit window, center window, display document title in title bar, non-fullscreen page mode. Configurable via `PdfOptions.ViewerPreferences` |
| **Open action** | What happens when PDF opens: go to page 1, fit width, zoom to specific level. `/OpenAction` in document catalog. Default: fit page width |
| **PDF version selection** | Users choose PDF version: 1.4 (max compatibility), 1.5 (object streams), 1.7 (most features), 2.0 (latest). Default: 1.7. Some features auto-upgrade version (e.g., transparency requires 1.4+, OCGs require 1.5+) |
| **Linearized PDF** | Reorganize PDF structure for byte-serving: first page loads immediately while rest downloads in background. Critical for web-served large PDFs. Adds hint tables and linearization parameter dictionary. Optional via `PdfOptions.Linearize = true` |

#### 10c. Metadata and Compliance

| Subsystem | Details |
|---|---|
| Document info | `/Info` dictionary: Title, Author, Subject, Keywords, Creator, Producer, CreationDate, ModDate |
| **XMP metadata** | XMP metadata stream (required for PDF/A). Dublin Core schema, `pdfaid:part`/`pdfaid:conformance`, custom properties |
| **PDF/A conformance** | PDF/A-1b, PDF/A-2b, PDF/A-3b (with file attachments). Output intent with ICC profile. No encryption. All fonts embedded. No external references |
| **PDF/UA (accessibility)** | Full tagged PDF: structure tree root, standard structure types (`<Document>`, `<H1>`-`<H6>`, `<P>`, `<Table>`, `<TR>`, `<TH>`, `<TD>`, `<L>`, `<LI>`, `<Figure>`, `<Link>`, `<Span>`, `<Code>`, `<BlockQuote>`, `<TOC>`, `<TOCI>`). Alt text from `<img alt>`. `<th scope>` maps to `/Scope` attribute on header cells. `aria-label` maps to `/Alt` or `/ActualText`. `role` attribute used to override default structure type mapping. `aria-hidden="true"` marks artifacts (not tagged). Reading order from DOM. `/MarkInfo` with `/Marked true`. Document language from `<html lang>` |
| **PDF/X (print production)** | PDF/X-4 conformance for commercial printing. TrimBox/BleedBox. Output intent. No encryption. CMYK + spot colors |

#### 10d. Security

| Subsystem | Details |
|---|---|
| **AES-256 encryption** | Encryption handler V5 R6. User password (open document), owner password (change permissions) |
| **Permission flags** | Bitfield: allow/deny printing, high-quality printing, copying text, modifying, extracting, annotating, form filling, assembling |
| **Digital signatures** | Signature form fields (`/FT /Sig`). CMS/PKCS#7 detached signatures (`/SubFilter /adbe.pkcs7.detached`). Sign with X.509 certificates. `/ByteRange` covers exact signed bytes |
| **PAdES signatures** | PAdES B-B (basic), B-T (with RFC 3161 timestamp). Visible signature appearance with signer name, date, optional image. Certification signature with `/DocMDP` (allowed modifications after signing) |
| **Long-Term Validation (LTV)** | Document Security Store (DSS) with CRLs, OCSP responses, certificates for offline verification. PAdES B-LT and B-LTA profiles |

#### 10e. Page Geometry (for commercial printing)

| Subsystem | Details |
|---|---|
| **MediaBox** | Physical page size (required). Includes crop marks area |
| **TrimBox** | Final page dimensions after cutting. Required for PDF/X |
| **BleedBox** | TrimBox + 3-5mm on each side. Ensures no white edges after cutting |
| **CropBox** | Visible region in viewers. Defaults to MediaBox |
| **ArtBox** | Meaningful content boundary |
| Crop/trim marks | Rendered as vector paths between TrimBox and MediaBox |
| Registration marks | Cross-hair marks for print alignment |

#### 10f. Forms and Interactive Elements

| Subsystem | Details |
|---|---|
| **AcroForm fields** | Text fields (`/FT /Tx`), checkboxes (`/FT /Btn`), dropdown/listbox (`/FT /Ch`), signature fields (`/FT /Sig`). Pre-filled from HTML form values |
| **Read-only form rendering** | HTML `<input>`, `<select>`, `<textarea>` rendered as visible static content (text, borders, backgrounds). Optionally also create underlying AcroForm field for machine readability |
| **Form appearance streams** | Each field has `/AP` (appearance) dictionary with normal, rollover, down appearances |

#### 10g. Attachments and Embedding

| Subsystem | Details |
|---|---|
| **File attachments** | `/EmbeddedFiles` name tree. Embed any file within the PDF (XML, CSV, JSON, images). File spec with `/UF` (Unicode filename), `/EF` (embedded file stream), `/Desc` |
| **ZUGFeRD / Factur-X** | PDF/A-3 with embedded XML invoice. `/AFRelationship /Alternative`. Factur-X XMP extension schema. This is the European e-invoicing standard |
| **Associated Files** | PDF 2.0 `/AF` array on document catalog. Relationship types: Source, Data, Alternative, Supplement |

#### 10h. Barcode and QR Code Generation

| Subsystem | Details |
|---|---|
| **QR Code** | Pure C# QR code generator. Render as vector paths (resolution-independent). Error correction levels L/M/Q/H. Used for: payment links, verification URLs, tickets |
| **Code 128** | 1D barcode. Most common for general purpose. Render as vector rectangles |
| **Code 39** | 1D barcode. Legacy, but still used in logistics |
| **EAN-13 / UPC-A** | 1D barcode. Product identification |
| **PDF417** | 2D barcode. IATA boarding passes, government IDs |
| **Data Matrix** | 2D barcode. Healthcare (GS1 DataMatrix for drug serialization), small items |

Barcodes are rendered via a **dedicated API** (not from HTML -- HTML has no barcode elements):
```csharp
options.Overlays.Add(new QrCodeOverlay
{
    Content = "https://verify.example.com/cert/ABC123",
    Position = new Point(150, 50, Unit.Mm),
    Size = 25, // mm
    Page = PageSelector.All // or specific page
});
```

Or via a **custom HTML element convention**:
```html
<div data-eggpdf-qrcode="https://pay.example.com/inv/123"
     style="width: 80px; height: 80px;"></div>
```

#### 10i. Optional Content Groups (Layers)

| Subsystem | Details |
|---|---|
| **OCG (layers)** | `/OCProperties` in document catalog. Group content into togglable layers |
| **Print vs. Screen layers** | `/Print` usage category -- content visible only when printing. `/View` -- visible only on screen |
| **Use cases** | Commercial printing: spot color layers, varnish, die-cut. Certificates: security features visible only in print. Watermarks on togglable layer |

#### 10j. Document Merging

| Subsystem | Details |
|---|---|
| **PDF concatenation** | Combine multiple rendered PDFs into one. Merge page trees, reconcile object numbers |
| **Resource deduplication** | Shared fonts and images across merged documents are deduplicated |
| **Combined outlines** | Merge bookmark trees from multiple documents into unified hierarchy |
| **Page label rebuilding** | After merge, rebuild `/PageLabels` number tree for correct section numbering |
| **Named destination deconfliction** | Namespace destinations from different source documents to avoid collisions |

```csharp
// Merging API
var merger = new PdfMerger();
merger.Add(await converter.RenderAsync(coverHtml), label: null); // no page number
merger.Add(await converter.RenderAsync(tocHtml), label: new PageLabel(style: Roman)); // i, ii, iii
merger.Add(await converter.RenderAsync(bodyHtml), label: new PageLabel(style: Decimal)); // 1, 2, 3
merger.Add(await converter.RenderAsync(appendixHtml), label: new PageLabel(prefix: "A-")); // A-1, A-2
byte[] merged = merger.Build();
```

#### 10k. Common PDF User Features

Features that end users universally expect from generated PDFs:

| Feature | Details | Why It Matters |
|---|---|---|
| **Text is selectable and copyable** | All text rendered with proper ToUnicode CMap. Glyph order matches reading order. Ligatures map back to constituent characters | Users copy invoice amounts, addresses, reference numbers from PDFs daily |
| **Text search works** | Text operators in content stream produce searchable text. ActualText on structure elements for complex glyphs | Users Ctrl+F to find content in large reports |
| **Correct text extraction order** | Content stream operators emitted in reading order (left-to-right, top-to-bottom for LTR; right-to-left for RTL). Tagged PDF structure tree defines logical order | Screen readers, text extraction tools, copy-paste all depend on this |
| **Clickable hyperlinks** | Link annotations with visible styling (underline, blue color) and proper hit areas. Tooltip shows URL on hover in reader | Every PDF with URLs must have working links |
| **Clickable table of contents** | TOC entries link to their target page/position via GoTo actions. Bookmarks in sidebar match document structure | Standard expectation for any document > 5 pages |
| **Print quality at any size** | Text and vector graphics are resolution-independent. Images embedded at highest available resolution. No visible compression artifacts | PDFs are printed at 300+ DPI. Rasterized content looks terrible |
| **Consistent rendering** | All fonts fully embedded. No system font dependencies. ICC color profiles for accurate colors. Same PDF looks the same on Windows, Mac, Linux, mobile | Users share PDFs across platforms and expect identical appearance |
| **Small file size** | Font subsetting (only used glyphs). Image compression (JPEG quality, Flate). Stream compression. Object streams (PDF 1.5+). No duplicate resources | Users email PDFs, upload to portals, store millions of documents |
| **Fast opening** | Linearized PDF for web. Minimal object count. No unnecessary indirection. Cross-reference streams for large documents | Users open PDFs from email/web and expect instant display |
| **Correct page display** | ViewerPreferences set sensibly: display document title (not filename) in title bar, fit page width, single page continuous layout. Bookmarks panel visible for structured docs | First impression when user opens the PDF |
| **Repeating table headers** | `<thead>` repeats at top of each page for multi-page tables | #1 most common complaint about PDF tables |
| **Page numbers** | "Page X of Y" in headers/footers. Section-specific numbering (Roman for front matter, decimal for body) | Standard in every formal document |
| **Watermarks** | "DRAFT", "CONFIDENTIAL", "COPY" overlaid on every page. Configurable opacity, rotation, position | Common in legal, financial, and draft documents |
| **Headers and footers** | Running headers with chapter/section title. Footers with page numbers, dates, confidentiality notices | Standard in reports, contracts, manuals |
| **Mixed page sizes** | Portrait and landscape pages in same document. Different sizes for different sections (A4 body, A3 fold-out charts) | Common in engineering reports, financial documents with wide tables |

---

## Resource Resolution (Images, Fonts, Stylesheets from Any Source)

**Critical feature:** Real-world HTML references external resources via URLs. We MUST support fetching them.

```html
<!-- These must ALL work out of the box -->
<img src="https://cdn.example.com/logo.png">
<img src="/images/photo.jpg">
<img src="data:image/png;base64,iVBOR...">
<img src="./relative/path/image.png">
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap@5/dist/css/bootstrap.min.css">
```
```css
@font-face {
    font-family: 'Roboto';
    src: url('https://fonts.gstatic.com/s/roboto/v30/KFOmCnqEu92Fr1Mu4mxK.woff2') format('woff2');
}
background-image: url('https://example.com/pattern.png');
```

### IResourceResolver Interface

```csharp
public interface IResourceResolver
{
    Task<byte[]?> ResolveAsync(string url, ResourceType type, CancellationToken ct = default);
}

public enum ResourceType { Image, Font, StyleSheet, Other }
```

### Built-in Resolvers (shipped with core library)

| Resolver | Handles | How |
|---|---|---|
| **HttpResourceResolver** | `https://`, `http://` URLs | Uses `System.Net.Http.HttpClient` (part of BCL, not an external dep). Configurable: timeout, max response size, allowed domains, User-Agent header |
| **FileResourceResolver** | Absolute file paths, `file://` | Reads from local filesystem. Configurable base directory. Path traversal prevention |
| **DataUriResolver** | `data:image/png;base64,...` | Decode inline Base64 data URIs |
| **CompositeResolver** (default) | All of the above | Tries each resolver in order based on URL scheme. This is the default when no custom resolver is configured |

### Configuration

```csharp
// Default: resolves HTTP, file, and data URIs automatically
var converter = new HtmlToPdfConverter(new PdfOptions());

// Custom: restrict to local files only (no HTTP -- for security-sensitive environments)
var converter = new HtmlToPdfConverter(new PdfOptions
{
    ResourceResolver = new FileResourceResolver("/app/assets/")
});

// Custom: HTTP with domain allowlist
var converter = new HtmlToPdfConverter(new PdfOptions
{
    ResourceResolver = new HttpResourceResolver(new HttpResourceOptions
    {
        AllowedDomains = ["cdn.example.com", "fonts.googleapis.com", "fonts.gstatic.com"],
        TimeoutSeconds = 10,
        MaxResponseSizeBytes = 50 * 1024 * 1024,  // 50MB max per resource
        UserAgent = "EggPdf/1.0"
    })
});

// Custom: implement your own (S3, Azure Blob, database, etc.)
var converter = new HtmlToPdfConverter(new PdfOptions
{
    ResourceResolver = new MyS3ResourceResolver(s3Client, bucketName)
});
```

### Base URL Resolution

Relative URLs are resolved against a base URL. Resolution order:
1. `<base href="...">` tag in HTML (highest priority)
2. `PdfOptions.BaseUrl` (configured by user)
3. Current working directory (for file paths)

```csharp
// With base URL, relative paths resolve correctly
var options = new PdfOptions { BaseUrl = "https://example.com/reports/" };
// <img src="logo.png"> resolves to https://example.com/reports/logo.png
// <link href="../styles/main.css"> resolves to https://example.com/styles/main.css
```

### Caching

Resources fetched during a render are cached in memory for the duration of that render (same image referenced 10 times = 1 HTTP request). Across renders, caching is configurable:

```csharp
var options = new PdfOptions
{
    ResourceCache = new ResourceCacheOptions
    {
        Enabled = true,
        MaxCacheSizeBytes = 100 * 1024 * 1024,  // 100MB cache
        ExpirationMinutes = 60
    }
};
```

### Security

| Concern | Mitigation |
|---|---|
| SSRF (Server-Side Request Forgery) | `AllowedDomains` restricts which hosts can be fetched. Default: all allowed. In EggPdf.Service: configurable via env var |
| Path traversal | `FileResourceResolver` restricts to base directory. Rejects `../../etc/passwd` |
| Large responses | `MaxResponseSizeBytes` prevents memory exhaustion from huge resources |
| Slow responses | `TimeoutSeconds` prevents hanging on unresponsive servers |
| Redirect loops | Max 5 redirects followed. Configurable |

### What `System.Net.Http.HttpClient` means for zero-dependency policy

`HttpClient` is part of the .NET BCL (Base Class Library), available in:
- .NET Framework 4.5+ (System.Net.Http.dll, ships with the framework)
- .NET Core 1.0+ / .NET 5+ (built-in)
- netstandard2.0 (available)

This is the same category as `System.IO.Compression` -- BCL is fair game, not an external NuGet dependency.

---

## Dependency Policy: Zero External Dependencies

**Hard rule: No NuGet packages. No native binaries. Pure managed C# only.**

This is a deliberate trade-off that defines the library's identity:

| What we get | What it costs |
|---|---|
| Zero supply chain risk -- critical for a public library | More upfront implementation effort |
| No version conflicts for consumers | We own every bug |
| Smallest possible binary (~2-3 MB managed DLL) | Can't lean on existing work |
| Runs anywhere .NET runs, no platform-specific concerns | Need our own HTML parser, zlib, etc. |
| Full control over memory, allocation, performance | - |

**What about System.IO.Compression?** This is part of the .NET BCL (Base Class Library), not an external dependency. We use it freely for Flate/Deflate compression in PDF streams. Same for `System.Numerics`, `System.Text.Encoding`, etc. -- BCL is fair game.

**Everything else we build:**
- HTML5 parser (WHATWG spec)
- CSS parser (CSS Syntax Level 3)
- Selector engine (Selectors Level 4)
- Cascade + style resolution
- Layout engine (block, inline, flex, grid, table, float, positioned)
- Text engine (TrueType parsing, shaping, line breaking)
- Fragmentation (pagination)
- Paint layer
- PDF writer (PDF 1.7)

---

## Target Framework Strategy

**Goal:** Support the widest possible .NET ecosystem -- both .NET Core/.NET 5+ AND .NET Framework.

### Multi-Target Matrix

```xml
<TargetFrameworks>netstandard2.0;netstandard2.1;net6.0;net8.0;net9.0;net10.0</TargetFrameworks>
```

| Target | Covers | Why |
|---|---|---|
| **netstandard2.0** | .NET Framework 4.6.2+, .NET Core 2.0+, Mono, Xamarin, Unity | Maximum reach. This is our **baseline** -- every feature must compile here |
| **netstandard2.1** | .NET Core 3.0+ | Gains `Span<T>` in more BCL APIs, `IAsyncDisposable`, default interface members |
| **net6.0** | .NET 6+ | Gains `DateOnly`, `PriorityQueue`, better `Span` integration, `CallerArgumentExpression` |
| **net8.0** | .NET 8+ | Gains `FrozenDictionary`, `SearchValues<T>`, `IUtf8SpanFormattable`, AOT improvements |
| **net9.0** | .NET 9+ | Latest stable APIs, best performance |
| **net10.0** | .NET 10+ | Preview/future: stay ahead, adopt new APIs early. Added when .NET 10 SDK is available |

### What This Means for Our Code

**1. netstandard2.0 is the floor -- all logic must work there.**

This constrains which BCL APIs we can use unconditionally:
- `System.IO.Compression` (DeflateStream) -- available in netstandard2.0
- `System.Text.Encoding` -- available in netstandard2.0
- `System.Numerics` (Vector, Matrix) -- available in netstandard2.0
- `System.Buffers.ArrayPool<T>` -- available in netstandard2.0
- `ReadOnlySpan<T>` / `Span<T>` -- available via `System.Memory` (BCL in netstandard2.1+, but ships inbox with .NET Framework via facade)

**NOT available in netstandard2.0** (use `#if` to light up on newer targets):
- `ReadOnlySpan<char>` overloads on string/int parsing (netstandard2.1+)
- `IAsyncDisposable` (netstandard2.1+)
- `FrozenDictionary` / `FrozenSet` (net8.0+)
- `SearchValues<T>` (net8.0+)

**2. Use `#if` for performance enhancements on modern runtimes.**

```csharp
// Example: fast path on modern .NET, fallback on Framework
#if NET8_0_OR_GREATER
    private static readonly SearchValues<char> HtmlSpecialChars = SearchValues.Create("<>&\"'");
    int idx = span.IndexOfAny(HtmlSpecialChars);
#elif NETSTANDARD2_1_OR_GREATER
    int idx = span.IndexOfAny('<', '>', '&');
#else
    int idx = FindFirstSpecialChar(text);  // manual loop for netstandard2.0
#endif
```

**3. The public API surface is identical across all targets.**

Consumers see the same classes, methods, and options regardless of target. The only difference is internal performance -- newer runtimes get faster code paths automatically.

### .NET Framework Compatibility Notes

| Concern | Approach |
|---|---|
| `Span<T>` / `Memory<T>` | Available via `System.Memory` NuGet package on .NET Framework -- but wait, we said zero deps. Solution: we use `ArraySegment<T>` and `byte[]` on netstandard2.0, `Span<T>` on netstandard2.1+ via `#if` |
| `System.IO.Compression` | Built into .NET Framework 4.5+. No issue |
| `async/await` | Fully supported in .NET Framework 4.5+. No issue |
| `ValueTask` | Available in netstandard2.0 via BCL. No issue |
| System fonts path | Windows: `C:\Windows\Fonts`. Detected at runtime |
| String interpolation handlers | Polyfilled via `#if` or avoided on older targets |

### Polyfill Strategy

For APIs that don't exist on netstandard2.0 but are critical for clean code, we write **internal polyfills**:

```csharp
// Internal polyfill for netstandard2.0 only
#if !NETSTANDARD2_1_OR_GREATER
namespace EggPdf.Internal
{
    internal static class StringExtensions
    {
        internal static bool Contains(this string s, char c)
            => s.IndexOf(c) >= 0;

        internal static string[] Split(this string s, char separator, StringSplitOptions options)
            => s.Split(new[] { separator }, options);
    }
}
#endif
```

These are internal, never exposed to consumers, and compiled away on targets that don't need them.

---

## Project Structure

```
EggPdf/
|-- src/
|   |-- EggPdf/                              # Main library (public API)
|   |   |-- HtmlToPdfConverter.cs
|   |   |-- PdfOptions.cs
|   |   |-- EggPdf.csproj
|   |
|   |-- EggPdf.Core/                         # Shared primitives
|   |   |-- Resources/
|   |   |   |-- IResourceResolver.cs         # Interface for loading external resources
|   |   |   |-- HttpResourceResolver.cs      # Fetch https:// and http:// URLs
|   |   |   |-- FileResourceResolver.cs      # Load from local filesystem
|   |   |   |-- DataUriResolver.cs           # Decode data:image/... base64 URIs
|   |   |   |-- CompositeResolver.cs         # Default: combines all resolvers
|   |   |   |-- ResourceCache.cs             # In-memory cache for fetched resources
|   |   |-- Units/                           # Length, Color, Rect, Point, etc.
|   |   |-- Collections/                     # Specialized collections
|   |
|   |-- EggPdf.Html/                         # HTML5 parser (WHATWG spec)
|   |   |-- Tokenizer/
|   |   |   |-- HtmlTokenizer.cs             # State machine (~80 states)
|   |   |   |-- TokenTypes.cs                # StartTag, EndTag, Text, Comment, Doctype
|   |   |   |-- EntityDecoder.cs             # &amp; &lt; &#x41; etc.
|   |   |   |-- EntityTable.cs              # ~2,231 named HTML entities lookup
|   |   |   |-- EncodingDetector.cs         # BOM, <meta charset>, Content-Type
|   |   |-- TreeBuilder/
|   |   |   |-- HtmlTreeBuilder.cs           # Insertion modes, adoption agency
|   |   |   |-- InsertionMode.cs             # Initial, BeforeHtml, InBody, InTable, etc.
|   |   |   |-- ActiveFormattingElements.cs  # Formatting element stack
|   |   |   |-- OpenElementsStack.cs         # Stack of open elements
|   |   |-- Dom/
|   |   |   |-- HtmlNode.cs
|   |   |   |-- HtmlDocument.cs
|   |   |   |-- HtmlElement.cs
|   |   |   |-- HtmlTextNode.cs
|   |   |   |-- HtmlComment.cs
|   |   |   |-- HtmlDocumentType.cs
|   |   |   |-- NodeList.cs
|   |   |   |-- NamedNodeMap.cs              # Attributes
|   |
|   |-- EggPdf.Css/                          # CSS parsing + cascade
|   |   |-- Tokenizer/
|   |   |-- Parser/
|   |   |-- Selectors/
|   |   |-- Values/
|   |   |-- Shorthands/                      # Shorthand -> longhand expansion (50+ shorthands)
|   |   |-- Cascade/
|   |   |-- Properties/                      # Per-property type definitions + defaults
|   |   |-- MediaQueries/
|   |
|   |-- EggPdf.Style/                        # Style resolution
|   |   |-- ComputedStyle.cs
|   |   |-- StyleResolver.cs
|   |   |-- InheritanceTable.cs
|   |   |-- UserAgentStyleSheet.cs
|   |   |-- ValueResolution/                 # calc(), var(), unit conversion
|   |
|   |-- EggPdf.Layout/                       # THE CORE -- layout engine
|   |   |-- BoxGeneration/
|   |   |   |-- BoxGenerator.cs              # DOM -> box tree
|   |   |   |-- AnonymousBoxBuilder.cs
|   |   |   |-- GeneratedContent.cs          # ::before, ::after
|   |   |-- FormattingContexts/
|   |   |   |-- BlockFormattingContext.cs
|   |   |   |-- InlineFormattingContext.cs
|   |   |   |-- FlexFormattingContext.cs
|   |   |   |-- GridFormattingContext.cs
|   |   |   |-- TableFormattingContext.cs
|   |   |-- Boxes/
|   |   |   |-- LayoutBox.cs
|   |   |   |-- BlockBox.cs
|   |   |   |-- InlineBox.cs
|   |   |   |-- LineBox.cs
|   |   |   |-- FlexContainerBox.cs
|   |   |   |-- GridContainerBox.cs
|   |   |   |-- TableBox.cs
|   |   |   |-- ReplacedBox.cs               # img, etc.
|   |   |-- Algorithms/
|   |   |   |-- MarginCollapse.cs
|   |   |   |-- FloatPlacement.cs
|   |   |   |-- AbsolutePositioning.cs
|   |   |   |-- IntrinsicSizing.cs
|   |   |   |-- StackingContext.cs
|   |   |-- LayoutEngine.cs                  # Orchestrator
|   |
|   |-- EggPdf.Text/                         # Text processing
|   |   |-- Fonts/
|   |   |   |-- FontResolver.cs
|   |   |   |-- FontMetrics.cs
|   |   |   |-- TrueType/
|   |   |   |   |-- TtfParser.cs
|   |   |   |   |-- CmapTable.cs
|   |   |   |   |-- HmtxTable.cs
|   |   |   |   |-- KernTable.cs
|   |   |   |   |-- GposTable.cs
|   |   |   |   |-- Os2Table.cs
|   |   |   |-- OpenType/
|   |   |   |   |-- GsubTable.cs             # Ligatures
|   |   |   |-- WoffDecoder.cs               # WOFF 1.0 -> TrueType decompression
|   |   |   |-- SystemFontLocator.cs         # Find fonts on Win/Mac/Linux
|   |   |   |-- FontFallbackChain.cs         # Missing glyph -> try next font
|   |   |   |-- FontSynthesizer.cs           # Fake bold (stroke) / fake italic (skew)
|   |   |   |-- FontSubsetter.cs
|   |   |-- Shaping/
|   |   |   |-- TextShaper.cs
|   |   |   |-- LineBreaker.cs               # UAX #14
|   |   |   |-- BidiAlgorithm.cs             # UAX #9
|   |   |   |-- WordBreaker.cs
|   |
|   |-- EggPdf.Svg/                          # SVG rendering engine (part of core, NOT a separate package)
|   |   |-- SvgParser.cs                    # Parse SVG XML to SVG DOM
|   |   |-- SvgRenderer.cs                  # SVG DOM -> Paint commands (vector)
|   |   |-- Elements/
|   |   |   |-- SvgRect.cs
|   |   |   |-- SvgCircle.cs
|   |   |   |-- SvgPath.cs                  # Full SVG path data parser (M,L,C,Q,A,Z)
|   |   |   |-- SvgText.cs
|   |   |   |-- SvgImage.cs
|   |   |   |-- SvgGroup.cs
|   |   |   |-- SvgGradient.cs
|   |   |   |-- SvgClipPath.cs
|   |   |-- PathDataParser.cs               # Parse d="M 0 0 L 10 10 ..." commands
|   |   |-- ViewBoxResolver.cs              # viewBox + preserveAspectRatio
|   |
|   |-- EggPdf.Fragmentation/               # Pagination
|   |   |-- PageBreaker.cs
|   |   |-- PageRuleResolver.cs              # @page
|   |   |-- MarginBoxLayout.cs              # Page margin boxes
|   |   |-- FragmentedBox.cs
|   |
|   |-- EggPdf.Paint/                        # Paint layer
|   |   |-- PaintTree.cs
|   |   |-- PaintCommands/
|   |   |   |-- DrawRect.cs
|   |   |   |-- DrawText.cs
|   |   |   |-- DrawImage.cs
|   |   |   |-- DrawGradient.cs
|   |   |   |-- DrawShadow.cs
|   |   |   |-- ClipRegion.cs
|   |   |   |-- TransformGroup.cs
|   |   |-- Painter.cs
|   |
|   |-- EggPdf.Pdf/                          # PDF output backend
|   |   |-- PdfDocument.cs
|   |   |-- PdfPage.cs
|   |   |-- PdfWriter.cs
|   |   |-- ContentStream/
|   |   |   |-- PdfContentStreamBuilder.cs
|   |   |   |-- TextOperators.cs
|   |   |   |-- GraphicsOperators.cs
|   |   |-- Objects/
|   |   |   |-- PdfDictionary.cs
|   |   |   |-- PdfArray.cs
|   |   |   |-- PdfStream.cs
|   |   |   |-- PdfName.cs
|   |   |   |-- PdfString.cs
|   |   |   |-- PdfNumber.cs
|   |   |   |-- PdfReference.cs
|   |   |-- Fonts/
|   |   |   |-- PdfFontEmbedder.cs
|   |   |   |-- CidFontWriter.cs
|   |   |   |-- ToUnicodeCMapWriter.cs
|   |   |-- Images/
|   |   |   |-- JpegXObject.cs
|   |   |   |-- PngXObject.cs
|   |   |   |-- ImageXObjectWriter.cs
|   |   |-- Annotations/
|   |   |   |-- LinkAnnotationWriter.cs      # <a href> -> PDF link annotations
|   |   |   |-- InternalLinkResolver.cs      # <a href="#id"> -> GoTo actions
|   |   |-- Outlines/
|   |   |   |-- BookmarkGenerator.cs         # <h1>-<h6> -> PDF outline tree
|   |   |-- Accessibility/
|   |   |   |-- StructureTreeWriter.cs       # Tagged PDF structure tree
|   |   |   |-- AltTextMapper.cs             # <img alt="..."> -> PDF alt text
|   |   |-- Compliance/
|   |   |   |-- PdfAWriter.cs               # PDF/A: ICC profile, XMP, conformance
|   |   |   |-- XmpMetadataWriter.cs        # XMP metadata stream
|   |   |-- Security/
|   |   |   |-- PdfEncryptor.cs             # AES-256 encryption + permission flags
|   |   |   |-- PdfSigner.cs               # Digital signatures (CMS/PKCS#7, PAdES)
|   |   |   |-- SignatureAppearance.cs      # Visible signature rendering
|   |   |   |-- LtvWriter.cs               # Long-Term Validation (DSS, CRLs, OCSP)
|   |   |-- Forms/
|   |   |   |-- AcroFormWriter.cs           # AcroForm generation from HTML forms
|   |   |   |-- FormFieldFactory.cs         # Text, checkbox, radio, dropdown, signature fields
|   |   |-- Attachments/
|   |   |   |-- FileAttachmentWriter.cs     # Embed files within PDF
|   |   |   |-- ZugferdWriter.cs            # ZUGFeRD/Factur-X e-invoice support
|   |   |-- Barcodes/
|   |   |   |-- QrCodeGenerator.cs          # QR Code as vector paths
|   |   |   |-- Code128Generator.cs         # Code 128 barcode
|   |   |   |-- DataMatrixGenerator.cs      # Data Matrix barcode
|   |   |   |-- BarcodeRenderer.cs          # Common rendering to paint commands
|   |   |-- PageGeometry/
|   |   |   |-- TrimBoxWriter.cs            # TrimBox/BleedBox/CropBox for print
|   |   |   |-- CropMarksRenderer.cs        # Crop marks, registration marks
|   |   |-- Merging/
|   |   |   |-- PdfMerger.cs               # Combine multiple PDFs
|   |   |   |-- ResourceDeduplicator.cs     # Shared fonts/images dedup
|   |   |   |-- PageLabelRebuilder.cs       # Rebuild page labels after merge
|   |   |-- Transparency/
|   |       |-- TransparencyGroupWriter.cs
|   |
|   |-- EggPdf.Razor/                           # OPTIONAL integration package (has NuGet deps)
|   |   |-- EggPdf.Razor.csproj                 # Depends on: EggPdf + Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation
|   |   |-- RazorToPdfConverter.cs              # Public API: .cshtml + model -> PDF
|   |   |-- RazorToPdfOptions.cs
|   |   |-- Internal/
|   |   |   |-- RazorViewRenderer.cs            # Compiles + executes .cshtml -> HTML string
|   |   |   |-- EmbeddedViewLocator.cs          # Find views from filesystem, embedded resources, or in-memory
|   |   |-- DependencyInjection/
|   |       |-- EggPdfRazorServiceExtensions.cs  # services.AddEggPdfRazor()
|   |
|   |-- EggPdf.AspNetCore/                      # OPTIONAL ASP.NET Core integration
|   |   |-- EggPdf.AspNetCore.csproj            # Depends on: EggPdf + Microsoft.AspNetCore.Http.Abstractions
|   |   |-- PdfResult.cs                        # IActionResult: HTML string -> PDF response
|   |   |-- RazorPdfResult.cs                   # IActionResult: Razor view -> PDF response
|   |   |-- DependencyInjection/
|   |       |-- EggPdfAspNetCoreExtensions.cs   # services.AddEggPdf()
|   |
|   |-- EggPdf.Cli/                             # dotnet tool: dotnet-eggpdf
|   |   |-- EggPdf.Cli.csproj                   # PackAsTool=true
|   |   |-- Program.cs                          # CLI: dotnet eggpdf input.html -o output.pdf
|   |
|   |-- EggPdf.Service/                         # Standalone HTTP microservice + Web UI
|   |   |-- EggPdf.Service.csproj               # ASP.NET Core minimal API
|   |   |-- Program.cs                          # Host builder + endpoints
|   |   |-- Endpoints/
|   |   |   |-- RenderEndpoint.cs               # POST /api/render (HTML -> PDF)
|   |   |   |-- RenderRazorEndpoint.cs          # POST /api/render/razor (template + model -> PDF)
|   |   |   |-- MergeEndpoint.cs                # POST /api/merge
|   |   |   |-- SignEndpoint.cs                 # POST /api/sign
|   |   |   |-- HealthEndpoint.cs               # GET /health
|   |   |-- WebUI/                              # Simple browser-based HTML-to-PDF interface
|   |   |   |-- index.html                      # SPA: HTML editor + live preview + download
|   |   |   |-- app.js                          # Editor logic, API calls, preview
|   |   |   |-- style.css                       # UI styling
|   |   |-- Auth/
|   |   |   |-- IEggPdfAuthHandler.cs           # Custom auth interface
|   |   |   |-- ApiKeyAuthHandler.cs            # X-Api-Key header validation
|   |   |   |-- JwtAuthHandler.cs               # JWT Bearer token validation
|   |   |   |-- BasicAuthHandler.cs             # Basic auth
|   |   |-- Middleware/
|   |   |   |-- RateLimitingMiddleware.cs        # Requests/minute per client
|   |   |   |-- RequestSizeLimitMiddleware.cs    # Max HTML body size
|   |   |   |-- RenderTimeoutMiddleware.cs       # Max render time per request
|   |   |-- Observability/
|   |   |   |-- MetricsMiddleware.cs            # Request count, duration, error rate, queue depth
|   |   |   |-- PrometheusExporter.cs           # GET /metrics (Prometheus format)
|   |   |   |-- OpenTelemetrySetup.cs           # Traces + metrics via OTEL
|   |   |-- appsettings.json                    # Default config (auth off, web UI on)
|   |
|-- docker/
|   |-- Dockerfile.service                      # REST API + Web UI (eggpdf/service)
|   |-- Dockerfile.cli                          # CLI only (eggpdf/cli)
|   |-- docker-compose.yml                      # Full stack ready-to-deploy
|   |-- .dockerignore
|   |
|-- samples/
|   |-- EggPdf.Samples.Console/                 # Console app: batch HTML -> PDF
|   |-- EggPdf.Samples.WebApp/                  # ASP.NET Core: Razor invoice generation
|   |-- EggPdf.Samples.Templates/               # Template gallery: invoice, report, letter, certificate
|
|-- tests/
|   |-- EggPdf.Tests.Unit/                   # Layer 1: fast isolated unit tests
|   |   |-- Html/
|   |   |   |-- TokenizerTests.cs
|   |   |   |-- TreeBuilderTests.cs
|   |   |   |-- Html5LibTestRunner.cs        # Runs html5lib-tests suite
|   |   |-- Css/
|   |   |   |-- CssTokenizerTests.cs
|   |   |   |-- CssParserTests.cs
|   |   |   |-- ShorthandExpansionTests.cs   # All 50+ CSS shorthands
|   |   |   |-- SelectorTests.cs
|   |   |   |-- CascadeTests.cs
|   |   |   |-- ValueResolutionTests.cs
|   |   |-- Pdf/
|   |   |   |-- PdfWriterTests.cs
|   |   |   |-- ContentStreamTests.cs
|   |   |   |-- FontEmbeddingTests.cs
|   |   |   |-- LinkAnnotationTests.cs       # <a href> -> PDF links
|   |   |   |-- BookmarkTests.cs             # Headings -> PDF outline tree
|   |   |   |-- PdfAComplianceTests.cs       # PDF/A validation
|   |   |-- Text/
|   |       |-- TrueTypeParserTests.cs
|   |       |-- FontSubsetterTests.cs
|   |       |-- LineBreakerTests.cs
|   |
|   |-- EggPdf.Tests.Layout/                # Layer 2: layout assertion tests
|   |   |-- BlockLayoutTests.cs
|   |   |-- InlineLayoutTests.cs
|   |   |-- FlexLayoutTests.cs
|   |   |-- GridLayoutTests.cs
|   |   |-- TableLayoutTests.cs
|   |   |-- FloatTests.cs
|   |   |-- PositionedLayoutTests.cs
|   |   |-- MarginCollapseTests.cs
|   |   |-- IntrinsicSizingTests.cs
|   |   |-- FragmentationTests.cs
|   |   |-- Helpers/
|   |       |-- LayoutTestHelper.cs          # HTML -> layout tree for assertions
|   |
|   |-- EggPdf.Tests.Visual/                # Layer 3: visual regression tests
|   |   |-- AsciiPixelTests/                # WeasyPrint-style character grid tests
|   |   |-- GoldenImageTests/               # Reference PNG comparison
|   |   |-- Helpers/
|   |   |   |-- AssertPixels.cs             # ASCII pixel-art comparison
|   |   |   |-- GoldenFileComparer.cs       # Pixel-diff with threshold
|   |   |   |-- RasterBackend.cs            # Paint commands -> bitmap (for testing)
|   |   |-- golden/                         # Golden reference PNGs
|   |
|   |-- EggPdf.Tests.Fuzz/                  # Fuzz testing
|   |   |-- HtmlParserFuzz.cs
|   |   |-- CssParserFuzz.cs
|   |   |-- LayoutFuzz.cs
|   |
|   |-- testdata/
|   |   |-- html5lib-tests/                 # git submodule
|   |   |-- fonts/                          # Test fonts (DejaVu, Liberation -- open license)
|   |   |-- images/                         # Test images (JPEG, PNG, transparent PNG)
|   |   |-- corpus/                         # Real-world HTML samples + Chrome references
|   |
|   |-- EggPdf.Tests.Integration/            # End-to-end HTML -> PDF tests
|   |   |-- RoundTripTests.cs               # Generate PDF, verify it opens and has correct content
|   |   |-- CrossReaderTests.cs             # Validate PDF across multiple readers
|   |
|   |-- EggPdf.Tests.Razor/                 # Razor integration tests (requires ASP.NET Core)
|       |-- RazorRenderTests.cs             # .cshtml + model -> PDF
|       |-- InlineRazorTests.cs             # Razor string -> PDF
|       |-- ViewLocatorTests.cs             # Find templates from various sources
|       |-- TestViews/                      # .cshtml test templates
|           |-- SimpleTemplate.cshtml
|           |-- InvoiceTemplate.cshtml
|           |-- PartialViewTemplate.cshtml
|
|-- benchmarks/
|   |-- EggPdf.Benchmarks/                  # BenchmarkDotNet performance suite
|       |-- ParseBenchmarks.cs
|       |-- LayoutBenchmarks.cs
|       |-- RenderBenchmarks.cs
|       |-- MemoryBenchmarks.cs
|
|-- tools/
|   |-- EggPdf.WptRunner/                   # WPT conformance test runner
|   |   |-- WptTestDiscovery.cs
|   |   |-- WptTestRunner.cs
|   |   |-- WptFuzzyMatcher.cs
|   |   |-- WptReport.cs
|   |-- EggPdf.ChromeRef/                   # Generate Chrome reference PNGs
|       |-- ChromeReferenceGenerator.cs
|
|-- EggPdf.sln
```

---

## Implementation Phases

### Phase 1: Vertical Slice -- "Hello World to PDF"
**Goal:** End-to-end pipeline producing a valid PDF from trivial HTML.

Every layer exists but in minimal form:
- HTML parsing: our own tokenizer + tree builder (handle basic tags, self-closing, attributes)
- HTML entities: full entity table (~2,231 named entities + numeric entities)
- Encoding: UTF-8 default, `<meta charset>` detection, BOM sniffing
- `<style>` in both `<head>` and `<body>` (common in real-world HTML)
- CSS: parse inline styles + a small user-agent stylesheet
- Style: resolve ~20 properties (display, color, font-size, margin, padding, width, height, background-color)
- Box generation: block boxes only
- Layout: block formatting context only (stack boxes vertically)
- Paint: backgrounds, borders, text
- PDF: write valid PDF 1.7 with built-in fonts (Helvetica/Times/Courier)
- PDF hyperlinks: `<a href="...">` produces clickable link annotations
- Text: use built-in PDF font metrics (no TrueType parsing yet)
- Custom page sizes: A4, Letter, Legal, and arbitrary width/height in any unit
- Error handling: parser never throws (infallible). Unknown elements/properties silently ignored
- Max limits: configurable max pages, max elements, max nesting depth (prevent OOM on adversarial input)
- CancellationToken: checked at each pipeline stage, not just top-level API
- Meaningful errors: "font not found: Arial, falling back to Helvetica", "image decode failed for img at line 42"
- Edge cases: empty/null HTML produces a valid 1-page blank PDF. `<script>` content not rendered. `<noscript>` content IS rendered
- Base URL: configurable via `PdfOptions.BaseUrl` for resolving relative URLs when no `<base>` tag

**Deliverable:** `<h1>Hello</h1><p>World</p>` renders to a valid, correctly-laid-out PDF with clickable links and selectable text.

### Phase 2: CSS Foundation
**Goal:** Full CSS parsing, cascade, and selector engine.

- CSS tokenizer (full CSS Syntax Level 3)
- CSS parser (stylesheets, at-rules, declarations)
- **CSS shorthand expansion** -- all 50+ shorthands (margin, padding, border, font, background, flex, grid-*, etc.)
- **`<link rel="stylesheet">` loading** via IResourceResolver
- **`@import url(...)` support** -- resolve and inline imported stylesheets
- **`!important` in inline styles** -- correctly handled in cascade
- Selector engine (Level 4: combinators, pseudo-classes, :nth-child, :not, :is, :where)
- Cascade algorithm (specificity, origin, !important, @layer order, source order)
- Inheritance for all inheritable properties
- Value resolution: units (em, rem, %, px, pt, cm, mm, in, vw, vh), calc(), min(), max(), clamp(), var()
- ComputedStyle with all Tier 1 properties
- **`@media print` handling** -- this is foundational to our engine:
  - Our engine evaluates `@media print` as **true** and `@media screen` as **false** by default
  - `@media all` rules apply. `@media not print` rules are skipped
  - Media features: `@media (min-width: ...)` evaluated against page width, `@media (color)` = true, `@media (prefers-color-scheme: light)` = true
  - `print-color-adjust: exact` / `-webkit-print-color-adjust: exact` -- when set, backgrounds and colors are preserved. When not set, we follow Chrome behavior (preserve by default in our engine, since users explicitly want PDF output)
  - Configurable via `PdfOptions.MediaType` (default: `Print`, can be set to `Screen` for screen-layout PDFs)
- **`@supports` feature queries**: `@supports (display: flex) { ... }` evaluated against our implemented properties. Supports `and`, `or`, `not` combinators
- **`@charset`**: encoding declaration at start of CSS file
- **Graceful degradation**: unknown properties silently ignored, unknown values fall back to initial value (per CSS error handling spec)
- **CSS compatibility warnings** (optional): log when unsupported features are encountered, e.g., "CSS Grid used but not supported until Phase 7"

**Deliverable:** Correctly resolves styles for any CSS that Chrome would accept. `@media print` rules applied correctly. Unknown features degrade gracefully.

### Phase 3: Inline Layout + Typography
**Goal:** Text wraps correctly, mixed inline content works.

- Inline formatting context (line boxes, inline boxes, text runs)
- TrueType font parsing (cmap, hmtx, hhea, head, OS/2, kern)
- **System font discovery** (Win: `C:\Windows\Fonts`, macOS: `/Library/Fonts`, Linux: `/usr/share/fonts`)
- @font-face support (local fonts, embedded fonts)
- **WOFF decoding** (zlib-compressed TrueType -- required for most @font-face in practice)
- **Font fallback chain** -- when a glyph is missing, try next font in stack, then system fallback
- **Bold/italic synthesis** -- fake bold via stroke, fake italic via skew when variant doesn't exist
- Glyph measurement with real font metrics
- Line breaking algorithm (UAX #14 + CSS word-break/overflow-wrap)
- Font embedding in PDF (subset TrueType + CIDFont + ToUnicode)
- Inline elements: `<b>`, `<i>`, `<u>`, `<span>`, `<a>`, `<br>`, `<wbr>`
- `<pre>` / `<code>` whitespace preservation with monospace font default
- `tab-size` property (default 8, commonly set to 4 for code blocks)
- white-space property (normal, nowrap, pre, pre-wrap, pre-line, break-spaces)
- `font-variant`: small-caps, numeric (tabular-nums, oldstyle-nums), ligatures
- `font-feature-settings` for direct OpenType feature control
- `font-kerning: auto | normal | none`
- text-align, text-indent, letter-spacing, word-spacing
- text-transform: uppercase, lowercase, capitalize

- **CSS `hyphens: auto`** with language-specific dictionaries (English, German, French, etc.). Use pattern-based hyphenation (Liang/Knuth algorithm, same as TeX). Hyphenation dictionaries shipped as embedded resources (~200KB total for major languages)
- CSS `line-height`: normal, number, length, percentage
- CSS `vertical-align` in inline context: baseline, middle, sub, super, top, bottom, text-top, text-bottom, length, percentage

**Deliverable:** Rich text paragraphs render with correct wrapping, hyphenation, font selection, fallback, and mixed styles.

### Phase 4: Block Model Complete
**Goal:** Full CSS 2.1 block layout.

- Margin collapsing (all cases: adjacent siblings, parent-child, empty blocks, through)
- Float layout (left/right, clear, line box shortening)
- min-width, max-width, min-height, max-height
- Percentage resolution for all box properties
- overflow: hidden (clipping)
- display: inline-block
- Generated content (::before, ::after)
- **CSS `content` property values**: strings, `counter()`, `counters()`, `attr()`, `open-quote`/`close-quote`
- List markers (::marker, counters, nested counter styles)
- Anonymous box generation
- **Form elements** rendered as static boxes: `<input>` shows value text, `<select>` shows selected option, `<textarea>` shows content, `<button>` renders label, `<fieldset>`/`<legend>` with border
- **`<details>`/`<summary>`** rendered in open state (Chrome print behavior)

**Deliverable:** CSS 2.1 visual formatting model is complete. Form elements render readably.

### Phase 5: Tables
**Goal:** Full table layout -- the #1 most-used feature in business PDFs.

- Table formatting context
- Fixed and auto table layout algorithms
- Column width calculation and distribution (incl. `<col width>` attributes)
- Row height calculation
- Spanning cells (colspan, rowspan)
- Border-collapse model + separated borders model
- `border-spacing`, `caption-side`, `empty-cells`
- Caption, thead/tbody/tfoot ordering
- Vertical-align within cells
- **Repeating `<thead>` at top of each page** when table spans multiple pages (critical -- #1 user request for PDF tables)
- **Repeating `<tfoot>` at bottom of each page** (optional, less common)
- Table fragmentation across pages (split rows at row boundaries, never mid-cell)
- **Streaming/incremental table layout**: for very large tables (5,000+ rows), layout and paginate rows incrementally without holding the entire table layout tree in memory. This is WeasyPrint's #1 performance complaint -- we must solve it. Approach: lay out rows in chunks, emit pages as they fill, discard row geometry after pagination

**Deliverable:** Any `<table>` renders correctly. Multi-page tables repeat headers. This covers 80% of business PDF use cases (invoices, reports, ledgers).

### Phase 6: Flexbox
**Goal:** Full CSS Flexible Box Layout Level 1.

- Flex container establishment
- Main axis / cross axis
- Flex item sizing (basis, grow, shrink)
- Flex line wrapping
- Alignment (align-items, align-self, align-content, justify-content)
- Order property
- Flex item min-size auto
- Nested flex containers
- Interaction with absolute positioning inside flex

**Deliverable:** All common flexbox patterns render correctly (centering, holy grail layout, card grids, etc.).

### Phase 7: Grid
**Goal:** Full CSS Grid Layout Level 1.

- Explicit grid definition (grid-template-rows/columns/areas)
- Auto-placement algorithm
- Named grid lines and areas
- Spanning items
- Track sizing (fixed, %, fr, min-content, max-content, minmax(), auto)
- Gap properties
- Alignment (align/justify-items, align/justify-content)
- Implicit grid tracks (grid-auto-rows/columns)
- Subgrid (if targeting recent Chrome)

**Deliverable:** Grid-based layouts render correctly.

### Phase 8: Pagination + Paged Media
**Goal:** Production-quality multi-page output. This is where we match/exceed Prince XML quality for paged media.

**Core fragmentation:**
- Region-based pagination: each layout element receives available regions and returns frames (Typst pattern)
- break-before/after/inside (auto, avoid, page, column)
- Orphans and widows
- Fragmentation of ALL formatting contexts: block, inline, flex, grid, table, float
- box-decoration-break (clone/slice)
- Introspection loop: layout iterates up to 5 times until page counters stabilize (Typst pattern)

**@page rules (CSS Paged Media Level 3):**
- Page size, margins, orientation
- **Mixed orientations in same document**: different `@page` rules can specify different sizes/orientations per named page. A report can have portrait body pages and landscape chart pages:
  ```css
  @page { size: A4 portrait; }
  @page landscape-chart { size: A4 landscape; }
  .chart-page { page: landscape-chart; }
  ```
- **Page rotation**: `/Rotate` attribute on individual pages (0, 90, 180, 270 degrees). Different from `@page size` -- rotation changes viewing orientation without changing page dimensions
- **`@page marks`**: `marks: crop` (crop marks), `marks: cross` (registration marks), `marks: crop cross` (both). CSS way to request print marks without programmatic API
- **`@page bleed`**: `bleed: 3mm` -- CSS way to set bleed area. Maps to BleedBox = TrimBox + bleed value on each side
- Named pages: `page: chapter` with auto-breaks between page names
- Page selectors: `:first`, `:left`, `:right`, `:blank`, `:nth(An+B)`
- All 16 page margin boxes: @top-left, @top-center, @top-right, @top-left-corner, @top-right-corner, @bottom-left, @bottom-center, @bottom-right, @bottom-left-corner, @bottom-right-corner, @left-top, @left-middle, @left-bottom, @right-top, @right-middle, @right-bottom

**Generated content for paged media (CSS GCPM):**
- Running headers/footers: `position: running(name)` + `content: element(name)` in @page
- Page counters: `counter(page)`, `counter(pages)`
- **`target-counter()`**: `content: target-counter(attr(href), page)` -- inserts the page number of a linked element. Essential for generating Table of Contents with page numbers:
  ```css
  /* TOC entry shows target page number */
  .toc-entry a::after {
      content: leader('.') target-counter(attr(href, url), page);
  }
  ```
- **`leader()`**: `content: leader('.')` -- generates dot leaders (.........) between TOC entry text and page number. CSS GCPM spec. Critical for professional-looking TOCs
- **Auto Table of Contents**: scan `<h1>`-`<h6>`, generate clickable TOC with page numbers, configurable depth. Available as:
  - CSS-based: author writes TOC HTML with `target-counter()` and `leader()`
  - API-based: `PdfOptions.GenerateTableOfContents = true` auto-generates TOC from headings
- **`string-set` / `content: string()`** -- named strings for running headers that change per section. This is how you show the current chapter title in the page header:
  ```css
  h1 { string-set: chapter-title content(); }
  @page { @top-center { content: string(chapter-title); } }
  /* Header shows "Chapter 3: Results" and updates when the next h1 appears */
  ```
  Supports `first` (first assignment on page), `last` (last assignment), `first-except` (all pages except where assigned). Prince XML's most-loved feature
- Footnotes: `float: footnote` with auto call markers
- **Footnote counter**: `counter(footnote)` for auto-numbering footnote calls (1, 2, 3...)
- **`@page { @footnote { ... } }` area styling**: style the footnote area itself (border-top, margin-top, max-height). Controls how footnotes look at page bottom
- **`target-content()`**: `content: target-content(attr(href))` -- inserts the text content of a linked element (e.g., auto-generate "See Section 'Introduction'" from the target heading text). Complement to `target-counter()`
- Page floats: `float: top` / `float: bottom`, `float: snap` (snap to nearest page edge)
- **Float deferral**: `float-defer-page` / `float-defer-column` -- defer a float to a subsequent page/column (for figure placement in academic/publishing layouts)
- **Sidenotes / margin notes**: content floated to the page margin area:
  ```css
  .sidenote { float: right; width: 30%; margin-right: -35%; font-size: 0.85em; }
  /* Or via Prince-style extensions for dedicated margin areas */
  ```
  Used in academic papers, legal briefs, textbooks. Rendered in the margin outside the main content area
- **Column footnotes**: in multi-column layout, footnotes at the bottom of each column (not the page). For newspaper/magazine/academic layouts
- **Page groups**: allow `:first` page selector to apply to the first page of each chapter/section, not just the first page of the document. Implemented via `page-group: start` on elements that begin a new group. Critical for multi-chapter books

**PDF document structure:**
- **PDF bookmarks / outlines**: auto-generate from `<h1>`-`<h6>` headings. Nested hierarchy (h2 under h1, etc.). Users see a clickable TOC sidebar in PDF readers
- **Internal links**: `<a href="#section2">` produces GoTo annotations pointing to the anchor's page/position
- **Watermark support**: render text/image watermark on every page via @page background or dedicated PdfOptions.Watermark

**Page labels:**
- Different numbering schemes per section: Roman numerals for front matter (i, ii, iii), decimal for body (1, 2, 3), prefixed for appendix (A-1, A-2)
- Configurable via `PdfOptions.PageLabels` or automatically from CSS named pages

**Document language:**
- `/Lang` entry from `<html lang="en-US">` attribute. Per-element override for multilingual documents

**Deliverable:** Multi-page documents with proper pagination, headers, footers, page numbers, bookmarks, page labels, internal/external links, and running elements. This is a key differentiator vs. other pure-C# libraries.

### Phase 9: Images, SVG + Replaced Elements
**Goal:** Full image and SVG support. Every image format that works in Chrome print works in EggPdf.

**All image formats:**
- JPEG embedding (pass-through to PDF via DCTDecode)
  - **EXIF orientation handling**: read EXIF orientation tag (1-8) and apply rotation/flip before embedding. Photos from smartphones are often stored rotated with EXIF tag indicating correct orientation. Without this, images appear sideways
  - CMYK JPEG: detect and handle (some print-workflow JPEGs are CMYK)
- PNG decoding (RGBA, palette, interlaced) + alpha channel separation to SMask
- GIF decoding (first frame, palette -> RGB)
- BMP decoding (legacy format, simple)
- Base64 data URIs (`data:image/png;base64,...` -- detect format from MIME type)
- **Image loading via IResourceResolver**: relative paths, absolute paths, custom resolvers (S3, database, CDN)
- **`<base href>` support** for resolving relative image URLs
- `<img>` intrinsic sizing and CSS `aspect-ratio` property
- object-fit / object-position
- **Image DPI handling**: high-DPI images render at intended display size, not pixel-for-pixel
- **Broken image fallback**: when image fails to load, show `alt` text in a placeholder box (like browsers)
- Image as background (background-image: url(...))

**SVG rendering** (major sub-engine):
- Inline `<svg>` elements in HTML
- `<img src="file.svg">` external SVG files
- `background-image: url("icon.svg")` SVG backgrounds
- Render SVG as **vector operations in PDF** (not rasterized) -- preserves quality at any zoom
- Basic shapes: rect, circle, ellipse, line, polyline, polygon
- `<path>` with full SVG path data (M, L, C, Q, A, Z commands)
- `<text>` and `<tspan>` with font resolution
- Gradients: linearGradient, radialGradient
- Transforms: translate, rotate, scale, matrix
- viewBox and preserveAspectRatio
- `<g>`, `<defs>`, `<use>`, `<symbol>` for structure and reuse
- `<clipPath>` for clipping
- CSS styling: fill, stroke, stroke-width, opacity, etc.
- `<image>` for embedded raster images within SVG

**Gradients (CSS):**
- linear-gradient, radial-gradient, conic-gradient, repeating variants

**Responsive images:**
- `<picture>` / `<source>` elements: select best source based on `media` attribute (prefer `@media print` source) and `type` attribute (prefer supported format)
- `srcset` attribute on `<img>`: select highest resolution image available (for print, prefer highest DPI)

**Ruby annotations (CJK):**
- `<ruby>`, `<rt>`, `<rp>` elements: render pronunciation guides above/beside CJK characters
- CSS `ruby-position: over | under`, `ruby-align`

**Deliverable:** All common image formats render correctly. SVG renders as crisp vectors. Responsive images select print-appropriate sources. Missing images degrade gracefully with alt text.

### Phase 10: Visual Effects + Modern Formats
**Goal:** Polish visual output to match Chrome. Support remaining image/CSS formats.

- box-shadow (offset, blur, spread, inset, multiple)
- border-radius (elliptical corners)
- opacity and RGBA colors
- CSS transforms (translate, rotate, scale) in print context
- text-decoration (underline, overline, line-through, wavy, etc.)
- text-shadow
- Clipping (overflow: hidden with border-radius)
- Stacking context correctness
- **WebP decoding** (increasingly the default format in Chrome; lossy + lossless variants)
- **CSS Nesting** (`div { & p { color: red } }`)
- **CSS Container Queries** (`@container`)
- **CSS `color-mix()`**, **`light-dark()`**
- **CSS `text-wrap: balance`** (for headings)
- **CSS `@scope`**

- **CSS Multi-column layout**: column-count, column-width, column-gap, column-rule, column-span. Common in print (newspaper-style layouts, magazine articles)
- CSS `visibility: hidden` (box laid out but invisible, different from `display: none`)

**Deliverable:** Visually polished output matching Chrome print for modern sites. WebP images and multi-column layouts work.

### Phase 11: Performance + Production Hardening
**Goal:** Production-ready performance and reliability.

**Performance:**
- Memory optimization (reduce allocations, pool objects, Span<T> on netstandard2.1+, ArrayPool on all targets)
- Streaming PDF output (don't hold entire document in memory)
- Parallel layout for independent subtrees
- Font caching across conversions
- Benchmark suite (vs. WeasyPrint, vs. Chrome headless)
- Thread safety for concurrent conversions

**Robustness:**
- Fuzz testing with malformed HTML/CSS
- **Optional ILogger integration**: diagnostic logging for render issues ("skipped unsupported property X on element Y", "font fallback from Arial to Helvetica", "image load failed for src=...")
- **Render timing breakdown**: report time per pipeline stage ("parse: 5ms, style: 12ms, layout: 45ms, paint: 8ms, pdf: 15ms")

**PDF compliance:**
- **PDF/A compliance** option (ICC color profile, XMP metadata, full font embedding, conformance declaration)
- **PDF encryption**: user password, owner password, permission flags (print, copy, modify)
- **WOFF2 decoding** (Brotli decompression -- complex, but needed for full @font-face support)
- **OpenType/CFF outlines** support (PostScript-outlined .otf fonts)

**Developer experience:**
- **Debug/inspect mode**: optionally draw box boundaries, padding, margin in colored overlays (like browser DevTools). Enabled via `PdfOptions.DebugLayout = true`
- **`dotnet-eggpdf` CLI tool**:
  - `dotnet eggpdf input.html -o output.pdf --page-size A4 --margin 2cm` -- single file conversion
  - `dotnet eggpdf input.html -o output.png --format png --dpi 150` -- render to image
  - `dotnet eggpdf *.html -o output/ --batch` -- batch convert directory
  - `dotnet eggpdf input.html --watch` -- **watch mode**: re-render on file change, open in PDF viewer, auto-refresh. Essential for design iteration (change HTML/CSS, see PDF update instantly)
- **Source Link + deterministic builds** for NuGet package (source link to GitHub, snupkg debug symbols)
- **XML documentation** on all public APIs (IntelliSense)
- **CHANGELOG.md** tracking changes per version

### Phase 12: Razor + ASP.NET Core + Ecosystem
**Goal:** First-class Razor template support, ASP.NET Core integration, and ecosystem tooling.

- **EggPdf.Razor** NuGet package
  - `IRazorToPdfConverter` interface with DI registration
  - `RenderViewAsync(viewName, model)` -- find .cshtml, render with model, convert to PDF (byte[], Stream, file)
  - `RenderStringAsync(razorTemplate, model)` -- inline Razor string to PDF
  - View locator: filesystem, embedded resources, in-memory
  - Per-render option overrides (page size, orientation, CSS)
  - Partial view and layout support (via Microsoft's Razor engine)
- **EggPdf.AspNetCore** NuGet package
  - `RazorPdfResult` -- IActionResult that renders a Razor view as PDF download
  - `PdfResult` -- IActionResult that renders an HTML string as PDF download
  - Middleware for serving PDF at specific endpoints
  - Content negotiation: return PDF when `Accept: application/pdf` header is present
- **Sample projects**
  - Minimal ASP.NET Core app: invoice generation from Razor template end-to-end
  - Console app: batch HTML-to-PDF conversion
  - Razor template gallery: invoice, report, letter, certificate templates
- **Migration guide** from SelectPdf / wkhtmltopdf / Puppeteer: "if you used X, here's the EggPdf equivalent"
- **Documentation site** with getting started, API reference, common patterns, troubleshooting
- **EggPdf.Service** -- standalone HTTP microservice (REST API)
  - Deploy as Docker container, Kubernetes sidecar, or shared company service
  - Any language/platform can generate PDFs by calling the REST API (not just .NET)
  - Ready-to-use Docker image published to Docker Hub / GitHub Container Registry

**EggPdf.Service REST API -- Full Feature Parity with Library:**

Every option available in `PdfOptions` is exposed via the REST API. REST callers get 100% of the library's capabilities.

```
=== RENDER ENDPOINTS ===

POST /api/render
  Body: {
    "html": "<h1>Hello</h1>",
    "options": {                           // ALL PdfOptions -- every field optional
      "pageSize": "A4",                    // A4, Letter, Legal, A3, A5, or custom
      "customPageSize": { "width": 210, "height": 297, "unit": "mm" },
      "orientation": "portrait",           // portrait, landscape
      "margins": { "top": 20, "right": 15, "bottom": 20, "left": 15, "unit": "mm" },
      "defaultFont": "Arial",
      "defaultFontSize": 12,
      "title": "My Document",
      "author": "Author",
      "subject": "Subject",
      "keywords": "pdf, report",
      "mediaType": "print",               // print, screen
      "userStyleSheet": "@page { margin: 2cm; }",
      "baseUrl": "https://example.com/",
      "pdfVersion": "1.7",                // 1.4, 1.5, 1.7, 2.0
      "compression": true,
      "linearize": false,
      "header": { "left": "", "center": "{{title}}", "right": "{{date}}", "fontSize": 9 },
      "footer": { "left": "", "center": "Page {{page}} of {{pages}}", "right": "", "fontSize": 8, "lineAbove": true },
      "imageOptimization": { "maxImageDpi": 150, "jpegQuality": 85, "convertPngToJpeg": false },
      "viewerPreferences": { "displayDocTitle": true, "pageLayout": "singlePage" },
      "generateTableOfContents": false,
      "shrinkToFit": false,
      "taggedPdf": false,                 // PDF/UA accessibility
      "pdfAConformance": null,            // "1b", "2b", "3b", or null
      "encryption": { "userPassword": "", "ownerPassword": "", "allowPrinting": true, "allowCopying": true },
      "watermark": { "text": "DRAFT", "opacity": 0.3, "rotation": -45, "fontSize": 72 },
      "batesNumbering": { "prefix": "CASE-", "startNumber": 1, "digits": 6, "position": "bottomRight" },
      "debugLayout": false,               // Draw box boundaries for debugging
      "resourceOptions": {
        "allowExternalUrls": true,         // Fetch images/fonts/CSS from http(s) URLs
        "allowedDomains": null,            // null = all domains allowed, or ["cdn.example.com", "fonts.googleapis.com"]
        "timeoutSeconds": 10,
        "maxResponseSizeMb": 50,
        "cacheEnabled": true,
        "cacheExpirationMinutes": 60
      }
    }
  }
  Response: application/pdf (streamed)
  Headers: X-EggPdf-Pages (page count), X-EggPdf-Warnings (warning count)

POST /api/render/image
  Body: {
    "html": "<h1>Hello</h1>",
    "imageOptions": {
      "format": "png",                    // png, jpeg
      "dpi": 150,
      "pageNumber": 1,                    // which page to render (1-based)
      "quality": 85                       // jpeg quality (ignored for png)
    },
    "options": { ... }                    // same PdfOptions as above
  }
  Response: image/png or image/jpeg

POST /api/render/pages
  Body: {
    "html": "<h1>Hello</h1>",
    "pageRange": { "start": 3, "end": 7 },  // render only pages 3-7
    "options": { ... }
  }
  Response: application/pdf

POST /api/render/url
  Body: {
    "url": "https://example.com/report",
    "options": { ... }                    // same PdfOptions
  }
  Response: application/pdf

POST /api/render/razor
  Body: {
    "template": "Invoice",
    "model": { "id": 123, "customer": "Acme" },
    "options": { ... }
  }
  Response: application/pdf

=== PDF UTILITY ENDPOINTS ===

POST /api/merge
  Body: {
    "documents": [
      { "pdf": "base64-encoded-pdf-bytes", "label": null },
      { "pdf": "base64-encoded-pdf-bytes", "label": { "style": "roman" } },
      { "pdf": "base64-encoded-pdf-bytes", "label": { "style": "decimal", "start": 1 } }
    ]
  }
  Response: application/pdf (merged)

POST /api/sign
  Body: {
    "pdf": "base64-encoded-pdf-bytes",
    "certificate": "base64-encoded-pfx",
    "password": "cert-password",
    "signOptions": {
      "reason": "Approved",
      "location": "New York",
      "visible": true,
      "page": 1,
      "position": { "x": 400, "y": 50, "width": 150, "height": 50 }
    }
  }
  Response: application/pdf (signed)

POST /api/encrypt
  Body: {
    "pdf": "base64-encoded-pdf-bytes",
    "userPassword": "open-password",
    "ownerPassword": "edit-password",
    "permissions": { "printing": true, "copying": false, "modifying": false }
  }
  Response: application/pdf (encrypted)

POST /api/attachments
  Body: {
    "pdf": "base64-encoded-pdf-bytes",
    "files": [
      { "name": "invoice.xml", "data": "base64-encoded-xml", "relationship": "alternative" }
    ]
  }
  Response: application/pdf (with attachments)

=== INFO ENDPOINTS ===

GET /health
  Response: { "status": "healthy", "version": "1.0.0", "uptime": "2h 15m", "activeRenders": 3 }

GET /health/ready
  Response: { "ready": true }  // false if memory pressure or queue full

GET /api/info
  Response: {
    "version": "1.0.0",
    "features": ["pdf-a", "pdf-ua", "signatures", "forms", "barcodes", ...],
    "limits": { "maxBodySize": "10MB", "timeoutSeconds": 30, "maxConcurrentRenders": 4 },
    "supportedPageSizes": ["A4", "Letter", "Legal", "A3", "A5"],
    "supportedImageFormats": ["png", "jpeg"],
    "supportedBarcodes": ["qrcode", "code128", "code39", "ean13", "pdf417", "datamatrix"]
  }

GET /api/fonts
  Response: { "fonts": ["Arial", "Helvetica", "Times New Roman", ...] }

GET /metrics
  Response: (Prometheus format) request_count, request_duration_seconds, render_pages_total, active_renders, memory_bytes, font_cache_hits
```

**Authentication (optional, off by default):**

Configurable via environment variables or `appsettings.json`. All layers are independent -- use none, one, or combine.

| Auth Layer | Config | How It Works |
|---|---|---|
| **None** (default) | `AUTH_ENABLED=false` | No auth. Suitable for internal networks, sidecar deployments, behind a gateway |
| **API Key** | `AUTH_MODE=ApiKey` `AUTH_API_KEYS=key1,key2,key3` | Client sends `X-Api-Key: key1` header. Simple, good for service-to-service |
| **JWT Bearer** | `AUTH_MODE=Jwt` `AUTH_JWT_AUTHORITY=https://auth.example.com` `AUTH_JWT_AUDIENCE=eggpdf` | Validates JWT token from any OpenID Connect provider (Auth0, Keycloak, Azure AD, etc.) |
| **Basic Auth** | `AUTH_MODE=Basic` `AUTH_BASIC_USERS=admin:pass123` | Username/password. Simple, for quick setups behind HTTPS |
| **Custom** | Implement `IEggPdfAuthHandler` | Register your own auth logic via DI for full control |

```json
// appsettings.json example
{
  "EggPdf": {
    "Auth": {
      "Enabled": true,
      "Mode": "ApiKey",
      "ApiKeys": ["prod-key-abc123", "staging-key-xyz789"]
    },
    "RateLimiting": {
      "Enabled": true,
      "RequestsPerMinute": 60
    },
    "MaxRequestBodySize": "10MB",
    "TimeoutSeconds": 30
  }
}
```

**Additional service hardening options:**
- **Rate limiting** -- configurable requests/minute per client (prevents abuse)
- **Request size limit** -- max HTML body size (prevents OOM from huge payloads)
- **Render timeout** -- max time per render (prevents hung requests from adversarial input)
- **CORS** -- configurable allowed origins for browser-based calls
- **HTTPS** -- enforced in production via standard ASP.NET Core Kestrel config

**Distribution: Docker Images, CLI Binaries, Web UI**

EggPdf ships in multiple forms so anyone can use it -- .NET developer, Python developer, DevOps engineer, or non-technical user.

### Docker Images (2 images published to Docker Hub + GitHub Container Registry)

**1. `eggpdf/service` -- REST API server**
```bash
# Run the REST API service
docker run -p 8080:8080 eggpdf/service:latest

# With custom fonts mounted
docker run -p 8080:8080 -v /path/to/fonts:/app/fonts eggpdf/service:latest

# With auth enabled
docker run -p 8080:8080 -e AUTH_ENABLED=true -e AUTH_MODE=ApiKey -e AUTH_API_KEYS=my-key eggpdf/service:latest

# Generate PDF from any language
curl -X POST http://localhost:8080/api/render \
  -H "Content-Type: application/json" \
  -d '{"html": "<h1>Hello!</h1>"}' \
  -o output.pdf
```

**2. `eggpdf/cli` -- CLI tool (no server, just convert files)**
```bash
# Convert HTML file to PDF
docker run -v $(pwd):/work eggpdf/cli /work/input.html -o /work/output.pdf

# Convert with options
docker run -v $(pwd):/work eggpdf/cli /work/input.html -o /work/output.pdf --page-size A4 --margin 2cm

# Batch convert all HTML files in a directory
docker run -v $(pwd):/work eggpdf/cli /work/*.html -o /work/output/ --batch

# Pipe HTML from stdin
echo "<h1>Hello</h1>" | docker run -i eggpdf/cli - -o - > output.pdf
```

**Dockerfile specs:**
- Base image: `mcr.microsoft.com/dotnet/aspnet:9.0-alpine` (minimal, ~80MB)
- Multi-stage build: build on `sdk`, run on `aspnet`
- Common system fonts pre-installed: Liberation Sans, Noto Sans, Noto Color Emoji
- Non-root user for security
- Health check built-in (service image)

### CLI Single-File Binaries (no .NET required)

Pre-built self-contained single-file executables for every major platform. Users download one file, run it. No .NET SDK or runtime needed.

| Platform | Binary | Size (est.) |
|---|---|---|
| Windows x64 | `eggpdf-win-x64.exe` | ~30-40MB |
| Windows ARM64 | `eggpdf-win-arm64.exe` | ~30-40MB |
| Linux x64 | `eggpdf-linux-x64` | ~30-40MB |
| Linux ARM64 | `eggpdf-linux-arm64` | ~30-40MB |
| macOS x64 | `eggpdf-osx-x64` | ~30-40MB |
| macOS ARM64 (Apple Silicon) | `eggpdf-osx-arm64` | ~30-40MB |

Built with:
```bash
dotnet publish src/EggPdf.Cli -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
```

**Published as GitHub Release assets** -- every release has all 6 binaries attached.

Usage:
```bash
# Download (Linux example)
curl -L https://github.com/eggspot/EggPdf/releases/latest/download/eggpdf-linux-x64 -o eggpdf
chmod +x eggpdf

# Convert
./eggpdf input.html -o output.pdf
./eggpdf input.html -o output.png --format png --dpi 150
./eggpdf input.html --watch   # watch mode: re-render on file change
./eggpdf --serve              # start REST API server (same as Docker service image)
```

### Web UI (`EggPdf.WebUI`)

A simple, self-hosted web interface for non-technical users. Open a browser, paste HTML, get PDF.

**Features:**
- HTML editor with syntax highlighting (using a lightweight JS editor like CodeMirror / Monaco)
- Live preview: see PDF output as you type (debounced)
- CSS editor panel alongside HTML
- Options panel: page size, orientation, margins, headers/footers
- Download PDF / PNG button
- Upload HTML file
- Template gallery: pre-built templates (invoice, report, letter, certificate) that users can customize
- Responsive: works on desktop and tablet

**How it works:**
- Single-page application (HTML/CSS/JS) served by the EggPdf.Service
- Calls the same REST API endpoints (`/api/render`, `/api/render/image`)
- No separate frontend build system -- static files bundled with the service

**Accessed at:** `http://localhost:8080/` (root of the service)

```bash
# Start with Web UI (default)
docker run -p 8080:8080 eggpdf/service:latest
# Open http://localhost:8080 in browser

# Start without Web UI (API only)
docker run -p 8080:8080 -e WEBUI_ENABLED=false eggpdf/service:latest
```

**docker-compose.yml** (full stack):
```yaml
version: '3.8'
services:
  eggpdf:
    image: eggpdf/service:latest
    ports:
      - "8080:8080"
    environment:
      - AUTH_ENABLED=false
      - WEBUI_ENABLED=true
    volumes:
      - ./fonts:/app/fonts          # Custom fonts
      - ./templates:/app/templates  # Razor templates
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 5s
      retries: 3
```

**Observability (production-ready):**
- **Prometheus metrics** at `GET /metrics`: request count, duration histogram (p50/p95/p99), error rate, active concurrent renders, memory usage, font cache hit rate
- **OpenTelemetry** support: distributed traces (trace a render request through parse → layout → paint → write stages), OTEL metrics export
- **Structured logging**: JSON logs with correlation IDs, render duration, page count, warnings
- **Health check**: `GET /health` returns status + version + uptime + active render count
- **Kubernetes-ready**: liveness probe (`/health`), readiness probe (`/health/ready` -- returns unhealthy if memory pressure or render queue full)

**Deliverable:** ASP.NET Core developers can generate PDFs from Razor templates with a single line of code. Non-.NET teams can use the REST API. Complete onboarding experience for new users.

### Phase 13: Business PDF Features
**Goal:** PDF features required by enterprise/business use cases.

**Digital signatures:**
- Signature form fields (`/FT /Sig`) with visible appearance (signer name, date, optional image)
- CMS/PKCS#7 detached signatures with X.509 certificates
- PAdES B-B (basic signature) and B-T (with RFC 3161 timestamp)
- Certification signature with DocMDP (control allowed modifications after signing)
- API: `PdfSigner.Sign(pdfBytes, certificate, options)` -- signs an already-generated PDF

**PDF forms (AcroForm):**
- Generate fillable form fields from HTML `<input>`, `<select>`, `<textarea>`, `<button>`
- Text fields, checkboxes, radio buttons, dropdowns
- Pre-filled from HTML values. Optionally left editable in the PDF
- API: `PdfOptions.FormMode = FormMode.Fillable` (default: `ReadOnly` -- just renders visually)

**File attachments (ZUGFeRD / Factur-X):**
- Embed arbitrary files within the PDF (XML, CSV, JSON)
- ZUGFeRD/Factur-X support: PDF/A-3 with embedded XML invoice data
- `/AFRelationship` for associated file types
- API: `PdfOptions.Attachments.Add("invoice.xml", xmlBytes, relationship: Alternative)`

**Barcode / QR code generation:**
- QR Code, Code 128, Code 39, EAN-13, PDF417, Data Matrix
- Rendered as vector paths (resolution-independent, crisp at any zoom)
- Via custom HTML attribute: `<div data-eggpdf-qrcode="https://...">` or overlay API

**Document merging:**
- Combine multiple PDFs into one: `PdfMerger.Add(pdf1).Add(pdf2).Build()`
- Merged outlines, page labels, resource deduplication
- API: `var merged = new PdfMerger().Add(coverPdf).Add(bodyPdf, pageLabel: Decimal).Build()`

**Deliverable:** EggPdf covers invoicing (ZUGFeRD), contracts (digital signatures), enterprise reports (merging, page labels), logistics (barcodes), and regulatory compliance (PDF/A, PDF/UA).

### Phase 14: Commercial Print + Advanced Compliance
**Goal:** Features needed for commercial printing and strict compliance.

**Print production (PDF/X):**
- PDF/X-4 conformance for prepress workflows
- TrimBox / BleedBox / CropBox page geometry
- Crop marks, registration marks, color bars in MediaBox margin
- CMYK color space, spot colors (Separation), multi-spot (DeviceN)
- ICC color profiles for device-independent color reproduction
- API: `PdfOptions.PrintProduction = new PrintProductionOptions { Bleed = 3mm, TrimMarks = true }`

**Optional Content Groups (layers):**
- Content on togglable layers (OCGs)
- Print-only and screen-only layers
- Use cases: spot color plates, security features on certificates, draft watermarks

**Long-Term Validation (LTV) for signatures:**
- Document Security Store (DSS) with CRLs and OCSP responses
- PAdES B-LT and B-LTA profiles for decade-long verification

**PDF/A-3 + ZUGFeRD v2.3:**
- Full Factur-X/ZUGFeRD conformance with proper XMP extension schemas
- Automated validation of embedded XML against Factur-X schema

**Deliverable:** EggPdf produces print-ready PDFs for commercial printing and meets the strictest compliance requirements for archival, e-invoicing, and long-term signature validation.

---

## Testing Strategy

Testing is **not an afterthought** -- it is foundational to this project. A rendering engine without automated conformance testing will drift from spec and accumulate regressions with every feature added. We adopt testing patterns proven by WeasyPrint, Servo, and Chromium.

### Test Architecture Overview

```
EggPdf Test Pyramid
====================

Layer 5: REAL-WORLD CORPUS          (~50 tests)
         Saved HTML from real sites, compared to Chrome print output

Layer 4: WPT CONFORMANCE            (~5,000+ tests, growing)
         Web Platform Tests reftests, adapted for our engine

Layer 3: VISUAL REGRESSION           (~500+ tests)
         ASCII pixel-art tests (WeasyPrint style) + golden image tests

Layer 2: LAYOUT ASSERTION            (~2,000+ tests)
         Feed HTML/CSS, assert box positions/sizes numerically

Layer 1: UNIT TESTS                  (~3,000+ tests)
         Parsers, cascade, selectors, PDF output -- isolated components
```

### Layer 1: Unit Tests (per component, fast, isolated)

Run in < 10 seconds total. No rendering, no PDF output. Pure logic testing.

| Component | What We Test | Test Data Source |
|---|---|---|
| **HTML Tokenizer** | Token output for edge cases: unclosed tags, entities, CDATA, comments, doctype variants | **html5lib-tests** tokenizer suite (JSON format: input -> expected tokens) |
| **HTML Tree Builder** | DOM tree construction: foster parenting, implicit closure, fragment parsing, error recovery | **html5lib-tests** tree-construction suite (.dat format: input HTML -> expected DOM tree) |
| **CSS Tokenizer** | Token types: idents, strings, numbers, dimensions, urls, functions, escape sequences | Custom suite + CSS WG examples |
| **CSS Parser** | Parsed values per property, shorthand expansion, @-rule parsing, error recovery | Custom suite |
| **Selector Engine** | Matching against known DOM trees: combinators, pseudo-classes, specificity ordering | Custom suite + adapted WPT selector tests |
| **Cascade** | Computed style resolution: specificity, origin, !important, inherit/initial/unset, var() | Custom suite |
| **Value Resolution** | Unit conversion (em->px, %->px), calc() evaluation, clamp/min/max | Custom suite |
| **TrueType Parser** | Table parsing: cmap, hmtx, hhea, head, OS/2, kern, GPOS from real .ttf files | Real font files (DejaVu Sans, Liberation Mono -- open license) |
| **Font Subsetter** | Subset output is valid TrueType; contains exactly the requested glyphs | Round-trip: subset -> parse subset -> verify glyphs |
| **PDF Writer** | Valid PDF structure: xref table offsets, object numbering, stream lengths, compression | Parse output with a PDF reader; validate byte-level structure |
| **PDF Content Stream** | Correct operators for text, graphics, images, state management | Custom suite |

**html5lib-tests integration:** We vendor the [html5lib-tests](https://github.com/html5lib/html5lib-tests) repo as a git submodule. A test runner parses the `.dat` and `.test` files and feeds them to our parser. This is the **authoritative** test suite for HTML5 parsing correctness -- used by Chromium, Firefox, AngleSharp, and every serious HTML parser.

```
tests/
|-- testdata/
|   |-- html5lib-tests/          # git submodule
|   |   |-- tree-construction/   # .dat files: input HTML -> expected DOM
|   |   |-- tokenizer/           # .test JSON files: input -> expected tokens
```

### Layer 2: Layout Assertion Tests (numeric, deterministic)

Test the layout engine by feeding HTML/CSS and asserting **exact positions and sizes** of boxes. No rendering -- we inspect the layout tree directly.

```csharp
[Fact]
public void BlockLayout_ChildrenStackVertically()
{
    var layout = LayoutTestHelper.Layout(
        "<div style='width:100px'>" +
        "  <div id='a' style='height:30px'></div>" +
        "  <div id='b' style='height:50px'></div>" +
        "</div>");

    var a = layout.GetById("a");
    var b = layout.GetById("b");

    Assert.Equal(0, a.Y);
    Assert.Equal(30, a.Height);
    Assert.Equal(30, b.Y);     // stacks below 'a'
    Assert.Equal(50, b.Height);
}

[Fact]
public void MarginCollapse_AdjacentSiblings()
{
    var layout = LayoutTestHelper.Layout(
        "<div style='margin-bottom:20px'></div>" +
        "<div style='margin-top:30px'></div>");

    // Collapsed margin = max(20, 30) = 30, not 50
    var gap = layout.Children[1].Y - layout.Children[0].Bottom;
    Assert.Equal(30, gap);
}
```

These tests are **fast** (no PDF generation, no rendering) and **deterministic** (no pixel comparison). They are the primary way to test the layout engine during development.

Organized by layout mode:
```
tests/EggPdf.Tests.Layout/
|-- BlockFormattingContextTests.cs
|-- InlineFormattingContextTests.cs
|-- FlexLayoutTests.cs
|-- GridLayoutTests.cs
|-- TableLayoutTests.cs
|-- FloatTests.cs
|-- PositionedLayoutTests.cs
|-- MarginCollapseTests.cs
|-- IntrinsicSizingTests.cs
```

### Layer 3: Visual Regression Tests

Two complementary approaches:

#### 3a. ASCII Pixel-Art Tests (inspired by WeasyPrint)

For small, focused visual tests. Each character maps to a pixel color. Human-readable, self-contained, no golden files to manage.

```csharp
[Fact]
public void Border_SolidRed_1px()
{
    AssertPixels(10, 5, @"
        __________
        _rrrrrrrr_
        _r______r_
        _rrrrrrrr_
        __________
    ", "<div style='border:1px solid red; width:6px; height:1px; margin:1px'>");
}

// Legend: _ = white, r = red, b = blue, B = black, g = green, etc.
```

This requires our engine to render to an in-memory bitmap (not just PDF). We implement a simple **raster backend** alongside the PDF backend -- both consume the same paint command list.

#### 3b. Golden Image Tests (reference PNG comparison)

For complex layouts where ASCII art is impractical:

1. Render HTML to PDF via EggPdf
2. Rasterize PDF to PNG (using our raster backend, or `pdftoppm` as external tool)
3. Compare pixel-by-pixel against a golden PNG

**Comparison parameters:**
- Per-pixel threshold: **0.1** (tolerates anti-aliasing)
- Max differing pixels: **< 1%** of total
- Uses same fuzzy matching model as WPT: `maxDifference` + `totalPixels`

**Golden file management:**
- Stored in `tests/golden/` organized by test name
- Regenerated explicitly via `dotnet test --filter UpdateGolden`
- Changes to golden files require code review (visible in PR diffs as image changes)

### Layer 4: WPT Conformance Tests

The [Web Platform Tests](https://github.com/web-platform-tests/wpt) repo contains **20,000-30,000+ CSS tests**. We don't need to run them all -- we adopt the subset relevant to print rendering.

#### How WPT Reftests Work

Each test is two HTML files:
- **Test file**: Uses the CSS feature being tested
- **Reference file**: Produces the same visual output using simpler/well-known techniques

```html
<!-- test.html -->
<link rel="match" href="reference.html">
<div style="display:flex; justify-content:center">
  <div style="width:50px; height:50px; background:green"></div>
</div>

<!-- reference.html -->
<div style="width:50px; height:50px; background:green; margin:0 auto"></div>
```

If both render identically, the test passes.

#### Our WPT Runner

```
tools/EggPdf.WptRunner/
|-- WptTestDiscovery.cs    # Parse WPT manifest, find CSS reftests
|-- WptTestRunner.cs       # Render test + reference, compare
|-- WptFuzzyMatcher.cs     # Handle <meta name="fuzzy"> tolerances
|-- WptReport.cs           # Generate pass/fail report + dashboard
```

Pipeline:
1. Clone/vendor the `wpt` repo (just the `css/` directory)
2. Parse the manifest to find reftest pairs
3. For each test: render test HTML -> bitmap, render reference HTML -> bitmap
4. Compare with fuzzy tolerance from the test's `<meta name="fuzzy">` tag
5. Record PASS/FAIL/TIMEOUT/CRASH
6. Generate report with pass rate per CSS module

**WPT pass rate is our primary metric for Chrome parity.** We track it per module:

| CSS Module | Target Pass Rate (Phase 4) | Target (Phase 11) |
|---|---|---|
| CSS2 (box model, floats, positioning) | 80% | 92% |
| css-flexbox | - | 85% |
| css-grid | - | 80% |
| css-text | 70% | 85% |
| css-backgrounds | 75% | 90% |
| css-values | 80% | 90% |
| css-cascade | 85% | 95% |
| css-selectors | 90% | 95% |

### Layer 5: Real-World Corpus Tests

Saved HTML from real websites + Chrome's print output as reference:

```
tests/corpus/
|-- invoice-stripe/
|   |-- input.html
|   |-- chrome-reference.pdf     # Chrome headless --print-to-pdf
|   |-- chrome-reference.png     # Rasterized from Chrome PDF
|-- tailwind-dashboard/
|-- bootstrap-docs/
|-- github-readme/
|-- wikipedia-article/
|-- google-docs-export/
```

These are **not** pass/fail in CI -- they are tracked as a **visual diff report** that reviewers inspect. They catch real-world regressions that synthetic tests miss.

### End-to-End (E2E) Tests

E2E tests validate the **complete user journey**: HTML in -> PDF out -> verify the PDF is correct and usable. These are distinct from visual regression tests because they verify **semantic correctness** (not just pixel appearance).

```csharp
// --- E2E: PDF structure and content verification ---
[Fact]
public async Task E2E_Invoice_ProducesValidPdf()
{
    string html = File.ReadAllText("testdata/invoice.html");
    byte[] pdf = await _converter.RenderAsync(html);

    // 1. PDF is valid and opens without error
    var doc = PdfTestReader.Open(pdf);
    Assert.True(doc.IsValid);

    // 2. Correct number of pages
    Assert.Equal(2, doc.PageCount);

    // 3. Text content is extractable and correct
    string text = doc.ExtractAllText();
    Assert.Contains("Invoice #1234", text);
    Assert.Contains("$1,250.00", text);

    // 4. Hyperlinks exist and point to correct URLs
    var links = doc.GetLinkAnnotations();
    Assert.Contains(links, l => l.Uri == "https://example.com/terms");

    // 5. Bookmarks / outlines generated from headings
    var outlines = doc.GetOutlines();
    Assert.Equal("Invoice #1234", outlines[0].Title);

    // 6. Images are embedded (not missing)
    var images = doc.GetImageXObjects();
    Assert.True(images.Count >= 1); // Logo

    // 7. Fonts are embedded and text is selectable
    var fonts = doc.GetEmbeddedFonts();
    Assert.True(fonts.Count >= 1);

    // 8. PDF metadata
    Assert.Equal("Invoice #1234", doc.Info.Title);
}

// --- E2E: Stream output produces identical result to byte[] ---
[Fact]
public async Task E2E_StreamOutput_MatchesByteOutput()
{
    string html = "<h1>Hello</h1><p>World</p>";
    byte[] bytesResult = await _converter.RenderAsync(html);

    using var ms = new MemoryStream();
    await _converter.RenderAsync(html, ms);
    byte[] streamResult = ms.ToArray();

    Assert.Equal(bytesResult, streamResult);
}

// --- E2E: File output works ---
[Fact]
public async Task E2E_FileOutput_CreatesValidPdf()
{
    string html = "<h1>Test</h1>";
    string path = Path.GetTempFileName() + ".pdf";

    await _converter.RenderToFileAsync(html, path);

    Assert.True(File.Exists(path));
    byte[] bytes = File.ReadAllBytes(path);
    Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    Assert.True(bytes.Length > 100);

    File.Delete(path);
}

// --- E2E: CancellationToken actually stops rendering ---
[Fact]
public async Task E2E_CancellationToken_StopsRendering()
{
    string hugeHtml = GenerateHugeHtml(10_000_elements);
    var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

    await Assert.ThrowsAsync<OperationCanceledException>(
        () => _converter.RenderAsync(hugeHtml, cts.Token));
}

// --- E2E: Cross-reader compatibility ---
[Theory]
[InlineData("simple-text")]
[InlineData("table-with-images")]
[InlineData("flexbox-layout")]
[InlineData("multi-page-with-bookmarks")]
public async Task E2E_PdfOpensInMultipleReaders(string testCase)
{
    string html = File.ReadAllText($"testdata/e2e/{testCase}.html");
    byte[] pdf = await _converter.RenderAsync(html);

    // Validate with our PDF parser
    Assert.True(PdfTestReader.IsValidPdf(pdf));

    // Validate PDF structure (xref, trailer, object streams)
    var validation = PdfStructureValidator.Validate(pdf);
    Assert.Empty(validation.Errors);
    Assert.Empty(validation.Warnings);
}
```

**What we need for E2E tests:**

| Tool | Purpose |
|---|---|
| `PdfTestReader` | Lightweight PDF reader (we build this) that extracts text, links, outlines, images, fonts, metadata from our PDFs. Does NOT use our PDF writer -- independent validation |
| `PdfStructureValidator` | Validates PDF byte-level structure: xref offsets, trailer, object counts, stream lengths. Catches serialization bugs |

E2E tests run in the **integration test** project:
```
tests/EggPdf.Tests.Integration/
|-- E2E/
|   |-- BasicRenderTests.cs          # Simple HTML -> valid PDF
|   |-- TextExtractionTests.cs       # Rendered text is extractable/correct
|   |-- HyperlinkTests.cs            # <a href> -> PDF link annotations
|   |-- BookmarkTests.cs             # Headings -> PDF outlines
|   |-- ImageEmbeddingTests.cs       # Images embedded correctly
|   |-- FontEmbeddingTests.cs        # Fonts embedded, text selectable
|   |-- MetadataTests.cs             # Title, author, dates
|   |-- StreamOutputTests.cs         # byte[] vs Stream vs File consistency
|   |-- CancellationTests.cs         # CancellationToken works mid-render
|   |-- CrossReaderTests.cs          # PDF structure validation
|   |-- PdfAComplianceTests.cs       # PDF/A mode produces compliant output
|   |-- EncryptionTests.cs           # Password-protected PDFs
|   |-- MultiTargetTests.cs          # Same results on net8.0 and netstandard2.0
|-- Helpers/
|   |-- PdfTestReader.cs             # Read-only PDF parser for verification
|   |-- PdfStructureValidator.cs     # Byte-level PDF structure checks
|-- testdata/
    |-- e2e/                         # HTML fixtures for E2E tests
```

### Fuzz Testing

Both parsers (HTML and CSS) process untrusted input and must not crash, hang, or consume unbounded memory.

```
tests/EggPdf.Tests.Fuzz/
|-- HtmlTokenizerFuzz.cs     # Feed random bytes to HTML tokenizer
|-- HtmlParserFuzz.cs        # Feed random bytes to HTML parser
|-- CssTokenizerFuzz.cs      # Feed random bytes to CSS tokenizer
|-- CssParserFuzz.cs         # Feed random bytes to CSS parser
|-- LayoutFuzz.cs            # Feed random styled DOM trees to layout engine
```

**Approach:**
- Use [SharpFuzz](https://github.com/Metalnem/sharpfuzz) (AFL-based .NET fuzzer) or custom random input generation
- Fuzz targets: tokenize/parse arbitrary byte sequences, assert no crash and no infinite loop
- Add timeout guards (e.g., 5 seconds max per parse) to catch infinite loops
- WeasyPrint found that adversarial input can cause infinite rendering loops -- we must guard against this from day 1

### Performance Benchmarks

```
benchmarks/EggPdf.Benchmarks/
|-- ParseBenchmarks.cs         # HTML + CSS parsing throughput (MB/s)
|-- LayoutBenchmarks.cs        # Layout speed for various complexities
|-- RenderBenchmarks.cs        # End-to-end HTML -> PDF time
|-- MemoryBenchmarks.cs        # Peak memory during rendering
|-- FontBenchmarks.cs          # Font loading + subsetting speed
|-- ScaleBenchmarks.cs         # 100-page, 1000-row table, deep nesting
|-- ConcurrencyBenchmarks.cs   # Parallel renders on shared converter instance
|-- StreamingBenchmarks.cs     # Stream output vs byte[] overhead comparison
```

Using **BenchmarkDotNet** (the one benchmark-only dev dependency in test projects, not in the library itself).

#### Performance Targets

| Scenario | Target Time | Target Memory | Test HTML |
|---|---|---|---|
| Simple page (1 page, text only) | < 50ms | < 10MB | `<h1>Hello</h1><p>World</p>` |
| Invoice (1 page, table + logo) | < 100ms | < 20MB | Table with 20 rows, 1 image |
| Report (10 pages, mixed content) | < 1s | < 50MB | Headings, paragraphs, tables, images |
| Large table (100 pages) | < 5s | < 200MB | 5,000-row table |
| Bootstrap-heavy CSS (1 page) | < 500ms | < 50MB | 1 page with 5,000+ CSS rules |
| Concurrent renders (10 parallel) | < 2s total | < 100MB/render | Invoice template x10 |

#### Regression Detection

Performance regressions are caught automatically:

```
benchmarks/
|-- baselines/                     # Stored baseline results (JSON)
|   |-- latest.json               # Most recent accepted baseline
|-- EggPdf.Benchmarks.Regression/  # Regression test runner
    |-- RegressionChecker.cs       # Compare current run vs baseline
```

**How it works:**
1. Weekly CI run executes all benchmarks on a **fixed-spec runner** (same CPU/memory every time)
2. Results are compared against `baselines/latest.json`
3. If any benchmark regresses by **>15%** in time or **>25%** in memory, CI **fails with a warning**
4. Developers review the regression. If intentional (e.g., new feature adds overhead), update baseline: `dotnet run --project EggPdf.Benchmarks.Regression -- --update-baseline`
5. Baseline file is committed to git -- changes visible in PR diffs

**Pipeline stage breakdown benchmarks:**

Each benchmark also reports per-stage timing:
```
[Benchmark] RenderInvoice
  Total: 85ms
    Parse HTML:    5ms  (6%)
    Parse CSS:     3ms  (4%)
    Cascade:      12ms  (14%)
    Layout:       35ms  (41%)
    Fragmentation: 8ms  (9%)
    Paint:        10ms  (12%)
    PDF Write:    12ms  (14%)
```

This helps identify which stage to optimize when performance degrades.

### CI Pipeline

```yaml
# === On every PR and push to main ===
stages:
  - unit-tests:                # Layer 1 -- < 30 seconds
      run: dotnet test EggPdf.Tests.Unit
      gate: MUST PASS to merge

  - layout-tests:              # Layer 2 -- < 60 seconds
      run: dotnet test EggPdf.Tests.Layout
      gate: MUST PASS to merge

  - visual-tests:              # Layer 3 -- < 5 minutes
      run: dotnet test EggPdf.Tests.Visual
      gate: MUST PASS to merge

  - e2e-tests:                 # E2E -- < 3 minutes
      run: dotnet test EggPdf.Tests.Integration
      gate: MUST PASS to merge

  - wpt-tests:                 # Layer 4 -- < 15 minutes (subset)
      run: dotnet run --project tools/EggPdf.WptRunner -- --report
      gate: Pass rate must not decrease vs. main branch

  - multi-target-build:        # Verify builds on all target frameworks
      run: dotnet build -c Release
      matrix: [netstandard2.0, netstandard2.1, net6.0, net8.0, net9.0]
      gate: MUST PASS to merge

  - multi-platform:            # Test on all OS platforms
      run: dotnet test EggPdf.Tests.Unit && dotnet test EggPdf.Tests.Integration
      os-matrix: [windows-latest, ubuntu-latest, macos-latest]
      gate: MUST PASS to merge (font paths, encoding, line endings differ per OS)

# === Weekly (scheduled) ===
  - benchmarks:
      run: dotnet run --project benchmarks/EggPdf.Benchmarks -- --export baselines/current.json
      gate: WARN if >15% regression vs baseline (auto-creates issue)

  - benchmark-regression:
      run: dotnet run --project benchmarks/EggPdf.Benchmarks.Regression -- --compare baselines/latest.json
      gate: FAIL if >30% regression (blocks release)

  - full-wpt:                  # Full WPT suite (not just subset)
      run: dotnet run --project tools/EggPdf.WptRunner -- --full --report
      gate: Informational (tracks progress over time)

# === Nightly ===
  - fuzz:
      run: dotnet run --project tests/EggPdf.Tests.Fuzz -- --duration 3600
      gate: FAIL on any crash or hang

  - corpus-visual-diff:        # Layer 5 -- real-world sites
      run: dotnet run --project tools/EggPdf.ChromeRef -- --diff
      gate: Informational (generates visual diff report for review)

# === On release (tag) ===
  - all-above: true
  - pdfa-validation:           # Validate PDF/A conformance
      run: java -jar veraPDF.jar --flavour 2b --format mrr output-pdfa.pdf
      gate: MUST PASS for release

  - pdfua-validation:          # Validate PDF/UA accessibility
      run: dotnet run --project tools/EggPdf.PdfUaValidator -- output-tagged.pdf
      gate: MUST PASS for release

  - netfx-integration:         # Test on actual .NET Framework 4.6.2 runtime
      run: dotnet test EggPdf.Tests.Integration -f net462
      os: windows-latest
      gate: MUST PASS for release

  - nuget-pack:
      run: dotnet pack -c Release --include-symbols
  - nuget-push:
      run: dotnet nuget push **/*.nupkg --source nuget.org
```

### Test Matrix

| Test Type | Runs On | Time | Gate? | Purpose |
|---|---|---|---|---|
| Unit tests | Every PR | < 30s | Merge blocker | Parser, cascade, selector, PDF writer correctness |
| Layout tests | Every PR | < 60s | Merge blocker | Box positions and sizes |
| Visual tests | Every PR | < 5min | Merge blocker | Pixel-level rendering correctness |
| E2E tests | Every PR | < 3min | Merge blocker | Full journey: HTML in, valid PDF out, content correct |
| WPT subset | Every PR | < 15min | Pass rate gate | CSS spec conformance (must not regress) |
| Multi-target build | Every PR | < 2min | Merge blocker | Compiles on all .NET targets |
| Benchmarks | Weekly | ~10min | Warn on regression | Performance tracking |
| Benchmark regression | Weekly | ~5min | Block release if >30% | Prevents shipping slow code |
| Full WPT | Weekly | ~1hr | Informational | Track overall progress toward Chrome parity |
| Fuzz testing | Nightly | 1hr | Crash = fail | Find parser/layout bugs from random input |
| Corpus visual diff | Nightly | ~15min | Informational | Catch real-world regressions |

### Test Infrastructure We Build

| Tool | Purpose |
|---|---|
| `LayoutTestHelper` | Parse HTML, run layout, return queryable layout tree for assertions |
| `AssertPixels()` | ASCII pixel-art comparison (WeasyPrint-style) |
| `RasterBackend` | Render paint commands to in-memory bitmap (needed for visual tests without PDF round-trip) |
| `GoldenFileComparer` | Pixel-diff with configurable threshold + tolerance |
| `PdfTestReader` | Lightweight read-only PDF parser for E2E verification: extract text, links, outlines, images, fonts, metadata. Independent from our PDF writer |
| `PdfStructureValidator` | Byte-level PDF structure validation: xref offsets, trailer, object counts, stream lengths |
| `WptRunner` | Discover, run, and report WPT reftests |
| `html5lib-tests runner` | Parse and execute html5lib tokenizer + tree-construction tests |
| `ChromeReferenceGenerator` | Uses **Playwright** (`page.pdf()`) to generate Chrome print PDFs as baseline references. Playwright is a dev/test dependency only -- never in the library |
| `RegressionChecker` | Compare benchmark results against stored baselines, flag regressions |

**Why Playwright for reference generation, not for E2E?**

Playwright is a browser automation tool -- it drives Chrome. We use it **only** in `tools/EggPdf.ChromeRef/` to generate "what Chrome would produce" as our comparison baseline. The actual E2E tests use our own `PdfTestReader` to verify our PDF output independently. Playwright is a dev dependency of the tooling project, never of the library or test projects.

---

## Risk Assessment

| Risk | Impact | Likelihood | Mitigation | Learned From |
|---|---|---|---|---|
| Layout engine takes too long | Critical | High | Phase-based delivery; block layout alone is useful. Each phase is independently valuable | WeasyPrint took 14 years |
| Page break fragmentation in flex/grid | Critical | High | Design `Split()` into every formatting context from day 1, not as an afterthought | WeasyPrint #2076, #2397 |
| Infinite loops from adversarial HTML/CSS | Critical | Medium | Recursion depth limits, timeout guards, fuzz testing from Phase 1 | WeasyPrint known issue |
| Subtle layout differences vs. Chrome | High | High | WPT reftests from day 1, track pass rates per module. Target 90%+ not 100% | Servo WPT dashboard |
| CSS cascade perf with large stylesheets | High | Medium | Bloom filter for fast selector rejection, index by rightmost selector (Servo pattern) | WeasyPrint perf issues |
| Font handling edge cases | High | Medium | Start with system fonts. Test with real .ttf files (DejaVu, Liberation). Handle composite glyphs | PDFsharp subsetting |
| PNG transparency as black | High | Medium | Separate alpha to SMask explicitly. Test transparent PNGs from Phase 1 | PdfSharpCore #41 |
| Table layout perf for large tables | Medium | Medium | Profile early. Streaming layout for 1000+ row tables | WeasyPrint perf issues |
| Grid layout complexity | High | Medium | Implement after Flexbox. Grid builds on similar concepts | Industry consensus |
| SVG rendering scope | High | Medium | Defer to later phase. Most print content doesn't need SVG | WeasyPrint built custom SVG |
| Pure C# text engine (no HarfBuzz) | High | Medium | Start with basic kerning from kern table. Add GPOS/GSUB incrementally. Complex scripts (Arabic, Thai, Hindi) are later phases | WeasyPrint relies on Pango |
| CJK/Vietnamese/multilingual | High | Medium | Font fallback chain auto-detects CJK. CIDFont embedding for large fonts. Vietnamese NFC normalization. Test with real multilingual documents | Chrome handles natively |
| Thai word segmentation | Medium | Medium | Need word break dictionary (~40K words). Can ship as embedded resource. Fall back to character-level breaks | No standard algorithm |
| SVG rendering complexity | High | High | Prioritize core SVG (shapes, paths, text, gradients). Defer filters, animations. WeasyPrint built a custom SVG renderer post-Cairo removal | WeasyPrint SVG experience |
| WebP decoding in pure C# | Medium | Medium | Lossy WebP is VP8 codec -- complex. Consider: decode via System.Drawing on .NET 6+ where available, or build decoder | No pure C# decoder exists |
| PDF compliance across readers | Medium | Low | Test across Adobe, Chrome, Firefox, Preview, SumatraPDF from Phase 1 | Industry best practice |

---

## Estimated Scale

This is a large project. Rough order-of-magnitude:

| Component | Estimated Lines of Code |
|---|---|
| HTML5 Parser (tokenizer + tree builder + DOM + entities + encoding) | ~8,000-12,000 |
| CSS Parser + Selectors + Shorthand Expansion | ~10,000-15,000 |
| Style Resolution (Cascade + value resolution) | ~5,000-8,000 |
| Block + Inline Layout | ~10,000-15,000 |
| Flex Layout | ~3,000-5,000 |
| Grid Layout | ~4,000-6,000 |
| Table Layout | ~3,000-5,000 |
| Text Engine (fonts, shaping, line-breaking, WOFF, fallback) | ~10,000-15,000 |
| Fragmentation (pagination + paged media) | ~4,000-6,000 |
| Paint Layer | ~3,000-5,000 |
| SVG Engine (parser, renderer, path data, elements) | ~5,000-8,000 |
| PDF Backend core (objects, fonts, images, content streams) | ~8,000-12,000 |
| PDF Business features (signatures, forms, attachments, barcodes, merging) | ~8,000-12,000 |
| PDF Compliance (PDF/A, PDF/UA, PDF/X, page geometry) | ~3,000-5,000 |
| Core (units, primitives, utilities, resource resolver) | ~2,000-3,000 |
| EggPdf.Razor integration | ~500-800 |
| EggPdf.AspNetCore integration | ~300-500 |
| EggPdf.Cli tool | ~200-400 |
| Tests | ~35,000-45,000 |
| **Total** | **~118,000-177,000** |

---

## NuGet Package Strategy

The library ships as multiple NuGet packages. The core is zero-dependency. Integration packages add optional features.

```
NuGet Packages:
===============

EggPdf                        (zero dependencies -- the core library)
  |
  +-- EggPdf.Razor             (depends on: EggPdf + Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation)
  |                            Render .cshtml templates with model data directly to PDF
  |
  +-- EggPdf.AspNetCore        (depends on: EggPdf + Microsoft.AspNetCore.Http.Abstractions)
  |                            Middleware, IActionResult, endpoint integration
  |
Note: SVG rendering is built into the core EggPdf package -- NOT a separate package.
SVG is too common in modern HTML (icons, charts, logos) to be optional.

  +-- EggPdf.Service           (depends on: EggPdf + ASP.NET Core)
                               Standalone HTTP microservice for PDF generation (Docker-ready)
```

| Package | Dependencies | Purpose |
|---|---|---|
| **EggPdf** | None | Core: HTML + CSS -> PDF. Works on .NET Framework 4.6.2+ through .NET 9+ |
| **EggPdf.Razor** | EggPdf, Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation | Razor template -> HTML -> PDF in one call. ASP.NET Core only |
| **EggPdf.AspNetCore** | EggPdf, Microsoft.AspNetCore.Http.Abstractions | `PdfResult` for controllers, middleware for PDF endpoints |
| **EggPdf.Service** | EggPdf, ASP.NET Core | Standalone HTTP microservice (REST API) for PDF generation. Docker-ready. Deploy as sidecar or shared service |

**Rule:** The core `EggPdf` package NEVER gains external dependencies. Integration packages are optional and clearly scoped.

---

## Public API Design

Designed for a **general-purpose audience** -- from first-time users to advanced integrations.

### Output Modes

The converter supports **three output modes** for every render method. All three are first-class -- none is a wrapper around another.

```
                    +-------------------+
   HTML string ---> |                   | --+--> byte[]          (in-memory)
   HTML Stream ---> | HtmlToPdfConverter| --+--> Stream           (streaming -- HTTP, network, etc.)
   TextReader  ---> |                   | --+--> file path        (direct to disk)
                    +-------------------+   +--> PNG/JPEG image   (thumbnail, preview, email embed)
```

### IHtmlToPdfConverter Interface

```csharp
public interface IHtmlToPdfConverter
{
    // --- HTML string input -> PDF ---
    Task<byte[]> RenderAsync(string html, CancellationToken ct = default);
    Task RenderAsync(string html, Stream output, CancellationToken ct = default);
    Task RenderToFileAsync(string html, string filePath, CancellationToken ct = default);

    // --- HTML Stream/TextReader input -> PDF (for large HTML without loading entire string) ---
    Task<byte[]> RenderAsync(Stream htmlInput, CancellationToken ct = default);
    Task RenderAsync(TextReader htmlInput, Stream output, CancellationToken ct = default);

    // --- Render to PNG/image (not just PDF) ---
    // Common request: thumbnail generation, social media preview, email embedding
    Task<byte[]> RenderToImageAsync(string html, ImageOptions options, CancellationToken ct = default);
    Task RenderToImageAsync(string html, Stream output, ImageOptions options, CancellationToken ct = default);

    // --- Render specific page range (for large documents) ---
    Task<byte[]> RenderPagesAsync(string html, PageRange pages, CancellationToken ct = default);

    // --- Sync variants (for .NET Framework / non-async contexts) ---
    byte[] Render(string html);
    void Render(string html, Stream output);
    void RenderToFile(string html, string filePath);

    // --- With structured result (warnings, diagnostics) ---
    Task<RenderResult> RenderWithResultAsync(string html, CancellationToken ct = default);
    Task<RenderResult> RenderWithResultAsync(string html, Stream output, CancellationToken ct = default);
}

public class ImageOptions
{
    public ImageFormat Format { get; set; } = ImageFormat.Png;  // Png, Jpeg
    public int Dpi { get; set; } = 150;           // Resolution (72=screen, 150=preview, 300=print)
    public int? PageNumber { get; set; }           // Which page to render (null = first page)
    public int? Quality { get; set; }              // JPEG quality 1-100 (ignored for PNG)
}

public class PageRange
{
    public int Start { get; set; }    // 1-based
    public int End { get; set; }      // inclusive
    // PageRange.All, PageRange.First, PageRange.Last, new PageRange(3, 7)
}

// Structured result with warnings and diagnostics
public class RenderResult
{
    public byte[]? PdfBytes { get; }           // null when output was written to Stream
    public int PageCount { get; }
    public IReadOnlyList<RenderWarning> Warnings { get; }  // Non-fatal issues
    public RenderTimings Timings { get; }       // Per-stage timing breakdown
}

public record RenderWarning(
    RenderWarningLevel Level,       // Info, Warning, Error
    string Code,                    // "CSS_UNSUPPORTED_PROPERTY", "FONT_NOT_FOUND", etc.
    string Message,                 // "Font 'CustomFont' not found, substituted 'Arial'"
    string? Selector,               // CSS selector context, if applicable
    string? Element                 // HTML element context: "<img src='missing.png'>"
);

public record RenderTimings(
    TimeSpan Parse,
    TimeSpan StyleResolution,
    TimeSpan Layout,
    TimeSpan Fragmentation,
    TimeSpan Paint,
    TimeSpan PdfWrite,
    TimeSpan Total
);

// --- Progress reporting for large documents ---
// Pass IProgress<RenderProgress> via PdfOptions
public class PdfOptions
{
    // ... existing options ...
    public IProgress<RenderProgress>? Progress { get; set; }
}

public record RenderProgress(
    RenderStage Stage,              // Parsing, Styling, Layout, Painting, Writing
    int? CurrentPage,               // Which page is being processed (null during parsing)
    int? EstimatedTotalPages,       // Estimate (may change as layout progresses)
    double PercentComplete          // 0.0 - 1.0
);
```

### Usage Examples

```csharp
var converter = new HtmlToPdfConverter(new PdfOptions
{
    PageSize = PageSize.A4,
    Orientation = PageOrientation.Portrait,
    Margins = new PageMargins(top: 20, right: 15, bottom: 20, left: 15, unit: Unit.Mm),
    DefaultFont = "Arial",
    DefaultFontSize = 12,
    Title = "Invoice #1234",
    Author = "MyApp",
    MediaType = CssMediaType.Print,
    Compression = true
});

// --- 1. byte[] (in-memory) ---
byte[] pdf = await converter.RenderAsync(htmlString);

// --- 2. Physical file ---
await converter.RenderToFileAsync(htmlString, @"D:\Reports\invoice.pdf");

// --- 3. Stream to HTTP response (ASP.NET Core) ---
[HttpGet("invoice/{id}/pdf")]
public async Task GetInvoicePdf(int id)
{
    string html = await BuildInvoiceHtml(id);

    Response.ContentType = "application/pdf";
    Response.Headers.Append("Content-Disposition", "attachment; filename=\"invoice.pdf\"");

    // Streams PDF directly to the HTTP response -- no buffering in memory
    await _converter.RenderAsync(html, Response.Body, HttpContext.RequestAborted);
}

// --- 4. Stream to any writable stream ---
// Network socket
await converter.RenderAsync(html, networkStream);

// Memory stream (when you need a stream but also want bytes later)
using var ms = new MemoryStream();
await converter.RenderAsync(html, ms);
byte[] bytes = ms.ToArray();

// Zip archive entry
using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);
var entry = archive.CreateEntry("report.pdf");
await using var entryStream = entry.Open();
await converter.RenderAsync(html, entryStream);

// Azure Blob Storage
var blobClient = containerClient.GetBlobClient("invoices/123.pdf");
await using var uploadStream = await blobClient.OpenWriteAsync(overwrite: true);
await converter.RenderAsync(html, uploadStream);

// --- 5. Static one-liner (convenience) ---
byte[] pdf = await EggPdf.HtmlToPdf.RenderAsync("<h1>Hello World</h1>");
await EggPdf.HtmlToPdf.RenderToFileAsync("<h1>Hello</h1>", "output.pdf");

// --- 6. Sync (for .NET Framework or non-async code) ---
byte[] pdf = converter.Render(htmlString);
converter.RenderToFile(htmlString, @"C:\output\report.pdf");

// --- 7. Render to PNG image (thumbnail, preview, social share) ---
byte[] png = await converter.RenderToImageAsync(htmlString, new ImageOptions
{
    Format = ImageFormat.Png,
    Dpi = 150,
    PageNumber = 1  // First page only
});

// --- 8. HTML input from Stream (large HTML without loading full string) ---
using var htmlStream = File.OpenRead("large-report.html");
byte[] pdf = await converter.RenderAsync(htmlStream);

// --- 9. Render specific pages only ---
byte[] pages3to7 = await converter.RenderPagesAsync(htmlString, new PageRange(3, 7));

// --- 10. Programmatic header/footer (alternative to CSS @page) ---
// For users who prefer API over CSS for headers/footers
var options = new PdfOptions
{
    Header = new PageHeaderFooter
    {
        Center = "{{title}}",                      // Document title
        Right = "{{date:yyyy-MM-dd}}",             // Current date
        FontSize = 9,
        FontFamily = "Arial"
    },
    Footer = new PageHeaderFooter
    {
        Left = "Confidential",
        Center = "Page {{page}} of {{pages}}",     // Page numbers
        Right = "Generated by EggPdf",
        FontSize = 8,
        LineAbove = true                           // Separator line
    }
};
// These are rendered OUTSIDE the HTML content area (in page margins)
// They work alongside CSS @page margin boxes -- API takes priority if both are set

// --- 11. Batch rendering (generate 1000s of documents efficiently) ---
// Reuses font cache, parsed stylesheets, and warmed-up converter across renders
var invoices = await GetAllInvoices();
var results = new List<byte[]>();
await Parallel.ForEachAsync(invoices, new ParallelOptions { MaxDegreeOfParallelism = 4 },
    async (invoice, ct) =>
    {
        string html = RenderInvoiceHtml(invoice);
        byte[] pdf = await converter.RenderAsync(html, ct);
        lock (results) results.Add(pdf);
    });
// The converter is thread-safe -- font cache and CSS UA stylesheet are shared

// --- 12. Image optimization for file size control ---
var options = new PdfOptions
{
    ImageOptimization = new ImageOptimizationOptions
    {
        MaxImageDpi = 150,                // Downsample images exceeding this DPI (saves huge file size)
        JpegQuality = 85,                 // Quality for JPEG compression (1-100)
        ConvertPngToJpeg = true,          // Convert opaque PNGs to JPEG for smaller size (keep alpha PNGs as PNG)
        ColorConversion = ColorConversion.None  // or RgbToCmyk for print production
    }
};

// --- 13. Bates numbering (legal documents) ---
var options = new PdfOptions
{
    BatesNumbering = new BatesNumberingOptions
    {
        Prefix = "CASE-2026-",
        StartNumber = 1,
        Digits = 6,                       // CASE-2026-000001, CASE-2026-000002, ...
        Position = BatesPosition.BottomRight,
        FontSize = 8
    }
};
```

### Error Recovery Strategy

When a resource fails to load or a feature isn't supported, the library must **never crash**. Instead it degrades gracefully and reports the issue:

| Error Scenario | Recovery Behavior | Warning Reported |
|---|---|---|
| Font not found | Fall back to next font in stack, then system default, then Helvetica | `FONT_NOT_FOUND: "CustomFont" not found, using "Arial"` |
| Image load failed | Render `alt` text in placeholder box (gray background, broken-image icon) | `IMAGE_LOAD_FAILED: "logo.png" returned 404` |
| External stylesheet failed | Skip that stylesheet, continue with remaining styles | `STYLESHEET_LOAD_FAILED: "styles.css" timed out` |
| @font-face font failed | Fall back to system font or built-in PDF font | `FONT_LOAD_FAILED: "MyFont.woff" decode error` |
| Unsupported CSS property | Silently ignore (per CSS spec) | `CSS_UNSUPPORTED: "container-type" not supported` (only with PdfOptions.LogLevel = Verbose) |
| HTML parse error | Produce DOM anyway (per HTML5 spec error recovery) | No warning (expected for real-world HTML) |
| Layout overflow | Content overflows page -- clip to page bounds | `LAYOUT_OVERFLOW: content exceeds page height on page 5` |
| Circular @import | Break cycle, skip duplicate import | `CSS_CIRCULAR_IMPORT: "a.css" already imported` |
| Max page limit reached | Stop rendering, return pages generated so far | `LIMIT_EXCEEDED: max 1000 pages reached, document truncated` |
| Render timeout | Cancel gracefully via CancellationToken | `RENDER_TIMEOUT: exceeded 30s limit` |

All warnings are collected in `RenderResult.Warnings` when using `RenderWithResultAsync()`. With standard `RenderAsync()`, warnings are logged via `ILogger` if configured, otherwise silently swallowed (no exceptions for non-fatal issues).

### Streaming Architecture (how Stream output works)

For `Stream` output, the PDF writer writes **progressively** to the output stream:

```
Pipeline:   HTML → Parse → Style → Layout → Paginate → Paint → PDF Write
                                                                   |
                                                         writes page-by-page
                                                         to output Stream
                                                                   |
                                                              HTTP Response
                                                              / File / Network
```

The PDF format requires a cross-reference table at the end of the file (byte offsets of all objects). This means we must track offsets as we write, but we do NOT need to hold the entire document in memory. Pages and their resources (fonts, images) are written incrementally, with the xref table written last.

**Memory profile for streaming:**
- Only the current page's content stream is in memory at any time
- Shared resources (fonts, images) are written once and referenced by all pages
- The xref table accumulates byte offsets (8 bytes per object -- negligible)

### Configuration

```csharp
// ----- Custom Fonts -----
var options = new PdfOptions();
options.Fonts.AddDirectory("/usr/share/fonts/truetype");
options.Fonts.AddFile("./assets/MyCustomFont.ttf");
options.Fonts.EnableSubsetting = true;  // Only embed used glyphs

// ----- Inject Additional CSS -----
var options = new PdfOptions
{
    UserStyleSheet = "@page { margin: 2cm; } body { font-size: 14px; }"
};

// ----- Image Optimization (file size control) -----
var options = new PdfOptions
{
    ImageOptimization = new ImageOptimizationOptions
    {
        MaxImageDpi = 150,             // Downsample hi-res images
        JpegQuality = 85,             // 1-100
        ConvertPngToJpeg = true,       // Opaque PNGs -> JPEG (smaller)
        ColorConversion = ColorConversion.None  // or RgbToCmyk for print
    }
};

// ----- Resource Resolver (for images, fonts, linked stylesheets) -----
var options = new PdfOptions
{
    ResourceResolver = new LocalFileResourceResolver("./assets/"),
    // or implement IResourceResolver for custom logic (S3, database, etc.)

    // Base URL for resolving relative URLs (images, stylesheets, fonts)
    // when no <base href> is present in the HTML
    BaseUrl = "https://example.com/reports/"
};

// ----- DI Registration -----
services.AddEggPdf(options =>
{
    options.PageSize = PageSize.A4;
    options.DefaultFont = "Arial";
});

// Then inject anywhere:
public class InvoiceService(IHtmlToPdfConverter converter)
{
    public async Task<byte[]> GenerateInvoice(InvoiceModel model)
    {
        string html = await RenderRazorTemplate(model);
        return await converter.RenderAsync(html);
    }
}
```

### Key API Principles
- **Four output formats** -- PDF (byte[], Stream, file), PNG/JPEG image -- all first-class
- **Three input modes** -- HTML string, HTML Stream, TextReader -- for small and large documents
- **Async-first** -- all rendering is async with CancellationToken support; sync wrappers for .NET Framework
- **True streaming** -- Stream output writes progressively, not buffer-then-flush
- **Page range rendering** -- render only pages 3-7 of a 50-page document (skip expensive layout for unneeded pages)
- **Structured results** -- `RenderWithResultAsync()` returns PDF bytes + warnings + page count + timing breakdown
- **Progress reporting** -- `IProgress<RenderProgress>` for large documents: stage, current page, estimated total, percent complete
- **Programmatic headers/footers** -- `PdfOptions.Header` / `PdfOptions.Footer` with template variables (`{{page}}`, `{{pages}}`, `{{title}}`, `{{date}}`) as simpler alternative to CSS @page margin boxes
- **Immutable options** -- PdfOptions is validated and frozen at converter creation
- **DI-ready** -- IHtmlToPdfConverter interface, IServiceCollection registration
- **Thread-safe** -- one converter instance shared across requests (font cache etc. reused)
- **IResourceResolver** -- extensible interface for loading images/fonts from any source

### Razor API (`EggPdf.Razor` package)

```csharp
// ----- Setup: Register EggPdf.Razor in DI -----
services.AddEggPdfRazor(options =>
{
    options.PageSize = PageSize.A4;
    options.DefaultFont = "Arial";
    options.ViewLocations = ["Views/Pdf", "Templates"];  // Where to find .cshtml files
});

// ----- IRazorToPdfConverter supports all 3 output modes -----
public interface IRazorToPdfConverter
{
    // byte[]
    Task<byte[]> RenderViewAsync<TModel>(string viewName, TModel model, CancellationToken ct = default);
    Task<byte[]> RenderStringAsync<TModel>(string razorTemplate, TModel model, CancellationToken ct = default);

    // Stream (for HTTP responses, cloud storage, etc.)
    Task RenderViewAsync<TModel>(string viewName, TModel model, Stream output, CancellationToken ct = default);
    Task RenderStringAsync<TModel>(string razorTemplate, TModel model, Stream output, CancellationToken ct = default);

    // File
    Task RenderViewToFileAsync<TModel>(string viewName, TModel model, string filePath, CancellationToken ct = default);

    // All methods accept optional PdfOptions override
    Task<byte[]> RenderViewAsync<TModel>(string viewName, TModel model, PdfOptions options, CancellationToken ct = default);
    Task RenderViewAsync<TModel>(string viewName, TModel model, Stream output, PdfOptions options, CancellationToken ct = default);
}

// ----- Basic: Render .cshtml to byte[] -----
public class InvoiceService(IRazorToPdfConverter pdfConverter)
{
    // To byte[]
    public async Task<byte[]> GenerateInvoice(InvoiceModel model)
        => await pdfConverter.RenderViewAsync("Invoice", model);

    // To file
    public async Task SaveInvoice(InvoiceModel model, string path)
        => await pdfConverter.RenderViewToFileAsync("Invoice", model, path);

    // To HTTP response stream
    public async Task StreamInvoice(InvoiceModel model, Stream responseBody, CancellationToken ct)
        => await pdfConverter.RenderViewAsync("Invoice", model, responseBody, ct);
}

// ----- The Razor template (Views/Pdf/Invoice.cshtml) -----
// @model InvoiceModel
// <html>
// <head>
//   <style>
//     @page { size: A4; margin: 2cm; }
//     @page { @bottom-center { content: "Page " counter(page) " of " counter(pages); } }
//     table { width: 100%; border-collapse: collapse; }
//     td, th { border: 1px solid #ddd; padding: 8px; }
//   </style>
// </head>
// <body>
//   <h1>Invoice #@Model.InvoiceNumber</h1>
//   <table>
//     @foreach (var line in Model.Lines)
//     {
//       <tr>
//         <td>@line.Description</td>
//         <td>@line.Amount.ToString("C")</td>
//       </tr>
//     }
//   </table>
// </body>
// </html>

// ----- Advanced: Inline Razor string (no .cshtml file needed) -----
byte[] pdf = await pdfConverter.RenderStringAsync(
    @"@model OrderModel
      <h1>Order #@Model.OrderId</h1>
      <p>Customer: @Model.CustomerName</p>",
    new OrderModel { OrderId = 42, CustomerName = "Acme Corp" });

// ----- Advanced: Override options per render -----
byte[] pdf = await pdfConverter.RenderViewAsync("Report", model, new PdfOptions
{
    Orientation = PageOrientation.Landscape,
    Margins = new PageMargins(10, 10, 10, 10, Unit.Mm)
});

// ----- ASP.NET Core Controller (EggPdf.AspNetCore) -----

// Option A: IActionResult (simplest -- buffers in memory, sets headers automatically)
[HttpGet("invoice/{id}/pdf")]
public async Task<IActionResult> DownloadInvoice(int id)
{
    var model = await _invoiceService.GetInvoice(id);
    return new RazorPdfResult("Invoice", model)
    {
        FileName = $"invoice-{id}.pdf",
        Options = new PdfOptions { PageSize = PageSize.A4 }
    };
    // Returns application/pdf with Content-Disposition: attachment
}

// Option B: Stream directly to Response.Body (zero buffering, best for large docs)
[HttpGet("report/{id}/pdf")]
public async Task StreamReport(int id)
{
    var model = await _reportService.GetReport(id);

    Response.ContentType = "application/pdf";
    Response.Headers.Append("Content-Disposition", $"inline; filename=\"report-{id}.pdf\"");

    // PDF bytes stream directly to the client as they are generated
    await _pdfConverter.RenderViewAsync("Report", model, Response.Body, HttpContext.RequestAborted);
}

// Option C: Minimal API endpoint
app.MapGet("/invoice/{id}/pdf", async (int id, IRazorToPdfConverter pdf, HttpContext ctx) =>
{
    var model = await GetInvoice(id);
    ctx.Response.ContentType = "application/pdf";
    await pdf.RenderViewAsync("Invoice", model, ctx.Response.Body, ctx.RequestAborted);
});
```

**How Razor rendering works internally:**

```
.cshtml template + Model
        |
        v
  [Microsoft.AspNetCore.Mvc.Razor]
  Compile .cshtml -> C# class -> Execute with model
        |
        v
    HTML string (fully rendered, no Razor syntax left)
        |
        v
  [EggPdf core pipeline]
  Parse HTML -> Style -> Layout -> Paint -> PDF
        |
        v
    PDF bytes
```

The Razor integration is a thin layer (~500 LOC) that:
1. Uses Microsoft's Razor engine to compile and execute .cshtml -> HTML string
2. Passes the HTML string to `EggPdf.HtmlToPdfConverter.RenderAsync()`
3. Returns the PDF bytes

This means **every Razor feature works**: `@model`, `@foreach`, `@if`, partial views, `@section`, tag helpers, view components, etc. We don't parse Razor ourselves -- we delegate to Microsoft's battle-tested engine.

### Advanced API (pipeline access for power users)

```csharp
// Access the rendering pipeline stages individually
// Useful for debugging, testing, or building custom tooling on top of EggPdf

// Parse HTML to DOM
var document = EggPdf.Html.HtmlParser.Parse(htmlString);

// Parse and resolve CSS
var styleResolver = new EggPdf.Css.StyleResolver();
styleResolver.AddStyleSheet(EggPdf.Css.CssParser.Parse(cssString));
var styledTree = styleResolver.Resolve(document);

// Run layout
var layoutEngine = new EggPdf.Layout.LayoutEngine(layoutOptions);
var layoutTree = layoutEngine.Layout(styledTree);

// Paginate
var pages = EggPdf.Fragmentation.PageBreaker.Paginate(layoutTree, pageOptions);

// Render to PDF
var pdfDoc = EggPdf.Rendering.Painter.Paint(pages);
byte[] bytes = pdfDoc.Save();
```

---

## Competitive Landscape

Where EggPdf fits among existing .NET HTML-to-PDF solutions:

| Library | Approach | Native Deps | License | Chrome Parity | Pure C# |
|---|---|---|---|---|---|
| **SelectPdf** | Embedded WebKit | ~50MB native | Commercial | High | No |
| **wkhtmltopdf** | Embedded Qt WebKit | ~40MB native | LGPL | Medium | No |
| **Puppeteer/Playwright** | Headless Chrome | ~300MB Chrome | Apache 2 | Perfect | No |
| **iTextSharp** | PDF builder (no HTML) | Medium | AGPL/Commercial | N/A | Yes |
| **QuestPDF** | Fluent PDF builder (no HTML) | None | Community MIT | N/A | Yes |
| **PdfSharpCore** | Low-level PDF (no HTML) | None | MIT | N/A | Yes |
| **WeasyPrint** | Own engine | Python runtime | BSD | High | No (.NET) |
| **EggPdf** | Own rendering engine | **None** | **MIT** | **Target: High** | **Yes** |

**Our unique position:** The only pure C#, zero-dependency library that accepts raw HTML input and targets Chrome-level print quality. No other library in the .NET ecosystem occupies this space.

---

## Success Criteria

### Phase 1 (Vertical Slice)
- Valid PDF output opens in Adobe Reader, Chrome, SumatraPDF, macOS Preview
- `<h1>` through `<h6>`, `<p>`, `<div>` render with correct default styles
- Background colors and simple borders work
- Text is selectable and copyable from the PDF
- `<a href>` produces clickable links in the PDF
- HTML entities decode correctly (&amp; &lt; &hearts; &#x41;)
- html5lib-tests tokenizer suite passes 90%+
- CancellationToken stops rendering mid-pipeline
- Adversarial input (deeply nested, huge values) doesn't crash or hang

### Phase 4 (CSS 2.1 Complete)
- Pass 80%+ of relevant WPT CSS 2.1 tests
- Simple real-world pages (Bootstrap docs, MDN articles) render recognizably
- Floats and margin collapsing work correctly
- CSS shorthands expand correctly (all 50+)
- External stylesheets load via `<link>` and `@import`
- Form elements render with visible values

### Phase 7 (Flex + Grid Complete)
- Pass 80%+ of WPT Flexbox + Grid tests
- Modern dashboard/card layouts render correctly
- Tailwind CSS-styled pages render correctly

### Phase 8 (Pagination Complete)
- PDF bookmarks auto-generated from headings
- Internal links (`<a href="#id">`) navigate within PDF
- Page numbers work correctly (Page X of Y)
- Running headers/footers render correctly
- Tables, flex, and grid containers fragment across pages
- `target-counter()` and `leader()` work for CSS-based TOC
- Mixed page orientations (portrait body + landscape charts) in same document
- Page labels: Roman numerals for front matter, decimal for body

### Phase 11 (Production Ready)
- Pass 90%+ of relevant WPT tests
- Simple page (1 page): < 100ms
- Complex page (10 pages, tables, images): < 1s
- Memory: < 100MB for typical documents
- NuGet package size: < 3MB (core)
- Thread-safe for concurrent conversions in ASP.NET Core
- PDF/A compliant output (optional)
- Debug mode shows layout overlay
- `dotnet-eggpdf` CLI tool published

### Phase 12 (Ecosystem Complete)
- EggPdf.Razor renders .cshtml templates to PDF
- EggPdf.AspNetCore provides PdfResult and RazorPdfResult
- Sample projects demonstrate all major use cases
- Migration guide from SelectPdf/wkhtmltopdf available

### Phase 13 (Business PDF Features)
- Digital signatures work (sign PDF with X.509 certificate, visible appearance)
- Fillable AcroForm fields generated from HTML `<input>`, `<select>`, `<textarea>`
- QR codes and barcodes render as crisp vectors
- File attachments embed correctly (ZUGFeRD/Factur-X XML validates)
- PDF merging produces correct combined outlines, page labels, deduplicated resources
- Page labels show correct numbering per section (Roman, decimal, prefixed)

### Phase 14 (Commercial Print + Compliance)
- PDF/X-4 output accepted by prepress workflows
- TrimBox/BleedBox correct, crop marks render
- CMYK and spot colors in output
- PAdES B-LT signatures with embedded validation data
- ZUGFeRD v2.3 invoices pass official validator

---

## Project Governance

### License
**MIT License.** Chosen for maximum adoption. No copyleft, no commercial license needed. Same license as QuestPDF, PdfSharpCore, AngleSharp.

### Versioning Strategy
**Semantic Versioning (SemVer 2.0):**
- **Major** (X.0.0): Breaking API changes (method signatures, removed public types). Aim to never break before 2.0
- **Minor** (0.X.0): New features, new CSS support, new PDF capabilities. Backward compatible
- **Patch** (0.0.X): Bug fixes, rendering corrections, performance improvements

**Pre-1.0 policy:** During 0.x development, minor versions may include breaking changes (clearly documented in CHANGELOG). API is considered unstable until 1.0.

**1.0 release criteria:** Phase 8 complete (pagination + paged media). At that point the core rendering pipeline is stable and the public API is frozen.

### API Stability Guarantees
- **Public API** (`EggPdf`, `EggPdf.Razor`, `EggPdf.AspNetCore` namespaces): stable after 1.0, SemVer-protected
- **Pipeline API** (`EggPdf.Html`, `EggPdf.Css`, `EggPdf.Layout` namespaces): semi-public for power users. May change in minor versions with migration guidance
- **Internal** (`EggPdf.*.Internal` namespaces): no stability guarantees, may change in any version

### Project Files
```
EggPdf/
|-- LICENSE                          # MIT
|-- CHANGELOG.md                     # Per-version changes (keep updated every PR)
|-- CONTRIBUTING.md                  # How to contribute: code style, PR process, testing requirements
|-- ARCHITECTURE.md                  # High-level architecture guide (extracted from this blueprint)
|-- CODE_OF_CONDUCT.md               # Standard Contributor Covenant
|-- .github/
|   |-- ISSUE_TEMPLATE/              # Bug report, feature request templates
|   |-- PULL_REQUEST_TEMPLATE.md
|   |-- workflows/
|       |-- ci.yml                   # PR/push CI pipeline
|       |-- weekly.yml               # Benchmarks, full WPT
|       |-- nightly.yml              # Fuzz testing, corpus diff
|       |-- release.yml              # Validation + NuGet publish
```

---

## Final Completeness Checklist

Items discovered across 9 review passes. Everything below is now integrated into the blueprint above, listed here as a cross-reference for verification.

### CSS Properties -- Remaining Minor Items

| Property | Status | Where |
|---|---|---|
| `counter-set` | Must support alongside `counter-increment` / `counter-reset` | Phase 4 (Generated Content) |
| `text-rendering` | `auto / optimizeSpeed / optimizeLegibility / geometricPrecision`. Affects kerning/ligature behavior. Map to font rendering hints | Phase 3 (Typography) |
| `zoom` | Non-standard but Chrome supports it. `zoom: 0.5` scales element. Common in legacy/email HTML. Treat as `transform: scale()` equivalent | Phase 10 |
| CSS `forced-color-adjust` | `auto / none`. Controls whether forced-colors mode overrides author styles. For print: always `none` (we don't force colors) | Ignored list |
| `overflow-x` / `overflow-y` separately | Must handle independently: `overflow-x: hidden; overflow-y: visible` | Phase 4 |
| `-webkit-line-clamp` | Truncate text to N lines with ellipsis. Non-standard but widely used. Chrome supports it | Phase 10 |
| `backdrop-filter` | `blur()`, `brightness()` etc. behind an element. Tier 3 alongside `filter` | Tier 3 |

### HTML/DOM -- Remaining Items

| Item | Details |
|---|---|
| `<math>` / MathML | Listed in Tier 3 but should note: MathML is now supported in Chrome 109+. Becoming more relevant for scientific/academic documents |
| `<slot>` | Web Components feature. Without shadow DOM (no JS), `<slot>` renders its fallback content |
| Custom elements (`<my-component>`) | Unknown elements render as inline by default (per HTML spec). No special handling needed -- our parser treats them as generic elements |
| `contenteditable` attribute | Ignored for print (not interactive) |
| `draggable` attribute | Ignored for print |
| `spellcheck` attribute | Ignored for print |
| `autofocus` attribute | Ignored for print |
| `tabindex` attribute | Used for PDF tab order (mapped to structure tree order) |

### PDF -- Remaining Items

| Item | Details |
|---|---|
| **Producer string** | `/Producer` in Info dict must be `"EggPdf vX.Y.Z"`. Auto-set, not configurable |
| **Creator string** | `/Creator` can be set by user via `PdfOptions.Creator` (e.g., "MyApp v1.0"). Defaults to "EggPdf" |
| **Article threads** | `/Threads` in document catalog. Defines reading order across columns in multi-column layouts. Important for PDF/UA accessibility of multi-column content |
| **GoToR actions** | Link annotations that open another PDF: `<a href="other-document.pdf#page=5">`. `/S /GoToR` with file specification |
| **Trapped key** | `/Trapped` in Info dict: `True`, `False`, or `Unknown`. Relevant for prepress workflows (PDF/X) |
| **Page thumbnails** | Optional embedded thumbnail images per page. Modern readers generate dynamically, so low priority. But PDF/A-1 may benefit |

### API -- Remaining Items

| Item | Details |
|---|---|
| **Warm-up method** | `converter.WarmUpAsync()` -- pre-loads system fonts, parses UA stylesheet, initializes font cache. First render is slower (cold start); warm-up eliminates this for latency-sensitive applications |
| **Page count estimation** | `converter.EstimatePageCountAsync(html)` -- runs parse + style + layout but skips paint + PDF write. Returns estimated page count. Useful for progress bars, pagination UI, "this will be ~47 pages" preview |
| **Element exclusion** | `data-eggpdf-exclude` attribute: elements with this attribute are excluded from PDF rendering. Useful for "print this page" buttons, screen-only navigation, ads. Also respect `@media print { .no-print { display: none } }` but the attribute is more discoverable |
| **Custom data attributes** | `data-eggpdf-page-break="before"` as alternative to CSS `break-before: page`. `data-eggpdf-bookmark="Chapter 1"` to explicitly set bookmark title (instead of using heading text). Discoverable API for users who don't want to write CSS |

### Security Model

| Concern | Mitigation |
|---|---|
| **Resource resolver file access** | `LocalFileResourceResolver` should restrict to a base directory. No path traversal (`../../etc/passwd`). Document this clearly |
| **SSRF via Service** | `POST /api/render/url` fetches external URLs -- potential SSRF vector. Mitigate: configurable allowlist of domains, disable by default in production, timeout, max response size |
| **Malicious HTML** | We don't execute JS, so XSS is not a concern. But adversarial HTML can cause: memory exhaustion (huge documents), CPU exhaustion (complex CSS selectors), infinite layout loops. Mitigated by: max page/element limits, render timeout, recursion depth limits |
| **Malicious fonts** | A crafted .ttf could exploit our TrueType parser. Mitigate: bounds checking on all table reads, fuzz testing fonts, reject fonts with invalid table offsets |
| **Malicious images** | A crafted PNG/JPEG could exploit our decoder. Mitigate: bounds checking, max image dimensions, fuzz testing image decoders |
| **PDF injection** | Our PDF writer generates all content -- there's no "user content in PDF operators" injection risk. But embedded file names (ZUGFeRD) should be sanitized |

### Performance -- Remaining Items

| Item | Details |
|---|---|
| **Cold start** | First render loads system fonts (~200-500ms on typical system). Subsequent renders reuse cache. Document this. Provide `WarmUpAsync()` |
| **Wide content handling** | When a table or element is wider than the page, options: (a) clip (default), (b) scale down to fit (`PdfOptions.ShrinkToFit = true`), (c) overflow visibly. Chrome clips by default |
| **Scale-to-fit option** | `PdfOptions.ShrinkToFit = true` -- if content width exceeds page width, scale down the entire page content to fit. Common in "print to fit" scenarios |
| **Concurrent render limit** | `PdfOptions.MaxConcurrentRenders` -- prevent memory exhaustion from too many parallel renders on a shared converter. Default: `Environment.ProcessorCount` |

### Quality Milestones

| Milestone | Description |
|---|---|
| **Acid2 test** | Pass the Acid2 test (tests CSS 2.1 compliance). WeasyPrint and Prince both pass it. A visible, shareable proof of quality |
| **Real-site rendering gallery** | Maintain a public gallery showing EggPdf output vs. Chrome output for 20+ real websites. Updated per release. Shows progress transparently |
| **Ecosystem packages rated** | All NuGet packages should target: >90% test coverage, A grade on code quality tools, zero known vulnerabilities |

