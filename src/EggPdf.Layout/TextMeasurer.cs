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
                    w += fontData.GetAdvanceWidth(gid) * fontSize / fontData.UnitsPerEm;
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
            if (letterSpacing != 0)
                glyphCount = CountGlyphs(text);
        }

        if (letterSpacing != 0)
            width += letterSpacing * glyphCount;

        return width;
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
            lines.Add(CollapseWhitespace(text));
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

        var currentLine = words[0];
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
            float candidateWidth = currentLineWidth + spaceWidth + wordWidth;

            if (candidateWidth <= maxWidth)
            {
                currentLine = preserveSpaces ? currentLine + word : currentLine + " " + word;
                currentLineWidth = candidateWidth;
            }
            else
            {
                string separator = preserveSpaces ? "" : " ";
                bool hyphenUsed = false;
                if (enableHyphenation)
                {
                    var hyphenated = HyphenateWordToFit(word, currentLine + separator, fontSize, fontFamily, fontWeight, fontStyle, maxWidth, letterSpacing);
                    if (hyphenated != null)
                    {
                        lines.Add(hyphenated.Item1);
                        currentLine = hyphenated.Item2;
                        currentLineWidth = MeasureWidth(currentLine, fontSize, fontFamily, fontWeight, fontStyle, letterSpacing);
                        hyphenUsed = true;
                    }
                }

                if (!hyphenUsed)
                {
                    lines.Add(currentLine);
                    currentLine = word;
                    currentLineWidth = wordWidth;

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
