using System;
using System.Collections.Generic;
using EggPdf.Text.Hyphenation;

namespace EggPdf.Layout;

/// <summary>
/// Text measurement using PDF Standard 14 font metrics (Helvetica, Times-Roman, Courier).
/// Uses exact per-character widths from the PDF specification.
/// </summary>
public static class TextMeasurer
{
    private const float DefaultLineHeight = 1.2f;

    /// <summary>Shared English hyphenator (thread-safe: immutable after construction).</summary>
    private static readonly Hyphenator _englishHyphenator = Hyphenator.CreateEnglish();

    /// <summary>
    /// Optional per-render provider resolving (font-family list, weight, style)
    /// to a real parsed font whose advance widths are used for measurement.
    /// Set by the render pipeline when @font-face webfonts are in play, so
    /// layout measures with the same glyph metrics the PDF paints with.
    /// </summary>
    [ThreadStatic]
    public static Func<string?, string?, string?, EggPdf.Text.TrueType.FontData?>? FontDataProvider;

    [ThreadStatic]
    private static Func<string, string?>? _installedFamilyResolver;

    [ThreadStatic]
    private static Dictionary<(string Family, bool Bold, bool Italic), string>? _installedFontNames;

    /// <summary>
    /// Given a CSS font-family list, returns the installed system family the text should use (the first
    /// family that is neither generic nor one of the standard-14 aliases and is installed), or null for the
    /// standard fonts. Set by the render pipeline for the duration of a render; setting it clears the names
    /// derived from the previous resolver.
    /// </summary>
    public static Func<string, string?>? InstalledFamilyResolver
    {
        get => _installedFamilyResolver;
        set
        {
            _installedFamilyResolver = value;
            _installedFontNames?.Clear();
        }
    }

    internal static string? InstalledFontName(string? fontFamily, bool bold, bool italic)
    {
        var resolver = _installedFamilyResolver;
        if (resolver == null || string.IsNullOrEmpty(fontFamily)) return null;
        _installedFontNames ??= new Dictionary<(string, bool, bool), string>();
        var key = (fontFamily!, bold, italic);
        if (_installedFontNames.TryGetValue(key, out var known)) return known.Length == 0 ? null : known;

        var family = resolver(fontFamily!);
        string name = family == null ? "" : PdfSafeFamilyName(family) + StyleSuffix(bold, italic);
        _installedFontNames[key] = name;
        return name.Length == 0 ? null : name;
    }

    private static string PdfSafeFamilyName(string family)
    {
        var safe = new System.Text.StringBuilder(family.Length);
        foreach (char c in family)
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) safe.Append(c);
        return safe.Length == 0 ? "Font" : safe.ToString();
    }

    private static string StyleSuffix(bool bold, bool italic) =>
        bold && italic ? "-BoldItalic" : bold ? "-Bold" : italic ? "-Italic" : "";

    /// <summary>
    /// Optional per-render provider resolving (font-family list, weight, style, text) to a real
    /// font able to shape that text's script (Thai, Indic, Arabic with marks). Consulted only for
    /// text needing glyph-level shaping, so Latin-only documents never invoke it.
    /// </summary>
    [ThreadStatic]
    public static Func<string?, string?, string?, string, EggPdf.Text.TrueType.FontData?>? ComplexFontProvider;

    /// <summary>
    /// Optional per-render provider returning the advance, in em, of the glyph the PDF actually paints for
    /// a code point above U+00FF: the document's own font when it covers it, otherwise the fallback font
    /// chosen for it. Arguments are (font-family list, weight, style, code point); a negative result means
    /// "unknown" and measurement keeps its default estimate. Without it, built-in-font measurement
    /// assumes half an em for every non-ASCII character, which is badly wrong for CJK and other scripts
    /// drawn from fallback fonts.
    /// </summary>
    [ThreadStatic]
    public static Func<string?, string?, string?, int, float>? GlyphAdvanceProvider;

    /// <summary>Measure the width of text in pixels using standard font metrics.</summary>
    public static float MeasureWidth(string text, float fontSize, string? fontFamily)
    {
        return MeasureWidth(text, fontSize, fontFamily, null, null);
    }

    /// <summary>Measure the width of text with font weight/style for accurate font selection.</summary>
    public static float MeasureWidth(string text, float fontSize, string? fontFamily,
        string? fontWeight, string? fontStyle)
    {
        return MeasureWidth(text, fontSize, fontFamily, fontWeight, fontStyle, 0f);
    }

    /// <summary>
    /// Measure the width of text including letter-spacing. The PDF Tc operator
    /// applies spacing after every glyph (including the last), so measurement
    /// adds letterSpacing per glyph to match painting.
    /// </summary>
    public static float MeasureWidth(string text, float fontSize, string? fontFamily,
        string? fontWeight, string? fontStyle, float letterSpacing)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        // Measure the contextual (shaped) form the renderer will paint -- Arabic medial forms
        // are narrower than isolated ones and lam-alef ligatures merge two letters into one.
        // No-op (single early-exit scan) for non-Arabic text.
        text = EggPdf.Text.ArabicShaper.Shape(text);

        // Complex scripts (Thai, Indic, Arabic with marks): width is the sum of GSUB/GPOS-shaped
        // advances (conjuncts collapse glyphs, marks are zero-width), not per-codepoint widths.
        if (ComplexFontProvider != null && EggPdf.Text.OpenType.ComplexTextShaper.NeedsShaping(text))
        {
            var complexFont = ComplexFontProvider(fontFamily, fontWeight, fontStyle, text);
            if (complexFont != null && complexFont.UnitsPerEm > 0)
            {
                var shaped = EggPdf.Text.OpenType.ComplexTextShaper.Shape(complexFont, text, baseRtl: false);
                long units = 0;
                for (int gi = 0; gi < shaped.Length; gi++) units += shaped[gi].XAdvance;
                float shapedWidth = units * fontSize / complexFont.UnitsPerEm;
                if (letterSpacing != 0) shapedWidth += letterSpacing * shaped.Length;
                return shapedWidth;
            }
        }

        float width;
        int glyphCount = 0;

        var fontData = FontDataProvider?.Invoke(fontFamily, fontWeight, fontStyle);
        if (fontData != null && fontData.UnitsPerEm > 0)
        {
            // Real font metrics (webfont) — matches what the PDF will paint
            var pdfFont = StandardFontMetrics.ResolvePdfFontName(fontFamily, fontWeight, fontStyle);
            float w = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int cp = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                glyphCount++;

                var gid = fontData.GetGlyphId(cp);
                if (gid > 0)
                {
                    w += fontData.GetAdvanceWidth(gid) * fontSize / fontData.UnitsPerEm;
                    continue;
                }

                float fallbackEm = cp > 0xFF && GlyphAdvanceProvider != null
                    ? GlyphAdvanceProvider(fontFamily, fontWeight, fontStyle, cp) : -1f;
                if (fallbackEm >= 0)
                    w += fallbackEm * fontSize;
                else if (cp <= char.MaxValue)
                    w += StandardFontMetrics.MeasureCharWidth((char)cp, fontSize, pdfFont);
                else
                    w += fontSize * 0.5f;
            }
            width = w;
        }
        else
        {
            var pdfFont = StandardFontMetrics.ResolvePdfFontName(fontFamily, fontWeight, fontStyle);
            width = StandardFontMetrics.MeasureWidth(text, fontSize, pdfFont);
            if (GlyphAdvanceProvider != null)
                width += NonLatinAdjustment(text, fontSize, fontFamily, fontWeight, fontStyle, pdfFont);
            if (letterSpacing != 0)
                glyphCount = CountGlyphs(text);
        }

        if (letterSpacing != 0)
            width += letterSpacing * glyphCount;

        return width;
    }

    /// <summary>
    /// Correction to the built-in-font width estimate for characters above U+00FF: the table-based
    /// estimate charges every such UTF-16 unit the same flat width, while the PDF paints each with the
    /// real glyph advance <see cref="GlyphAdvanceProvider"/> reports.
    /// </summary>
    private static float NonLatinAdjustment(string text, float fontSize, string? fontFamily,
        string? fontWeight, string? fontStyle, string pdfFont)
    {
        float delta = 0;
        float flatWidth = -1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c <= 0xFF) continue;

            int cp = c;
            int units = 1;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(c, text[i + 1]);
                units = 2;
                i++;
            }

            float advanceEm = GlyphAdvanceProvider!(fontFamily, fontWeight, fontStyle, cp);
            if (advanceEm < 0) continue;
            if (flatWidth < 0) flatWidth = StandardFontMetrics.MeasureCharWidth('Ā', fontSize, pdfFont);
            delta += advanceEm * fontSize - flatWidth * units;
        }
        return delta;
    }

    /// <summary>
    /// Baseline offset from the line-box top: half-leading plus ascent, using the
    /// real font's metrics when a webfont provider is active (falls back to the
    /// 0.86em/0.14em approximation for built-in fonts).
    /// </summary>
    public static float GetBaselineOffset(float fontSize, float lineBoxHeight,
        string? fontFamily, string? fontWeight, string? fontStyle, string? text = null)
    {
        float asc = 0.86f, desc = 0.14f;
        var complex = text == null ? null : ComplexMetrics(fontFamily, fontWeight, fontStyle, text);
        if (complex.HasValue)
        {
            asc = complex.Value.ascent;
            desc = complex.Value.descent;
        }
        else
        {
            var fd = FontDataProvider?.Invoke(fontFamily, fontWeight, fontStyle);
            if (fd != null && fd.UnitsPerEm > 0 && fd.Ascent > 0)
            {
                float a = (float)fd.Ascent / fd.UnitsPerEm;
                float d = Math.Abs((float)fd.Descent) / fd.UnitsPerEm;
                if (a >= 0.5f && a <= 1.2f && d <= 0.6f)
                {
                    asc = a;
                    desc = d;
                }
            }
        }
        float halfLeading = (lineBoxHeight - (asc + desc) * fontSize) / 2f;
        return halfLeading + asc * fontSize;
    }

    /// <summary>
    /// Ascent/descent/line-gap (in em) of the font that shapes <paramref name="text"/>, for complex
    /// scripts only. Tall fonts (Myanmar, Khmer, Indic) need far more than the 1.2em default line box
    /// or consecutive lines collide; null for text that isn't shaped or has no shaping font.
    /// </summary>
    private static (float ascent, float descent, float lineGap)? ComplexMetrics(
        string? fontFamily, string? fontWeight, string? fontStyle, string text)
    {
        if (ComplexFontProvider == null || !EggPdf.Text.OpenType.ComplexTextShaper.NeedsShaping(text)) return null;
        var font = ComplexFontProvider(fontFamily, fontWeight, fontStyle, text);
        if (font == null || font.UnitsPerEm <= 0 || font.Ascent <= 0) return null;

        float asc = (float)font.Ascent / font.UnitsPerEm;
        float desc = Math.Abs((float)font.Descent) / font.UnitsPerEm;
        float gap = Math.Max(0, font.LineGap) / (float)font.UnitsPerEm;
        return asc + desc >= 0.5f && asc + desc <= 3f ? (asc, desc, gap) : ((float, float, float)?)null;
    }

    /// <summary>
    /// Line height honouring font metrics: <c>line-height: normal</c> is the font's ascent + descent + line gap
    /// (as browsers compute it) for complex-script text and for any real font the
    /// <see cref="FontDataProvider"/> resolves; the built-in standard fonts keep the fixed 1.2em.
    /// Everything else defers to <see cref="GetLineHeight(float, string?)"/>.
    /// </summary>
    public static float GetLineHeight(float fontSize, string? lineHeight,
        string? fontFamily, string? fontWeight, string? fontStyle, string text)
    {
        if (string.IsNullOrEmpty(lineHeight) || lineHeight == "normal")
        {
            var m = ComplexMetrics(fontFamily, fontWeight, fontStyle, text);
            if (m.HasValue) return fontSize * (m.Value.ascent + m.Value.descent + m.Value.lineGap);
            var normal = NormalLineHeightEm(fontFamily, fontWeight, fontStyle);
            if (normal.HasValue) return fontSize * normal.Value;
        }
        return GetLineHeight(fontSize, lineHeight);
    }

    private const float MinNormalLineHeightEm = 0.8f;
    private const float MaxNormalLineHeightEm = 3f;

    private static float? NormalLineHeightEm(string? fontFamily, string? fontWeight, string? fontStyle)
    {
        var fd = FontDataProvider?.Invoke(fontFamily, fontWeight, fontStyle);
        if (fd == null || fd.UnitsPerEm <= 0 || fd.Ascent <= 0) return null;
        float em = (fd.Ascent + Math.Abs((float)fd.Descent) + Math.Max(0, fd.LineGap)) / fd.UnitsPerEm;
        return em >= MinNormalLineHeightEm && em <= MaxNormalLineHeightEm ? em : (float?)null;
    }

    /// <summary>Count glyphs (surrogate pairs form one glyph).</summary>
    private static int CountGlyphs(string text)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                i++;
            count++;
        }
        return count;
    }

    /// <summary>Get line height for a font size.</summary>
    public static float GetLineHeight(float fontSize, string? lineHeight)
    {
        if (!string.IsNullOrEmpty(lineHeight) && lineHeight != "normal")
        {
            if (float.TryParse(lineHeight, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float multiplier))
                return fontSize * multiplier;
            float length = BlockLayout.ResolveLength(lineHeight, fontSize, fontSize);
            if (length > 0f)
                return length;
        }
        return fontSize * DefaultLineHeight;
    }

    /// <summary>
    /// Break text into lines that fit within maxWidth.
    /// Returns list of line strings.
    /// </summary>
    public static List<string> WrapText(string text, float fontSize, string? fontFamily, float maxWidth)
    {
        return WrapText(text, fontSize, fontFamily, null, null, maxWidth);
    }

    /// <summary>
    /// Break text into lines that fit within maxWidth, with font weight/style awareness.
    /// </summary>
    public static List<string> WrapText(string text, float fontSize, string? fontFamily,
        string? fontWeight, string? fontStyle, float maxWidth)
    {
        return WrapText(text, fontSize, fontFamily, fontWeight, fontStyle, maxWidth, "normal");
    }

    /// <summary>
    /// Break text into lines respecting white-space CSS property.
    /// </summary>
    public static List<string> WrapText(string text, float fontSize, string? fontFamily,
        string? fontWeight, string? fontStyle, float maxWidth, string whiteSpace)
    {
        return WrapText(text, fontSize, fontFamily, fontWeight, fontStyle, maxWidth, whiteSpace, false);
    }

        /// <summary>
    /// Break text into lines respecting white-space and overflow-wrap/word-break.
    /// When breakWord is true, words that exceed maxWidth are broken character-by-character.
    /// </summary>
    public static List<string> WrapText(string text, float fontSize, string? fontFamily,
        string? fontWeight, string? fontStyle, float maxWidth, string whiteSpace, bool breakWord)
    {
        return WrapText(text, fontSize, fontFamily, fontWeight, fontStyle, maxWidth, whiteSpace, breakWord, false);
    }

    /// <summary>
    /// Break text into lines respecting white-space, overflow-wrap/word-break, and optional hyphenation.
    /// When enableHyphenation is true, long words are broken at valid hyphenation points with a '-' appended.
    /// letterSpacing (px per glyph) is included in width calculations.
    /// </summary>
    public static List<string> WrapText(string text, float fontSize, string? fontFamily,
        string? fontWeight, string? fontStyle, float maxWidth, string whiteSpace, bool breakWord,
        bool enableHyphenation, float letterSpacing = 0f)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text))
            return lines;

        bool preserveNewlines = whiteSpace == "pre" || whiteSpace == "pre-wrap" || whiteSpace == "pre-line";
        bool preserveSpaces = whiteSpace == "pre" || whiteSpace == "pre-wrap";
        bool allowWrap = whiteSpace != "pre" && whiteSpace != "nowrap";

        if (preserveNewlines)
        {
            var physicalLines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            foreach (var physLine in physicalLines)
            {
                var processedLine = preserveSpaces ? physLine : CollapseWhitespace(physLine);

                if (allowWrap && maxWidth > 0)
                {
                    var wrappedLines = WrapSingleLine(processedLine, fontSize, fontFamily, fontWeight, fontStyle, maxWidth, preserveSpaces, breakWord, enableHyphenation, letterSpacing);
                    lines.AddRange(wrappedLines);
                }
                else
                {
                    lines.Add(processedLine);
                }
            }
        }
        else if (!allowWrap)
        {
            lines.Add(LineBreakOpportunities.RemoveInvisibleBreaks(CollapseWhitespace(text)));
        }
        else
        {
            var collapsed = CollapseWhitespace(text);
            if (maxWidth <= 0)
            {
                if (!string.IsNullOrEmpty(collapsed)) lines.Add(collapsed);
                return lines;
            }
            var wrappedLines = WrapSingleLine(collapsed, fontSize, fontFamily, fontWeight, fontStyle, maxWidth, false, breakWord, enableHyphenation, letterSpacing);
            lines.AddRange(wrappedLines);
        }

        return lines;
    }

    /// <summary>Wrap a single line of text at word boundaries.</summary>
    private static List<string> WrapSingleLine(string text, float fontSize, string? fontFamily,
        string? fontWeight, string? fontStyle, float maxWidth, bool preserveSpaces,
        bool breakWord = false, bool enableHyphenation = false, float letterSpacing = 0f)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            lines.Add("");
            return lines;
        }

        if (maxWidth <= 0)
        {
            lines.Add(text);
            return lines;
        }

        // Split into words — preserveSpaces path uses a dedicated helper.
        string[] words;
        if (preserveSpaces)
            words = SplitPreservingSpaces(text);
        else
            words = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0) { lines.Add(""); return lines; }

        bool[]? noSpaceBefore = null;
        bool[]? softHyphenAfter = null;
        if (!preserveSpaces && (EggPdf.Text.SpacelessLineBreaker.Contains(text) || LineBreakOpportunities.HasBreaksInsideWords(text)))
        {
            var segments = new List<(string Text, bool JoinsPrevious, bool EndsAtSoftHyphen)>(words.Length + 8);
            foreach (var w in words)
            {
                if (EggPdf.Text.SpacelessLineBreaker.Contains(w))
                {
                    var syllables = EggPdf.Text.SpacelessLineBreaker.Split(w);
                    for (int s = 0; s < syllables.Count; s++)
                        LineBreakOpportunities.AppendSegments(syllables[s], s > 0, segments);
                }
                else
                {
                    LineBreakOpportunities.AppendSegments(w, false, segments);
                }
            }
            if (segments.Count == 0) { lines.Add(""); return lines; }

            words = new string[segments.Count];
            noSpaceBefore = new bool[segments.Count];
            softHyphenAfter = new bool[segments.Count];
            for (int s = 0; s < segments.Count; s++)
            {
                words[s] = segments[s].Text;
                noSpaceBefore[s] = s > 0 && segments[s].JoinsPrevious;
                softHyphenAfter[s] = segments[s].EndsAtSoftHyphen;
            }
        }

        var currentLine = words[0];
        bool currentEndsAtSoftHyphen = softHyphenAfter != null && softHyphenAfter[0];
        float currentLineWidth = MeasureWidth(currentLine, fontSize, fontFamily, fontWeight, fontStyle, letterSpacing);
        // Measure separator width once; reused every iteration to avoid per-word allocation checks.
        float spaceWidth = preserveSpaces ? 0f : MeasureWidth(" ", fontSize, fontFamily, fontWeight, fontStyle, letterSpacing);

        // If first word is too wide, try hyphenation then character-breaking
        if (currentLineWidth > maxWidth)
        {
            if (enableHyphenation)
            {
                var hyphenated = HyphenateWordToFit(currentLine, "", fontSize, fontFamily, fontWeight, fontStyle, maxWidth, letterSpacing);
                if (hyphenated != null)
                {
                    lines.Add(hyphenated.Item1);
                    currentLine = hyphenated.Item2;
                    currentLineWidth = MeasureWidth(currentLine, fontSize, fontFamily, fontWeight, fontStyle, letterSpacing);
                }
                else if (breakWord)
                {
                    var broken = BreakWordByCharacter(currentLine, fontSize, fontFamily, fontWeight, fontStyle, maxWidth, letterSpacing);
                    for (int bi = 0; bi < broken.Count - 1; bi++) lines.Add(broken[bi]);
                    currentLine = broken.Count > 0 ? broken[broken.Count - 1] : "";
                    currentLineWidth = MeasureWidth(currentLine, fontSize, fontFamily, fontWeight, fontStyle, letterSpacing);
                }
            }
            else if (breakWord)
            {
                var broken = BreakWordByCharacter(currentLine, fontSize, fontFamily, fontWeight, fontStyle, maxWidth, letterSpacing);
                for (int bi = 0; bi < broken.Count - 1; bi++) lines.Add(broken[bi]);
                currentLine = broken.Count > 0 ? broken[broken.Count - 1] : "";
                currentLineWidth = MeasureWidth(currentLine, fontSize, fontFamily, fontWeight, fontStyle, letterSpacing);
            }
        }

        for (int i = 1; i < words.Length; i++)
        {
            string word = words[i];
            float wordWidth = MeasureWidth(word, fontSize, fontFamily, fontWeight, fontStyle, letterSpacing);
            // Check width without building candidate string — only concat when it fits.
            bool joinNoSpace = noSpaceBefore != null && noSpaceBefore[i];
            float candidateWidth = currentLineWidth + (joinNoSpace ? 0f : spaceWidth) + wordWidth;

            if (candidateWidth <= maxWidth)
            {
                currentLine = preserveSpaces || joinNoSpace ? currentLine + word : currentLine + " " + word;
                currentLineWidth = candidateWidth;
                currentEndsAtSoftHyphen = softHyphenAfter != null && softHyphenAfter[i];
            }
            else
            {
                string separator = preserveSpaces || joinNoSpace ? "" : " ";
                bool hyphenUsed = false;
                if (enableHyphenation && !currentEndsAtSoftHyphen)
                {
                    var hyphenated = HyphenateWordToFit(word, currentLine + separator, fontSize, fontFamily, fontWeight, fontStyle, maxWidth, letterSpacing);
                    if (hyphenated != null)
                    {
                        lines.Add(hyphenated.Item1);
                        currentLine = hyphenated.Item2;
                        currentLineWidth = MeasureWidth(currentLine, fontSize, fontFamily, fontWeight, fontStyle, letterSpacing);
                        currentEndsAtSoftHyphen = false;
                        hyphenUsed = true;
                    }
                }

                if (!hyphenUsed)
                {
                    lines.Add(currentEndsAtSoftHyphen && joinNoSpace ? currentLine + LineBreakOpportunities.VisibleHyphen : currentLine);
                    currentLine = word;
                    currentLineWidth = wordWidth;
                    currentEndsAtSoftHyphen = softHyphenAfter != null && softHyphenAfter[i];

                    // If this word alone exceeds maxWidth, try hyphenation then character-breaking
                    if (currentLineWidth > maxWidth)
                    {
                        if (enableHyphenation)
                        {
                            var hyphenated = HyphenateWordToFit(currentLine, "", fontSize, fontFamily, fontWeight, fontStyle, maxWidth, letterSpacing);
                            if (hyphenated != null)
                            {
                                lines.Add(hyphenated.Item1);
                                currentLine = hyphenated.Item2;
                                currentLineWidth = MeasureWidth(currentLine, fontSize, fontFamily, fontWeight, fontStyle, letterSpacing);
                            }
                            else if (breakWord)
                            {
                                var broken = BreakWordByCharacter(currentLine, fontSize, fontFamily, fontWeight, fontStyle, maxWidth, letterSpacing);
                                for (int bi = 0; bi < broken.Count - 1; bi++) lines.Add(broken[bi]);
                                currentLine = broken.Count > 0 ? broken[broken.Count - 1] : "";
                                currentLineWidth = MeasureWidth(currentLine, fontSize, fontFamily, fontWeight, fontStyle, letterSpacing);
                            }
                        }
                        else if (breakWord)
                        {
                            var broken = BreakWordByCharacter(currentLine, fontSize, fontFamily, fontWeight, fontStyle, maxWidth, letterSpacing);
                            for (int bi = 0; bi < broken.Count - 1; bi++) lines.Add(broken[bi]);
                            currentLine = broken.Count > 0 ? broken[broken.Count - 1] : "";
                            currentLineWidth = MeasureWidth(currentLine, fontSize, fontFamily, fontWeight, fontStyle, letterSpacing);
                        }
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(currentLine) || preserveSpaces)
            lines.Add(currentLine);

        return lines;
    }

    /// <summary>
    /// Try to fit a word (possibly with a prefix already on the line) by finding the best
    /// hyphenation point. Returns (lineWithHyphen, remainder) or null if no point fits.
    /// </summary>
    private static Tuple<string, string>? HyphenateWordToFit(string word, string linePrefix,
        float fontSize, string? fontFamily, string? fontWeight, string? fontStyle, float maxWidth,
        float letterSpacing = 0f)
    {
        var breakPoints = _englishHyphenator.Hyphenate(word);
        if (breakPoints.Length == 0) return null;

        // Try break points from right to left — find the rightmost that fits
        string? bestLine = null;
        string? bestRemainder = null;
        for (int k = breakPoints.Length - 1; k >= 0; k--)
        {
            int bp = breakPoints[k];
            var prefix = word.Substring(0, bp) + "-";
            var candidate = linePrefix + prefix;
            if (MeasureWidth(candidate, fontSize, fontFamily, fontWeight, fontStyle, letterSpacing) <= maxWidth)
            {
                bestLine = candidate;
                bestRemainder = word.Substring(bp);
                break;
            }
        }

        if (bestLine == null) return null;
        return Tuple.Create(bestLine, bestRemainder!);
    }

    /// <summary>Break a single word into chunks that each fit within maxWidth.</summary>
    private static List<string> BreakWordByCharacter(string word, float fontSize,
        string? fontFamily, string? fontWeight, string? fontStyle, float maxWidth, float letterSpacing = 0f)
    {
        var chunks = new List<string>();
        if (string.IsNullOrEmpty(word))
        {
            chunks.Add("");
            return chunks;
        }

        // Resolve font once; accumulate width char-by-char to avoid per-iteration Substring allocations.
        var pdfFont = StandardFontMetrics.ResolvePdfFontName(fontFamily, fontWeight, fontStyle);
        int start = 0;
        float runWidth = 0f;
        for (int i = 0; i < word.Length; i++)
        {
            float cw = MeasureWidth(word[i].ToString(), fontSize, fontFamily, fontWeight, fontStyle, letterSpacing);
            if (runWidth + cw > maxWidth && i > start)
            {
                chunks.Add(word.Substring(start, i - start));
                start = i;
                runWidth = cw;
            }
            else
            {
                runWidth += cw;
            }
        }
        if (start < word.Length)
            chunks.Add(word.Substring(start));

        return chunks;
    }

    /// <summary>Collapse sequences of whitespace into single spaces and trim.</summary>
    private static string CollapseWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var result = new char[text.Length];
        int len = 0;
        bool lastWasSpace = true; // trim leading

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
            {
                if (!lastWasSpace)
                {
                    result[len++] = ' ';
                    lastWasSpace = true;
                }
            }
            else
            {
                result[len++] = c;
                lastWasSpace = false;
            }
        }

        // Trim trailing space
        if (len > 0 && result[len - 1] == ' ')
            len--;

        return new string(result, 0, len);
    }

    /// <summary>
    /// Expand tab characters in text using a fixed tab-size (number of spaces per tab).
    /// Used by pre/pre-wrap whitespace modes to honour the CSS tab-size property.
    /// </summary>
    public static string ExpandTabs(string text, int tabSize)
    {
        if (string.IsNullOrEmpty(text) || tabSize <= 0) return text;
        if (text.IndexOf('\t') < 0) return text; // fast path: no tabs

        var sb = new System.Text.StringBuilder(text.Length + tabSize);
        int col = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\t')
            {
                int spaces = tabSize - (col % tabSize);
                sb.Append(' ', spaces);
                col += spaces;
            }
            else if (c == '\n' || c == '\r')
            {
                sb.Append(c);
                col = 0;
            }
            else
            {
                sb.Append(c);
                col++;
            }
        }
        return sb.ToString();
    }

    /// <summary>Split text into chunks preserving spaces (for pre/pre-wrap).</summary>
    private static string[] SplitPreservingSpaces(string text)
    {
        var parts = new List<string>();
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ')
            {
                if (i > start)
                    parts.Add(text.Substring(start, i - start));
                parts.Add(" ");
                start = i + 1;
            }
        }

        if (start < text.Length)
            parts.Add(text.Substring(start));

        return parts.ToArray();
    }
}
