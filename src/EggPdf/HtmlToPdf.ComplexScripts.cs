using System;
using System.Collections.Generic;
using EggPdf.Layout;
using EggPdf.Pdf;
using EggPdf.Text;
using EggPdf.Text.OpenType;
using EggPdf.Text.TrueType;

namespace EggPdf;

public static partial class HtmlToPdf
{
    /// <summary>
    /// If the box's text needs glyph-level shaping (Thai, Indic, Arabic with marks), record the
    /// exact strings the painter will shape under the box's "-CX" font key. Returns false for
    /// ordinary text, which continues through the codepoint-keyed path.
    /// </summary>
    private static bool RegisterComplexText(LayoutBox box, string fontName,
        Dictionary<string, string> fontFamilyLists, Dictionary<string, HashSet<string>> complexTexts)
    {
        if (!ComplexTextShaper.NeedsShaping(box.Text)) return false;

        // The painter shapes Arabic letters into presentation forms first, then the glyph shaper
        // sees that logical string; text-transform/small-caps variants are collected as well.
        var variants = new List<string> { ArabicShaper.Shape(box.Text!) };
        var textTransform = box.Style?.Get("text-transform");
        var fontVariant = box.Style?.Get("font-variant");
        if ((!string.IsNullOrEmpty(textTransform) && textTransform != "none") ||
            (fontVariant != null && fontVariant.IndexOf("small-caps", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            variants.Add(ArabicShaper.Shape(box.Text!.ToUpperInvariant()));
            variants.Add(ArabicShaper.Shape(box.Text!.ToLowerInvariant()));
        }

        string? suffix = ComplexTextShaper.FontKeySuffix(variants[0]);
        if (suffix == null) return false;

        string key = fontName + suffix;
        if (!complexTexts.TryGetValue(key, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            complexTexts[key] = set;
        }
        string dir = box.Style?.Get("direction") == "rtl" ? "R" : "L";
        foreach (var v in variants) set.Add(dir + v);

        var familyList = box.Style?.FontFamily;
        if (!string.IsNullOrEmpty(familyList) && !fontFamilyLists.ContainsKey(key))
            fontFamilyLists[key] = familyList!;
        return true;
    }

    /// <summary>
    /// Resolve a font able to shape a script: the author's webfont or system families first (when
    /// they cover the script), then well-known script-capable system fonts. Shared by layout
    /// measurement and embedding so both shape with the same font.
    /// </summary>
    private static FontData? ResolveComplexFont(string? familyList,
        Dictionary<string, List<FontFaceCandidate>> fontFaces, FontResolver resolver,
        int targetWeight, bool bold, bool italic, string suffix)
    {
        int sample = ComplexTextShaper.SampleCodepoint(suffix);
        bool Covers(FontData? f) => f != null && f.RawData != null && f.RawData.Length > 0 && f.GetGlyphId(sample) != 0;

        var webfont = TryResolveFontFace(familyList, fontFaces, targetWeight, italic, resolver);
        if (Covers(webfont)) return webfont;

        if (!string.IsNullOrEmpty(familyList))
        {
            foreach (var raw in familyList!.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var family = raw.Trim().Trim('"', '\'');
                if (family.Length == 0) continue;
                var f = resolver.Resolve(family, bold, italic);
                if (Covers(f)) return f;
            }
        }

        foreach (var candidate in ComplexTextShaper.FallbackFontFamilies(suffix))
        {
            var f = resolver.Resolve(candidate, bold, italic);
            if (Covers(f)) return f;
        }
        return null;
    }

    /// <summary>Layout-time font lookup for complex-script text (memoized per render).</summary>
    private static Func<string?, string?, string?, string, FontData?> CreateComplexFontProvider(
        Dictionary<string, List<FontFaceCandidate>> fontFaces)
    {
        var cache = new Dictionary<string, FontData?>(StringComparer.Ordinal);
        return (family, weight, fontStyle, text) =>
        {
            string? suffix = ComplexTextShaper.FontKeySuffix(text);
            if (suffix == null) return null;

            var key = family + "|" + weight + "|" + fontStyle + "|" + suffix;
            if (cache.TryGetValue(key, out var cached)) return cached;

            int w = ParseFontWeight(weight);
            bool italic = fontStyle == "italic" || fontStyle == "oblique";
            var font = ResolveComplexFont(family, fontFaces, SharedFontResolver, w, w >= 600, italic, suffix);
            cache[key] = font;
            return font;
        };
    }

    /// <summary>
    /// Subset and embed one script-capable font per "-CX" key. Because shaped glyphs (conjuncts,
    /// reph forms, marks) have no codepoint, the exact strings are shaped now and the resulting
    /// glyph IDs are requested from the subsetter directly.
    /// </summary>
    private static void EmbedComplexScriptFonts(Dictionary<string, HashSet<string>> complexTexts,
        Dictionary<string, string> fontFamilyLists, PdfDocument pdfDoc,
        Dictionary<string, List<FontFaceCandidate>> fontFaces)
    {
        foreach (var kv in complexTexts)
        {
            string key = kv.Key;
            int cx = key.LastIndexOf("-CX", StringComparison.Ordinal);
            if (cx <= 0) continue;
            string baseName = key.Substring(0, cx);
            string suffix = key.Substring(cx);

            bool bold = baseName.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0;
            bool italic = baseName.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          baseName.IndexOf("Oblique", StringComparison.OrdinalIgnoreCase) >= 0;
            int targetWeight = ParseWeightSuffix(baseName) ?? (bold ? 700 : 400);
            if (targetWeight >= 600) bold = true;
            fontFamilyLists.TryGetValue(key, out var familyList);

            var font = ResolveComplexFont(familyList, fontFaces, SharedFontResolver, targetWeight, bold, italic, suffix);
            if (font == null) continue;

            var codepoints = new HashSet<int>();
            var glyphs = new HashSet<ushort>();
            foreach (var entry in kv.Value)
            {
                bool rtl = entry[0] == 'R';
                string text = entry.Substring(1);
                AddCodepoints(codepoints, text);
                foreach (var g in ComplexTextShaper.Shape(font, text, rtl))
                    glyphs.Add(g.GlyphId);
            }

            var subset = SubsetCached(font, codepoints, null, glyphs);
            if (subset == null || subset.FontData.Length == 0) continue;

            pdfDoc.AddEmbeddedFont(key, subset.FontData, subset.CodepointToNewGlyphId, subset.AdvanceWidths,
                font.UnitsPerEm, font.Ascent, font.Descent);
            pdfDoc.SetShapingData(key, font, subset.OldToNewGlyphId);
        }
    }
}
