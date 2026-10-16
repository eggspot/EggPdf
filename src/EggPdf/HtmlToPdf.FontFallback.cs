using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EggPdf.Core;
using EggPdf.Html.Dom;
using EggPdf.Pdf;
using EggPdf.Text;
using EggPdf.Text.Fallback;
using EggPdf.Text.TrueType;

namespace EggPdf;

public static partial class HtmlToPdf
{
    /// <summary>Codepoints listed individually in a missing-glyph warning before it switches to "and N more".</summary>
    private const int MaxWarningExamples = 6;

    /// <summary>
    /// Embed fallback fonts for the codepoints the primary font cannot draw and register them on the
    /// primary ("&lt;font&gt;-FB", "&lt;font&gt;-FB2", ...) so the painter can switch fonts per character.
    /// Which font supplies which codepoint is decided by <see cref="FontFallbackResolver"/> from the
    /// installed fonts' actual coverage, for any script. Codepoints nothing covers are returned.
    /// </summary>
    private static List<int> EmbedFallbackFonts(PdfDocument pdfDoc, string pdfFontName,
        HashSet<int> missing, bool bold, bool italic, HanVariant han)
    {
        var result = FontFallbackResolver.Default.Resolve(missing, bold, italic, han);
        var uncovered = new List<int>(result.Uncovered);

        int registered = 0;
        foreach (var fallback in result.Fonts)
        {
            var subset = SubsetCached(fallback.Font, new HashSet<int>(fallback.Codepoints));
            if (subset == null || subset.FontData.Length == 0)
            {
                uncovered.AddRange(fallback.Codepoints);
                continue;
            }

            registered++;
            string name = registered == 1 ? pdfFontName + "-FB" : pdfFontName + "-FB" + registered.ToString(CultureInfo.InvariantCulture);
            pdfDoc.AddEmbeddedFont(
                name,
                subset.FontData,
                subset.CodepointToNewGlyphId,
                subset.AdvanceWidths,
                fallback.Font.UnitsPerEm,
                fallback.Font.Ascent,
                fallback.Font.Descent,
                subset.ColorLayersByCodepoint);
            pdfDoc.AddFallbackFont(pdfFontName, name);
        }

        return uncovered;
    }

    /// <summary>
    /// Surface codepoints no installed font could supply: they paint as the missing-glyph box, so say so
    /// (once per codepoint per render) instead of failing silently. Reported to the caller's collector
    /// when given and always to <see cref="System.Diagnostics.Trace"/>.
    /// </summary>
    private static void ReportMissingGlyphs(IEnumerable<int> uncovered, string familyList, HashSet<int> alreadyReported,
        WarningCollector? warnings)
    {
        var byScript = new SortedDictionary<UnicodeScript, List<int>>();
        foreach (var cp in uncovered)
        {
            if (!alreadyReported.Add(cp)) continue;
            var script = UnicodeScripts.Of(cp);
            if (!byScript.TryGetValue(script, out var list)) byScript[script] = list = new List<int>();
            list.Add(cp);
        }

        foreach (var kv in byScript)
        {
            var sb = new StringBuilder();
            sb.Append("No installed font covers ").Append(kv.Value.Count.ToString(CultureInfo.InvariantCulture))
              .Append(kv.Value.Count == 1 ? " character" : " characters")
              .Append(" of script ").Append(UnicodeScripts.DisplayName(kv.Key)).Append(" (");
            for (int i = 0; i < kv.Value.Count && i < MaxWarningExamples; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("U+").Append(kv.Value[i].ToString("X4", CultureInfo.InvariantCulture));
            }
            if (kv.Value.Count > MaxWarningExamples)
                sb.Append(", and ").Append((kv.Value.Count - MaxWarningExamples).ToString(CultureInfo.InvariantCulture)).Append(" more");
            sb.Append(") used with font-family '").Append(familyList)
              .Append(kv.Value.Count == 1 ? "'; it renders as a missing-glyph box. " : "'; they render as missing-glyph boxes. ")
              .Append("Install a font covering this script (for example ").Append(FontFallbackCatalog.InstallHint(kv.Key))
              .Append(") or load one with @font-face.");

            string message = sb.ToString();
            warnings?.AddGlyphMissing(message);
            System.Diagnostics.Trace.TraceWarning("EggPdf " + WarningCodes.GlyphMissing + ": " + message);
        }
    }

    /// <summary>
    /// The font a box's text is painted with when the font-family list resolves to an installed system
    /// font: the first installed family in the list, then metric-compatible substitutes for the
    /// standard font class, then any generic sans-serif — so characters never fall through to the
    /// non-embedded path (which can only paint '?') merely because the named family is not installed.
    /// </summary>
    private static FontData? ResolveSystemPrimaryFont(string? systemFamilyList, string pdfFontName,
        FontResolver fontResolver, bool bold, bool italic)
    {
        // Real families from the list, then metric-compatible substitutes for the standard font class.
        var fontData = TryResolveSystemFont(systemFamilyList, fontResolver, bold, italic);
        if (fontData != null) return fontData;

        foreach (var candidate in GetSystemFontCandidates(pdfFontName))
        {
            fontData = fontResolver.Resolve(candidate, bold, italic);
            if (fontData != null) return fontData;
        }

        // Last resort: none of the family-specific candidates were installed -- a minimal container
        // often lacks every one of Courier/Liberation*/DejaVu*/Noto* for a given class. A generic
        // sans-serif substitute, even if metrically mismatched, still renders the real character,
        // which is what a browser's font-fallback chain would do.
        foreach (var candidate in GetSystemFontCandidates(string.Empty))
        {
            fontData = fontResolver.Resolve(candidate, bold, italic);
            if (fontData != null) return fontData;
        }

        return null;
    }

    /// <summary>
    /// Layout-time glyph advance lookup (see <see cref="Layout.TextMeasurer.GlyphAdvanceProvider"/>):
    /// the advance of the glyph the PDF will paint for a codepoint above U+00FF -- from the document's
    /// own font when it covers it, otherwise from the fallback font -- so wrapping and box sizes match
    /// the painted glyphs. Memoized per render. Codepoints WinAnsi can encode keep the built-in estimate.
    /// </summary>
    private static Func<string?, string?, string?, int, float> CreateGlyphAdvanceProvider(
        Dictionary<string, List<FontFaceCandidate>> fontFaces, HanVariant han)
    {
        var byStyle = new Dictionary<string, MeasuredFont>(StringComparer.Ordinal);
        MeasuredFont? last = null;
        string? lastFamily = null, lastWeight = null, lastStyle = null;

        return (family, weight, fontStyle, codepoint) =>
        {
            if (IsWinAnsiEncodable(codepoint)) return -1f;

            // Measurement calls this once per character: reuse the previous lookup while the style is
            // unchanged (same string instances) instead of re-keying on every character.
            if (last == null || !ReferenceEquals(family, lastFamily) || !ReferenceEquals(weight, lastWeight) || !ReferenceEquals(fontStyle, lastStyle))
            {
                var key = family + "|" + weight + "|" + fontStyle;
                if (!byStyle.TryGetValue(key, out var found))
                {
                    int numericWeight = ParseFontWeight(weight);
                    bool italic = fontStyle == "italic" || fontStyle == "oblique";
                    var primary = TryResolveFontFace(family, fontFaces, numericWeight, italic, SharedFontResolver)
                                  ?? ResolveSystemPrimaryFont(Css.FontVariationMarker.Split(family, out _),
                                      Layout.StandardFontMetrics.ResolvePdfFontName(family, weight, fontStyle),
                                      SharedFontResolver, numericWeight >= 600, italic);
                    byStyle[key] = found = new MeasuredFont(primary, numericWeight >= 600, italic);
                }
                last = found;
                lastFamily = family;
                lastWeight = weight;
                lastStyle = fontStyle;
            }

            if (last.Advances.TryGetValue(codepoint, out var cached)) return cached;

            float advanceEm = -1f;
            var font = last.Primary;
            if (font != null && font.UnitsPerEm > 0)
            {
                var gid = font.GetGlyphId(codepoint);
                if (gid > 0) advanceEm = font.GetAdvanceWidth(gid) / (float)font.UnitsPerEm;
            }
            if (advanceEm < 0 && !FontFallbackResolver.Default.TryGetAdvanceEm(codepoint, last.Bold, last.Italic, han, out advanceEm))
                advanceEm = -1f;

            last.Advances[codepoint] = advanceEm;
            return advanceEm;
        };
    }

    /// <summary>A style's resolved document font plus its per-codepoint advance cache (see <see cref="CreateGlyphAdvanceProvider"/>).</summary>
    private sealed class MeasuredFont
    {
        public MeasuredFont(FontData? primary, bool bold, bool italic) { Primary = primary; Bold = bold; Italic = italic; }
        public FontData? Primary { get; }
        public bool Bold { get; }
        public bool Italic { get; }
        public Dictionary<int, float> Advances { get; } = new Dictionary<int, float>();
    }

    /// <summary>
    /// Regional Han glyph preference for the document: the root element's <c>lang</c> when it names a CJK
    /// language, otherwise inferred from the text (kana implies Japanese, Hangul implies Korean).
    /// </summary>
    internal static HanVariant DetectHanVariant(HtmlDocument document)
    {
        var fromLang = HanVariantFromLang(document.DocumentElement?.GetAttribute("lang") ?? document.Body?.GetAttribute("lang"));
        if (fromLang != HanVariant.Auto) return fromLang;

        bool hangul = false;
        var stack = new Stack<HtmlNode>();
        stack.Push(document);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is HtmlTextNode textNode)
            {
                foreach (char c in textNode.Data)
                {
                    if (c < 0x1100) continue;
                    var script = UnicodeScripts.Of(c);
                    if (script == UnicodeScript.Kana) return HanVariant.Japanese;
                    if (script == UnicodeScript.Hangul) hangul = true;
                }
            }
            for (int i = node.ChildNodes.Count - 1; i >= 0; i--)
                stack.Push(node.ChildNodes[i]);
        }
        return hangul ? HanVariant.Korean : HanVariant.Auto;
    }

    internal static HanVariant HanVariantFromLang(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return HanVariant.Auto;
        var tag = lang!.Trim().ToLowerInvariant().Replace('_', '-');
        if (tag == "ja" || tag.StartsWith("ja-", StringComparison.Ordinal)) return HanVariant.Japanese;
        if (tag == "ko" || tag.StartsWith("ko-", StringComparison.Ordinal)) return HanVariant.Korean;
        if (tag == "zh-tw" || tag == "zh-hk" || tag == "zh-mo" || tag.StartsWith("zh-hant", StringComparison.Ordinal))
            return HanVariant.TraditionalChinese;
        if (tag == "zh" || tag.StartsWith("zh-", StringComparison.Ordinal)) return HanVariant.SimplifiedChinese;
        return HanVariant.Auto;
    }
}
