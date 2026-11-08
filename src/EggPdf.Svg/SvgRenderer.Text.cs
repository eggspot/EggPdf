using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EggPdf.Pdf;

namespace EggPdf.Svg;

public static partial class SvgRenderer
{
    private const float AverageGlyphWidthEm = 0.5f;

    [ThreadStatic]
    private static Func<string, float, string, float>? _measureTextWidth;

    private sealed class TextRun
    {
        public TextRun(SvgElement style, string text, float? x, float? y, float dx, float dy)
        {
            Style = style;
            Text = text;
            X = x;
            Y = y;
            Dx = dx;
            Dy = dy;
        }

        public SvgElement Style { get; }
        public string Text { get; set; }
        public float? X { get; }
        public float? Y { get; }
        public float Dx { get; }
        public float Dy { get; }
        public float StartX { get; set; }
        public float StartY { get; set; }
        public float Width { get; set; }
        public int Chunk { get; set; }
    }

    private static void RenderText(SvgElement text, StringBuilder sb)
    {
        var runs = new List<TextRun>();
        bool previousEndsWithSpace = true;
        CollectTextRuns(text, runs, ref previousEndsWithSpace, null, null, 0f, 0f);
        TrimTrailingSpace(runs);
        if (runs.Count == 0) return;

        var chunkShifts = LayOutTextRuns(runs);
        sb.AppendOpLine("q");
        foreach (var run in runs)
        {
            if (IsInvisible(run.Style)) continue;
            sb.AppendOpLine("q");
            if (ApplyTextPaint(run.Style, sb))
            {
                float fontSize = FontSizeOf(run.Style);
                float letterSpacing = GetFloat(run.Style, "letter-spacing");
                float x = run.StartX + chunkShifts[run.Chunk];
                float y = run.StartY + BaselineShift(run.Style, fontSize);
                sb.Append("BT /").Append(StandardFontFor(run.Style)).Append(' ').Append(F(fontSize)).Append(" Tf ");
                if (letterSpacing != 0f) sb.Append(F(letterSpacing)).Append(" Tc ");
                sb.Append("1 0 0 -1 ").Append(F(x)).Append(' ').Append(F(y)).Append(" Tm (");
                PdfPage.AppendEscapedPdfString(sb, run.Text);
                sb.AppendOpLine(") Tj ET");
            }
            sb.AppendOpLine("Q");
        }
        sb.AppendOpLine("Q");
    }

    private static bool CollectTextRuns(SvgElement element, List<TextRun> runs, ref bool previousEndsWithSpace,
        float? x, float? y, float dx, float dy)
    {
        x = FirstListLength(element, "x") ?? x;
        y = FirstListLength(element, "y") ?? y;
        dx += FirstListLength(element, "dx") ?? 0f;
        dy += FirstListLength(element, "dy") ?? 0f;

        bool positionUsed = false;
        foreach (var child in element.Children)
        {
            if (child.TagName == SvgParser.TextRunTagName)
            {
                var collapsed = CollapseWhitespace(child.TextContent, ref previousEndsWithSpace);
                if (collapsed.Length == 0) continue;
                runs.Add(positionUsed
                    ? new TextRun(element, collapsed, null, null, 0f, 0f)
                    : new TextRun(element, collapsed, x, y, dx, dy));
                positionUsed = true;
            }
            else if (SvgParser.HoldsTextRuns(child.TagName) && child.GetAttribute("display").Trim() != "none")
            {
                bool childUsedPosition = positionUsed
                    ? CollectTextRuns(child, runs, ref previousEndsWithSpace, null, null, 0f, 0f)
                    : CollectTextRuns(child, runs, ref previousEndsWithSpace, x, y, dx, dy);
                positionUsed |= childUsedPosition;
            }
        }
        return positionUsed;
    }

    private static string CollapseWhitespace(string text, ref bool previousEndsWithSpace)
    {
        var collapsed = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            bool space = c == ' ' || c == '\t' || c == '\n' || c == '\r';
            if (space)
            {
                if (!previousEndsWithSpace) collapsed.Append(' ');
                previousEndsWithSpace = true;
            }
            else
            {
                collapsed.Append(c);
                previousEndsWithSpace = false;
            }
        }
        return collapsed.ToString();
    }

    private static void TrimTrailingSpace(List<TextRun> runs)
    {
        while (runs.Count > 0)
        {
            var last = runs[runs.Count - 1];
            last.Text = last.Text.TrimEnd(' ');
            if (last.Text.Length > 0) return;
            runs.RemoveAt(runs.Count - 1);
        }
    }

    private static float[] LayOutTextRuns(List<TextRun> runs)
    {
        float penX = 0f, penY = 0f;
        int chunk = -1;
        var chunkStarts = new List<float>();
        var chunkEnds = new List<float>();
        var chunkAnchors = new List<string>();
        foreach (var run in runs)
        {
            if (run.X.HasValue || chunk < 0)
            {
                chunk++;
                penX = run.X ?? penX;
                chunkStarts.Add(penX + run.Dx);
                chunkEnds.Add(penX + run.Dx);
                chunkAnchors.Add(run.Style.GetAttribute("text-anchor").Trim());
            }
            if (run.Y.HasValue) penY = run.Y.Value;
            penX += run.Dx;
            penY += run.Dy;
            run.StartX = penX;
            run.StartY = penY;
            run.Width = MeasureRun(run);
            run.Chunk = chunk;
            penX += run.Width;
            chunkEnds[chunk] = penX;
        }

        var shifts = new float[chunkStarts.Count];
        for (int i = 0; i < shifts.Length; i++)
        {
            float width = chunkEnds[i] - chunkStarts[i];
            shifts[i] = chunkAnchors[i] switch
            {
                "middle" => -width / 2f,
                "end" => -width,
                _ => 0f,
            };
        }
        return shifts;
    }

    private static float MeasureRun(TextRun run)
    {
        float fontSize = FontSizeOf(run.Style);
        float letterSpacing = GetFloat(run.Style, "letter-spacing") * run.Text.Length;
        float width = _measureTextWidth != null
            ? _measureTextWidth(run.Text, fontSize, StandardFontName(run.Style))
            : run.Text.Length * fontSize * AverageGlyphWidthEm;
        return width + letterSpacing;
    }

    private static float FontSizeOf(SvgElement el)
    {
        float size = GetFloat(el, "font-size", DefaultFontSizePx);
        return size > 0f ? size : DefaultFontSizePx;
    }

    private static float BaselineShift(SvgElement el, float fontSize)
    {
        var baseline = el.GetAttribute("alignment-baseline").Trim();
        if (baseline.Length == 0 || baseline == "auto" || baseline == "baseline")
            baseline = el.GetAttribute("dominant-baseline").Trim();
        switch (baseline)
        {
            case "middle": return 0.26f * fontSize;
            case "central":
            case "mathematical": return 0.35f * fontSize;
            case "hanging":
            case "text-before-edge":
            case "text-top": return 0.8f * fontSize;
            case "text-after-edge":
            case "text-bottom":
            case "ideographic": return -0.2f * fontSize;
            default: return 0f;
        }
    }

    private static float? FirstListLength(SvgElement el, string attr)
    {
        var value = el.GetAttribute(attr).Trim();
        if (value.Length == 0) return null;
        int end = value.IndexOfAny(LengthListSeparators);
        if (end > 0) value = value.Substring(0, end);
        float parsed = ParseLength(value, el, attr, float.NaN);
        return float.IsNaN(parsed) ? null : parsed;
    }

    private static readonly char[] LengthListSeparators = { ' ', ',', '\t', '\n', '\r' };

    private static string StandardFontFor(SvgElement text)
    {
        var font = StandardFontName(text);
        if (_usedFonts != null && !_usedFonts.Contains(font)) _usedFonts.Add(font);
        return font;
    }

    private static string StandardFontName(SvgElement text)
    {
        var family = FirstFontFamily(text.GetAttribute("font-family"));
        var weight = text.GetAttribute("font-weight").Trim();
        bool bold = weight == "bold" || weight == "bolder" ||
                    (int.TryParse(weight, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numericWeight) && numericWeight >= 600);
        var style = text.GetAttribute("font-style").Trim();
        bool italic = style == "italic" || style.StartsWith("oblique", StringComparison.Ordinal);

        if (family.IndexOf("mono", StringComparison.Ordinal) >= 0 || family.IndexOf("courier", StringComparison.Ordinal) >= 0 ||
            family.IndexOf("consolas", StringComparison.Ordinal) >= 0)
            return bold && italic ? "Courier-BoldOblique" : bold ? "Courier-Bold" : italic ? "Courier-Oblique" : "Courier";
        if ((family.IndexOf("serif", StringComparison.Ordinal) >= 0 && family.IndexOf("sans", StringComparison.Ordinal) < 0) ||
            family.IndexOf("times", StringComparison.Ordinal) >= 0 || family.IndexOf("georgia", StringComparison.Ordinal) >= 0 ||
            family.IndexOf("garamond", StringComparison.Ordinal) >= 0)
            return bold && italic ? "Times-BoldItalic" : bold ? "Times-Bold" : italic ? "Times-Italic" : "Times-Roman";
        return bold && italic ? "Helvetica-BoldOblique" : bold ? "Helvetica-Bold" : italic ? "Helvetica-Oblique" : "Helvetica";
    }

    private static string FirstFontFamily(string fontFamily)
    {
        int comma = fontFamily.IndexOf(',');
        var first = comma >= 0 ? fontFamily.Substring(0, comma) : fontFamily;
        return first.Trim().Trim('\'', '"').ToLowerInvariant();
    }
}
