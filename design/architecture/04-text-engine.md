# 04 - Text Engine Architecture

## Overview

The text engine handles everything related to fonts and text rendering: font discovery, font parsing, glyph measurement, line breaking, bidirectional text, and font embedding in PDF.

```
Text content + font-family CSS
    |
    v
FontResolver (find the right font file)
    |
    v
TrueTypeParser (parse font tables)
    |
    v
FontMetrics (glyph widths, ascent, descent)
    |         |
    |         v
    |    TextShaper (measure text runs, kerning, ligatures)
    |         |
    |         v
    |    LineBreaker (UAX #14 + CSS rules -> break positions)
    |
    v
FontSubsetter (extract only used glyphs for PDF embedding)
    |
    v
PDF Font Objects (CIDFont + ToUnicode CMap)
```

## Font Resolution

### Resolution Order

```
1. PdfOptions.Fonts (user-provided .ttf/.otf/.woff files)
2. @font-face rules from CSS (resolved via IResourceResolver)
3. System fonts (platform-specific directories)
4. Built-in PDF standard fonts (Helvetica, Times-Roman, Courier)
```

### System Font Discovery

```csharp
static class SystemFontLocator
{
    static IEnumerable<string> GetFontDirectories()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            yield return @"C:\Windows\Fonts";
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Windows\Fonts");

        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            yield return "/System/Library/Fonts";
            yield return "/Library/Fonts";
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library/Fonts");

        else // Linux
            yield return "/usr/share/fonts";
            yield return "/usr/local/share/fonts";
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".fonts");
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local/share/fonts");
    }
}
```

### Font Matching Algorithm

```csharp
class FontResolver
{
    // CSS font-family: "Custom Font", Arial, sans-serif;
    // Try each name in order until a font is found

    FontFace? Resolve(string[] familyStack, FontWeight weight, FontStyle style, FontStretch stretch)
    {
        foreach (var family in familyStack)
        {
            // 1. Exact match by family name + weight + style
            if (TryExactMatch(family, weight, style, stretch, out var face))
                return face;

            // 2. Close match (e.g., weight 700 requested, 600 available)
            if (TryCloseMatch(family, weight, style, stretch, out face))
                return face;
        }

        // 3. Generic family fallback
        // sans-serif -> Arial (Win), Helvetica (Mac), DejaVu Sans (Linux)
        // serif -> Times New Roman (Win), Times (Mac), DejaVu Serif (Linux)
        // monospace -> Consolas (Win), Menlo (Mac), DejaVu Sans Mono (Linux)
        return ResolveGenericFamily(familyStack.Last());
    }
}
```

### Font Fallback Chain

When a glyph is missing from the primary font:

```
1. Try next font in font-family stack
2. Try system fallback fonts:
   - CJK detection: if codepoint is CJK (U+4E00-9FFF, etc.)
     -> Noto Sans CJK, MS Gothic, PingFang, etc.
   - Emoji detection: if codepoint is emoji (U+1F600-1F64F, etc.)
     -> Noto Color Emoji, Segoe UI Emoji, Apple Color Emoji
   - Arabic/Hebrew: if RTL script
     -> Noto Sans Arabic, Arial, etc.
3. Synthesize if needed:
   - Bold not available: increase stroke width
   - Italic not available: apply oblique transform (skew)
4. Last resort: Helvetica / built-in PDF font
5. If glyph still missing: render .notdef (tofu box) + emit warning
```

## TrueType/OpenType Parser

### Tables We Parse

| Table | Required | Purpose |
|-------|----------|---------|
| `head` | Yes | Font header: units-per-em, bounding box, flags |
| `hhea` | Yes | Horizontal header: ascent, descent, line gap |
| `hmtx` | Yes | Horizontal metrics: advance width per glyph |
| `cmap` | Yes | Character-to-glyph mapping (Unicode -> glyph ID) |
| `maxp` | Yes | Maximum profile: number of glyphs |
| `OS/2` | Yes | OS/2 metrics: weight class, width class, strikeout position, subscript/superscript |
| `name` | Yes | Font names: family, subfamily, full name, PostScript name |
| `post` | Yes | PostScript data: glyph names, underline position/thickness |
| `kern` | No | Kerning pairs (older format) |
| `GPOS` | No | Glyph positioning: kerning (modern), mark positioning |
| `GSUB` | No | Glyph substitution: ligatures, contextual alternates |
| `loca` | No | Glyph data locations (for subsetting) |
| `glyf` | No | Glyph outlines (for subsetting) |
| `fvar` | No | Font variations: variable font axes |
| `STAT` | No | Style attributes for variable fonts |
| `COLR` | No | Color glyph layers (emoji) |
| `CPAL` | No | Color palettes (emoji) |

### cmap Table Parsing

We support two cmap formats:

- **Format 4** (BMP): maps Unicode codepoints U+0000 to U+FFFF to glyph IDs. Segment-based encoding.
- **Format 12** (Full Unicode): maps codepoints beyond BMP (U+10000+). Group-based encoding. Required for emoji and supplementary CJK.

```csharp
class CmapTable
{
    ushort GetGlyphId(int codepoint);
    bool HasGlyph(int codepoint);
}
```

### WOFF Decoding

```csharp
static class WoffDecoder
{
    // WOFF 1.0: zlib-compressed TrueType/OpenType
    static byte[] Decode(byte[] woffData)
    {
        // 1. Read WOFF header (signature, numTables, totalSfntSize)
        // 2. For each table: decompress with zlib (System.IO.Compression.DeflateStream)
        // 3. Reassemble into valid TrueType/OpenType byte array
    }
}
```

## Text Shaping

### Glyph Width Measurement

```csharp
class TextShaper
{
    // Measure the width of a text run in a specific font/size
    float MeasureWidth(string text, FontFace font, float fontSize)
    {
        float width = 0;
        ushort prevGlyphId = 0;

        foreach (int codepoint in text.EnumerateCodepoints())
        {
            ushort glyphId = font.Cmap.GetGlyphId(codepoint);
            float advance = font.Hmtx.GetAdvanceWidth(glyphId);

            // Apply kerning
            if (prevGlyphId != 0)
                width += font.GetKerning(prevGlyphId, glyphId);

            width += advance * fontSize / font.UnitsPerEm;
            prevGlyphId = glyphId;
        }

        return width;
    }
}
```

### Kerning

Two sources:
1. **kern table** (older): simple pair-based kerning. O(log n) lookup with binary search.
2. **GPOS table** (modern): more complex, supports context-dependent kerning. We implement PairPos (Format 1 and 2) for basic kerning.

### Ligatures (GSUB)

We implement basic ligature substitution from the GSUB table:
- Standard ligatures: fi -> fi, fl -> fl, ff -> ff, ffi -> ffi, ffl -> ffl
- Lookup Type 4 (Ligature Substitution)

## Line Breaking

### Unicode Line Break Algorithm (UAX #14)

```csharp
class LineBreaker
{
    // Returns allowed break positions in a text run
    List<BreakOpportunity> FindBreaks(string text, LineBreakContext ctx)
    {
        // 1. Classify each character by line break class (per UAX #14)
        //    AL (alphabetic), NU (numeric), OP (open paren), CL (close paren),
        //    SP (space), BK (mandatory break), CR, LF, etc.

        // 2. Apply pair table: for each adjacent pair of classes,
        //    determine if break is allowed, prohibited, or indirect

        // 3. Apply CSS overrides:
        //    word-break: break-all -> allow break between any chars
        //    word-break: keep-all -> don't break CJK
        //    overflow-wrap: break-word -> break within word as last resort
        //    white-space: nowrap -> no breaks at all
    }
}

enum BreakOpportunity { Mandatory, Allowed, Prohibited }
```

### CJK Line Break Rules (Kinsoku Shori)

```
No break BEFORE these characters (closing): 。、）」』】〉》
No break AFTER these characters (opening):  （「『【〈《
```

### Hyphenation

```csharp
class Hyphenator
{
    // Liang/Knuth algorithm (same as TeX)
    // Language-specific patterns loaded from embedded resource files

    List<int> FindHyphenationPoints(string word, string language)
    {
        // 1. Look up patterns for the language
        // 2. Apply pattern matching to find valid hyphenation points
        // 3. Return positions where a hyphen can be inserted
    }
}
```

Hyphenation dictionaries (~200KB total) shipped as embedded resources for: English, German, French, Spanish, Italian, Portuguese, Dutch, Swedish, Norwegian, Danish, Finnish, Polish, Czech, Hungarian, Turkish, Russian, Ukrainian.

### Thai Word Segmentation

Thai text has no spaces between words. We use a dictionary-based approach:

```csharp
class ThaiWordBreaker
{
    // Dictionary of ~40,000 Thai words (embedded resource)
    // Longest matching algorithm to find word boundaries
    List<int> FindWordBreaks(string thaiText);
}
```

## Bidi Algorithm (UAX #9)

For mixed LTR/RTL text (e.g., English with Arabic):

```csharp
class BidiAlgorithm
{
    // Input: string with mixed LTR/RTL characters
    // Output: reordered runs for visual display

    List<BidiRun> Resolve(string text, BidiDirection paragraphDirection)
    {
        // 1. Determine embedding levels (per UAX #9)
        // 2. Resolve weak types
        // 3. Resolve neutral types
        // 4. Resolve implicit levels
        // 5. Reorder runs for visual display
    }
}

struct BidiRun
{
    int Start;
    int Length;
    int Level;          // even = LTR, odd = RTL
    BidiDirection Direction;
}
```

## Font Subsetting

For PDF embedding, we extract only the glyphs actually used in the document:

```csharp
class FontSubsetter
{
    byte[] Subset(FontFace font, HashSet<ushort> usedGlyphIds)
    {
        // 1. Always include glyph 0 (.notdef)
        // 2. For each used glyph: check if it's composite (references other glyphs)
        //    -> transitively include all component glyphs
        // 3. Build new glyf table with only used glyphs
        // 4. Build new loca table (glyph offsets)
        // 5. Build new cmap table (only used mappings)
        // 6. Update hmtx table (only used metrics)
        // 7. Update maxp table (new glyph count)
        // 8. Rebuild font with subset prefix: ABCDEF+FontName
        // 9. Return valid TrueType byte array
    }
}
```

### CIDFont Embedding for PDF

```csharp
class CidFontWriter
{
    // For CJK and large fonts: embed as CIDFont Type 2
    // Uses Identity-H encoding (glyph IDs are the CIDs)
    // Requires ToUnicode CMap for text extraction

    void WriteCidFont(PdfWriter writer, FontFace font, HashSet<ushort> usedGlyphs)
    {
        // 1. Write /Type /Font /Subtype /Type0
        // 2. Write /DescendantFonts [CIDFont ref]
        // 3. Write CIDFont: /Subtype /CIDFontType2, /CIDSystemInfo, /W (widths array)
        // 4. Write /FontDescriptor with /FontFile2 (subset TrueType data)
        // 5. Write /ToUnicode CMap stream (glyph ID -> Unicode mapping)
    }
}
```

### ToUnicode CMap

Required for text to be selectable/searchable in the PDF:

```csharp
class ToUnicodeCMapWriter
{
    // Maps glyph IDs back to Unicode codepoints
    // So PDF readers can extract text for copy/paste and search

    void Write(Stream output, Dictionary<ushort, int> glyphToUnicode)
    {
        // CMap format:
        // /CIDInit /ProcSet findresource begin
        // beginbfchar
        //   <0041> <0041>   (glyph 0x41 -> Unicode U+0041 'A')
        //   <0042> <0042>   (glyph 0x42 -> Unicode U+0042 'B')
        // endbfchar
    }
}
```

## Emoji Rendering

### Color Font Detection

```csharp
class ColorFontDetector
{
    // Check if a font has color glyph data
    bool HasColrTable(FontFace font);   // COLR/CPAL (vector color)
    bool HasCbdtTable(FontFace font);   // CBDT/CBLC (bitmap color)
    bool HasSbixTable(FontFace font);   // sbix (Apple bitmap)
}
```

### COLR/CPAL Rendering (vector color emoji)

```
1. For a color glyph, COLR table defines layers (back to front)
2. Each layer references a glyph ID + a color from CPAL palette
3. Render each layer as a separate colored path
4. In PDF: emit each layer as a separate fill operation with the layer's color
```

## Font Cache

```csharp
class FontCache
{
    // Thread-safe, shared across renders
    ConcurrentDictionary<string, FontFace> _cache;

    // Key: normalized family name + weight + style
    // Value: parsed FontFace (tables, metrics, cmap)

    // Fonts are loaded and parsed once, then reused for all renders
    // Memory: ~50-200KB per font (metadata only, not full glyph data)
}
```

## Testing

| Test Area | Approach |
|-----------|----------|
| Font resolution | Test family stack with fallbacks on each platform |
| TrueType parsing | Parse real font files (DejaVu, Liberation), verify tables |
| cmap lookup | Map known codepoints to glyph IDs, verify |
| Text measurement | Measure known strings, compare to expected widths |
| Kerning | Verify kerning pairs produce correct adjustments |
| Line breaking | UAX #14 test cases + CJK + Thai word break |
| Bidi | UAX #9 test cases + mixed LTR/RTL strings |
| Font subsetting | Subset -> reparse -> verify only requested glyphs present |
| ToUnicode CMap | Generate CMap -> extract text from PDF -> verify matches |
| Hyphenation | Known words -> expected hyphenation points per language |
| WOFF decode | Decode WOFF file -> verify equals original TrueType |
| Emoji | Verify color emoji glyphs render with correct colors |
