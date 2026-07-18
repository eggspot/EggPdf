using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Content-stream helpers that keep PDF output byte-identical across
/// platforms. StringBuilder.AppendLine emits Environment.NewLine, which would
/// make Windows output differ from Linux for the same input — unacceptable for
/// a library whose PDFs get hashed and signed.
/// </summary>
internal static class PdfContentStreamExtensions
{
    /// <summary>Append a line terminated by a literal LF, never CRLF.</summary>
    public static StringBuilder AppendOpLine(this StringBuilder sb, string value)
        => sb.Append(value).Append('\n');
}

/// <summary>
/// Represents a single PDF page with content operations.
/// </summary>
public class PdfPage
{
    internal float WidthPt { get; }
    internal float HeightPt { get; }
    internal StringBuilder ContentStream { get; } = new();
    internal HashSet<string> UsedFonts { get; } = new();
    internal List<PdfLinkAnnotation> Links { get; } = new();
    internal List<string> UsedImages { get; } = new();
    private readonly HashSet<string> _usedImageSet = new();
    internal HashSet<string> UsedExtGStates { get; } = new();

    internal PdfPage(float widthPt, float heightPt)
    {
        WidthPt = widthPt;
        HeightPt = heightPt;
    }

    /// <summary>Add text at a position (PDF coordinates, bottom-left origin).</summary>
    public void AddText(string text, float x, float y, string fontName, float fontSize,
        float colorR = 0, float colorG = 0, float colorB = 0,
        float letterSpacing = 0, float wordSpacing = 0)
    {
        UsedFonts.Add(fontName);
        ContentStream.AppendOpLine($"{F(colorR)} {F(colorG)} {F(colorB)} rg");
        ContentStream.Append($"BT /{fontName} {F(fontSize)} Tf ");
        ContentStream.Append($"{F(letterSpacing)} Tc ");
        ContentStream.Append($"{F(wordSpacing)} Tw ");
        ContentStream.Append($"{F(x)} {F(y)} Td ");
        ContentStream.Append('(');
        AppendEscapedPdfString(ContentStream, text);
        ContentStream.Append(") Tj ");
        ContentStream.AppendOpLine("ET");
    }

    /// <summary>Add text using CIDFont glyph IDs (for embedded TrueType fonts with full Unicode).</summary>
    public void AddTextCID(ushort[] glyphIds, float x, float y, string fontName, float fontSize,
        float colorR = 0, float colorG = 0, float colorB = 0,
        float letterSpacing = 0, float wordSpacing = 0)
    {
        UsedFonts.Add(fontName);
        ContentStream.AppendOpLine($"{F(colorR)} {F(colorG)} {F(colorB)} rg");
        ContentStream.Append($"BT /{fontName} {F(fontSize)} Tf ");
        ContentStream.Append($"{F(letterSpacing)} Tc ");
        ContentStream.Append($"{F(wordSpacing)} Tw ");
        ContentStream.Append($"{F(x)} {F(y)} Td ");
        // Encode glyph IDs as hex string: <001A002B003C>
        ContentStream.Append('<');
        foreach (var gid in glyphIds)
            ContentStream.Append(gid.ToString("X4"));
        ContentStream.Append("> Tj ");
        ContentStream.AppendOpLine("ET");
    }

    /// <summary>
    /// Add CID text composed of runs in different embedded fonts (glyph
    /// fallback). All runs share one BT block; Tj advances the text position,
    /// so runs flow naturally after each other.
    /// </summary>
    public void AddTextRunsCID(List<(string fontName, ushort[] glyphIds)> runs,
        float x, float y, float fontSize,
        float colorR = 0, float colorG = 0, float colorB = 0,
        float letterSpacing = 0, float wordSpacing = 0)
    {
        ContentStream.AppendOpLine($"{F(colorR)} {F(colorG)} {F(colorB)} rg");
        ContentStream.Append("BT ");
        ContentStream.Append($"{F(letterSpacing)} Tc ");
        ContentStream.Append($"{F(wordSpacing)} Tw ");
        ContentStream.Append($"{F(x)} {F(y)} Td ");
        foreach (var run in runs)
        {
            if (run.glyphIds.Length == 0) continue;
            UsedFonts.Add(run.fontName);
            ContentStream.Append($"/{run.fontName} {F(fontSize)} Tf ");
            ContentStream.Append('<');
            foreach (var gid in run.glyphIds)
                ContentStream.Append(gid.ToString("X4"));
            ContentStream.Append("> Tj ");
        }
        ContentStream.AppendOpLine("ET");
    }

    /// <summary>Append raw PDF content stream commands (for SVG rendering etc.).</summary>
    public void AppendRawContent(string commands)
    {
        ContentStream.Append(commands);
    }

    /// <summary>Add a filled rectangle.</summary>
    public void AddRectangle(float x, float y, float width, float height, float r, float g, float b)
    {
        ContentStream.AppendOpLine($"{F(r)} {F(g)} {F(b)} rg");
        ContentStream.AppendOpLine($"{F(x)} {F(y)} {F(width)} {F(height)} re f");
    }

    /// <summary>Add a stroked rectangle (border).</summary>
    public void AddStrokeRectangle(float x, float y, float width, float height, float r, float g, float b, float lineWidth)
    {
        ContentStream.AppendOpLine($"{F(lineWidth)} w");
        ContentStream.AppendOpLine($"{F(r)} {F(g)} {F(b)} RG");
        ContentStream.AppendOpLine($"{F(x)} {F(y)} {F(width)} {F(height)} re S");
    }

    /// <summary>Set dash pattern for subsequent strokes. Empty array = solid.</summary>
    public void SetDashPattern(float[] dashArray, float dashPhase = 0)
    {
        if (dashArray == null || dashArray.Length == 0)
        {
            ContentStream.AppendOpLine("[] 0 d");
            return;
        }
        var sb = new StringBuilder("[");
        for (int i = 0; i < dashArray.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(F(dashArray[i]));
        }
        sb.Append("] ");
        sb.Append(F(dashPhase));
        sb.Append(" d");
        ContentStream.AppendOpLine(sb.ToString());
    }

    /// <summary>Set line cap style: 0=butt, 1=round, 2=square.</summary>
    public void SetLineCap(int cap)
    {
        ContentStream.AppendOpLine($"{cap} J");
    }

    /// <summary>Add a stroked line between two points (border side).</summary>
    public void AddBorderLine(float x1, float y1, float x2, float y2,
        float r, float g, float b, float lineWidth, string borderStyle)
    {
        ContentStream.AppendOpLine($"{F(lineWidth)} w");
        ContentStream.AppendOpLine($"{F(r)} {F(g)} {F(b)} RG");

        switch (borderStyle)
        {
            case "dashed":
                // Dash pattern: 3x line width on, 3x off
                float dashLen = lineWidth * 3;
                ContentStream.AppendOpLine($"[{F(dashLen)} {F(dashLen)}] 0 d");
                ContentStream.AppendOpLine("0 J"); // butt cap
                ContentStream.AppendOpLine($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S");
                ContentStream.AppendOpLine("[] 0 d"); // reset dash
                break;

            case "dotted":
                // Dotted: round cap with 0-length dash
                ContentStream.AppendOpLine($"[0 {F(lineWidth * 2)}] 0 d");
                ContentStream.AppendOpLine("1 J"); // round cap
                ContentStream.AppendOpLine($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S");
                ContentStream.AppendOpLine("[] 0 d"); // reset dash
                ContentStream.AppendOpLine("0 J"); // reset cap
                break;

            case "double":
            {
                // Double: two lines at 1/3 width with 1/3 gap
                float third = lineWidth / 3;
                if (third < 0.5f) third = 0.5f;
                ContentStream.AppendOpLine($"{F(third)} w");

                // Calculate perpendicular offset for the two lines
                float dx = x2 - x1, dy = y2 - y1;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len < 0.001f) break;
                float nx = -dy / len * third, ny = dx / len * third;

                // First line (offset outward)
                ContentStream.AppendOpLine($"{F(x1 + nx)} {F(y1 + ny)} m {F(x2 + nx)} {F(y2 + ny)} l S");
                // Second line (offset inward)
                ContentStream.AppendOpLine($"{F(x1 - nx)} {F(y1 - ny)} m {F(x2 - nx)} {F(y2 - ny)} l S");
                break;
            }

            case "groove":
            case "ridge":
            {
                // 3D effect: two half-width lines with lighter/darker colors
                float half = lineWidth / 2;
                if (half < 0.5f) half = 0.5f;

                float dx = x2 - x1, dy = y2 - y1;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len < 0.001f) break;
                float nx = -dy / len * (half / 2), ny = dx / len * (half / 2);

                float darkR = r * 0.5f, darkG = g * 0.5f, darkB = b * 0.5f;
                float lightR = Math.Min(r * 1.5f, 1f), lightG = Math.Min(g * 1.5f, 1f), lightB = Math.Min(b * 1.5f, 1f);

                bool grooveFirst = borderStyle == "groove";
                float r1 = grooveFirst ? darkR : lightR, g1 = grooveFirst ? darkG : lightG, b1 = grooveFirst ? darkB : lightB;
                float r2 = grooveFirst ? lightR : darkR, g2 = grooveFirst ? lightG : darkG, b2 = grooveFirst ? lightB : darkB;

                ContentStream.AppendOpLine($"{F(half)} w");
                ContentStream.AppendOpLine($"{F(r1)} {F(g1)} {F(b1)} RG");
                ContentStream.AppendOpLine($"{F(x1 + nx)} {F(y1 + ny)} m {F(x2 + nx)} {F(y2 + ny)} l S");
                ContentStream.AppendOpLine($"{F(r2)} {F(g2)} {F(b2)} RG");
                ContentStream.AppendOpLine($"{F(x1 - nx)} {F(y1 - ny)} m {F(x2 - nx)} {F(y2 - ny)} l S");
                break;
            }

            case "inset":
            case "outset":
            {
                // Inset/outset: simple color adjustment
                bool darken = borderStyle == "inset";
                float adjR = darken ? r * 0.6f : Math.Min(r * 1.4f, 1f);
                float adjG = darken ? g * 0.6f : Math.Min(g * 1.4f, 1f);
                float adjB = darken ? b * 0.6f : Math.Min(b * 1.4f, 1f);
                ContentStream.AppendOpLine($"{F(adjR)} {F(adjG)} {F(adjB)} RG");
                ContentStream.AppendOpLine($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S");
                break;
            }

            default: // solid
                ContentStream.AppendOpLine($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S");
                break;
        }
    }

    /// <summary>Add a filled rounded rectangle using Bézier curves for corners.</summary>
    public void AddRoundedRectangle(float x, float y, float w, float h,
        float r, float g, float b,
        float tlr, float trr, float brr, float blr)
    {
        // Clamp radii to half dimensions
        float maxRadiusW = w / 2f;
        float maxRadiusH = h / 2f;
        tlr = Math.Min(tlr, Math.Min(maxRadiusW, maxRadiusH));
        trr = Math.Min(trr, Math.Min(maxRadiusW, maxRadiusH));
        brr = Math.Min(brr, Math.Min(maxRadiusW, maxRadiusH));
        blr = Math.Min(blr, Math.Min(maxRadiusW, maxRadiusH));

        ContentStream.AppendOpLine($"{F(r)} {F(g)} {F(b)} rg");
        AppendRoundedRectPath(x, y, w, h, tlr, trr, brr, blr);
        ContentStream.AppendOpLine("h f");
    }

    /// <summary>Add a stroked rounded rectangle using Bézier curves for corners.</summary>
    public void AddStrokeRoundedRectangle(float x, float y, float w, float h,
        float r, float g, float b, float lineWidth,
        float tlr, float trr, float brr, float blr)
    {
        // Clamp radii to half dimensions
        float maxRadiusW = w / 2f;
        float maxRadiusH = h / 2f;
        tlr = Math.Min(tlr, Math.Min(maxRadiusW, maxRadiusH));
        trr = Math.Min(trr, Math.Min(maxRadiusW, maxRadiusH));
        brr = Math.Min(brr, Math.Min(maxRadiusW, maxRadiusH));
        blr = Math.Min(blr, Math.Min(maxRadiusW, maxRadiusH));

        ContentStream.AppendOpLine($"{F(lineWidth)} w");
        ContentStream.AppendOpLine($"{F(r)} {F(g)} {F(b)} RG");
        AppendRoundedRectPath(x, y, w, h, tlr, trr, brr, blr);
        ContentStream.AppendOpLine("h S");
    }

    /// <summary>Appends the rounded rectangle path operators (m, l, c) to the content stream.</summary>
    private void AppendRoundedRectPath(float x, float y, float w, float h,
        float tlr, float trr, float brr, float blr)
    {
        // Kappa constant for approximating quarter-circle arcs with cubic Bézier curves
        const float k = 0.5522847498f;

        // PDF coordinate system: Y increases upward
        // Start at top-left corner (after TL radius), go clockwise

        // Move to start of top edge (after TL radius)
        ContentStream.AppendOpLine($"{F(x + tlr)} {F(y + h)} m");

        // Top edge line to start of TR radius
        ContentStream.AppendOpLine($"{F(x + w - trr)} {F(y + h)} l");

        // TR corner curve
        if (trr > 0)
            ContentStream.AppendOpLine($"{F(x + w - trr + trr * k)} {F(y + h)} {F(x + w)} {F(y + h - trr + trr * k)} {F(x + w)} {F(y + h - trr)} c");
        else
            ContentStream.AppendOpLine($"{F(x + w)} {F(y + h)} l");

        // Right edge line to start of BR radius
        ContentStream.AppendOpLine($"{F(x + w)} {F(y + brr)} l");

        // BR corner curve
        if (brr > 0)
            ContentStream.AppendOpLine($"{F(x + w)} {F(y + brr - brr * k)} {F(x + w - brr + brr * k)} {F(y)} {F(x + w - brr)} {F(y)} c");
        else
            ContentStream.AppendOpLine($"{F(x + w)} {F(y)} l");

        // Bottom edge line to start of BL radius
        ContentStream.AppendOpLine($"{F(x + blr)} {F(y)} l");

        // BL corner curve
        if (blr > 0)
            ContentStream.AppendOpLine($"{F(x + blr - blr * k)} {F(y)} {F(x)} {F(y + blr - blr * k)} {F(x)} {F(y + blr)} c");
        else
            ContentStream.AppendOpLine($"{F(x)} {F(y)} l");

        // Left edge line to start of TL radius
        ContentStream.AppendOpLine($"{F(x)} {F(y + h - tlr)} l");

        // TL corner curve
        if (tlr > 0)
            ContentStream.AppendOpLine($"{F(x)} {F(y + h - tlr + tlr * k)} {F(x + tlr - tlr * k)} {F(y + h)} {F(x + tlr)} {F(y + h)} c");
        else
            ContentStream.AppendOpLine($"{F(x)} {F(y + h)} l");
    }

    /// <summary>Set fill and stroke opacity (0.0-1.0). Creates an ExtGState reference.</summary>
    public void SetOpacity(float opacity)
    {
        // SetOpacity(1) must EMIT a reset state — the gs operator persists, so
        // silently skipping it leaks the previous alpha into later content.
        if (opacity > 1.0f) opacity = 1.0f;
        if (opacity < 0f) opacity = 0f;
        var gsName = $"GS{(int)(opacity * 100)}";
        UsedExtGStates.Add(gsName);
        ContentStream.AppendOpLine($"/{gsName} gs");
    }

    /// <summary>
    /// Set PDF blend mode. CSS blend mode names are mapped to PDF /BM names.
    /// Call SaveState/RestoreState around the element to scope the blend mode.
    /// </summary>
    public void SetBlendMode(string cssBlendMode)
    {
        var pdfBm = CssBlendModeToPdf(cssBlendMode);
        if (pdfBm == null) return;
        var gsName = $"GSBM_{cssBlendMode.ToLowerInvariant().Replace('-', '_')}";
        UsedExtGStates.Add(gsName);
        ContentStream.AppendOpLine($"/{gsName} gs");
    }

    internal static string? CssBlendModeToPdf(string cssMode)
    {
        switch (cssMode.ToLowerInvariant())
        {
            case "normal": return "Normal";
            case "multiply": return "Multiply";
            case "screen": return "Screen";
            case "overlay": return "Overlay";
            case "darken": return "Darken";
            case "lighten": return "Lighten";
            case "color-dodge": return "ColorDodge";
            case "color-burn": return "ColorBurn";
            case "hard-light": return "HardLight";
            case "soft-light": return "SoftLight";
            case "difference": return "Difference";
            case "exclusion": return "Exclusion";
            case "hue": return "Hue";
            case "saturation": return "Saturation";
            case "color": return "Color";
            case "luminosity": return "Luminosity";
            default: return null;
        }
    }

    /// <summary>Save graphics state.</summary>
    public void SaveState()
    {
        ContentStream.AppendOpLine("q");
    }

    /// <summary>Restore graphics state.</summary>
    public void RestoreState()
    {
        ContentStream.AppendOpLine("Q");
    }

    /// <summary>Add a clipping rectangle (clips all subsequent drawing).</summary>
    public void AddClipRect(float x, float y, float width, float height)
    {
        ContentStream.AppendOpLine($"{F(x)} {F(y)} {F(width)} {F(height)} re W n");
    }

    /// <summary>Concatenate a transformation matrix (PDF cm operator).</summary>
    public void ConcatMatrix(float a, float b, float c, float d, float e, float f)
    {
        ContentStream.AppendOpLine($"{F(a)} {F(b)} {F(c)} {F(d)} {F(e)} {F(f)} cm");
    }

    /// <summary>Add an image at a position with specified dimensions (PDF coordinates).</summary>
    public void AddImage(string imageName, float x, float y, float width, float height)
    {
        if (_usedImageSet.Add(imageName)) // O(1) de-dup; list keeps insertion order
            UsedImages.Add(imageName);

        // Save graphics state, apply transformation matrix, draw image, restore
        ContentStream.AppendOpLine("q");
        ContentStream.AppendOpLine($"{F(width)} 0 0 {F(height)} {F(x)} {F(y)} cm");
        ContentStream.AppendOpLine($"/{imageName} Do");
        ContentStream.AppendOpLine("Q");
    }

    /// <summary>Add a stroked line between two points.</summary>
    public void AddLine(float x1, float y1, float x2, float y2, float r, float g, float b, float lineWidth)
    {
        ContentStream.AppendOpLine($"{F(lineWidth)} w");
        ContentStream.AppendOpLine($"{F(r)} {F(g)} {F(b)} RG");
        ContentStream.AppendOpLine($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S");
    }

    /// <summary>
    /// Add a text-decoration line with the given CSS style
    /// (solid, dashed, dotted, double, wavy).
    /// </summary>
    public void AddDecorationLine(float x1, float y1, float x2, float y2,
        float r, float g, float b, float lineWidth, string? decorationStyle)
    {
        var style = (decorationStyle ?? "solid").ToLowerInvariant();
        switch (style)
        {
            case "dashed":
                ContentStream.AppendOpLine($"{F(lineWidth)} w");
                ContentStream.AppendOpLine($"{F(r)} {F(g)} {F(b)} RG");
                float dashLen = lineWidth * 4;
                ContentStream.AppendOpLine($"[{F(dashLen)} {F(dashLen)}] 0 d 0 J");
                ContentStream.AppendOpLine($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S");
                ContentStream.AppendOpLine("[] 0 d");
                break;

            case "dotted":
                ContentStream.AppendOpLine($"{F(lineWidth)} w");
                ContentStream.AppendOpLine($"{F(r)} {F(g)} {F(b)} RG");
                ContentStream.AppendOpLine($"[0 {F(lineWidth * 2.5f)}] 0 d 1 J");
                ContentStream.AppendOpLine($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S");
                ContentStream.AppendOpLine("[] 0 d 0 J");
                break;

            case "double":
            {
                float third = Math.Max(lineWidth / 3f, 0.3f);
                ContentStream.AppendOpLine($"{F(third)} w");
                ContentStream.AppendOpLine($"{F(r)} {F(g)} {F(b)} RG");
                float off = lineWidth * 0.67f;
                ContentStream.AppendOpLine($"{F(x1)} {F(y1 + off)} m {F(x2)} {F(y2 + off)} l S");
                ContentStream.AppendOpLine($"{F(x1)} {F(y1 - off)} m {F(x2)} {F(y2 - off)} l S");
                break;
            }

            case "wavy":
            {
                // Approximate wavy line with a cubic Bezier sine curve.
                // Wave period = 4 × lineWidth; amplitude = lineWidth.
                float amplitude = Math.Max(lineWidth * 1.5f, 0.5f);
                float period = Math.Max(amplitude * 4f, 2f);
                ContentStream.AppendOpLine($"{F(lineWidth)} w");
                ContentStream.AppendOpLine($"{F(r)} {F(g)} {F(b)} RG");
                ContentStream.AppendOpLine($"{F(x1)} {F(y1)} m");
                float x = x1;
                bool up = true;
                while (x < x2)
                {
                    float segEnd = Math.Min(x + period, x2);
                    float halfPeriod = (segEnd - x) / 2f;
                    float cy = up ? y1 + amplitude : y1 - amplitude;
                    // Two control points at 1/4 and 3/4 of segment
                    float cp1x = x + halfPeriod * 0.5f;
                    float cp2x = x + halfPeriod * 1.5f;
                    float midX = x + halfPeriod;
                    ContentStream.AppendOpLine(
                        $"{F(cp1x)} {F(cy)} {F(cp2x)} {F(cy)} {F(midX)} {F(y1)} c");
                    x = midX;
                    up = !up;
                    if (segEnd >= x2) break;
                    // Second half of wave
                    cy = up ? y1 + amplitude : y1 - amplitude;
                    float segEnd2 = Math.Min(x + halfPeriod, x2);
                    float cp3x = x + (segEnd2 - x) * 0.5f;
                    float cp4x = x + (segEnd2 - x) * 1.5f;
                    ContentStream.AppendOpLine(
                        $"{F(cp3x)} {F(cy)} {F(cp4x)} {F(cy)} {F(segEnd2)} {F(y1)} c");
                    x = segEnd2;
                    up = !up;
                    if (x >= x2) break;
                }
                ContentStream.AppendOpLine("S");
                break;
            }

            default: // solid
                AddLine(x1, y1, x2, y2, r, g, b, lineWidth);
                break;
        }
    }

    /// <summary>Add a clickable link annotation.</summary>
    public void AddLink(float x, float y, float width, float height, string url)
    {
        Links.Add(new PdfLinkAnnotation(x, y, width, height, url));
    }

    private static string F(float value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string EscapePdfString(string text)
    {
        var sb = new StringBuilder(text.Length);
        AppendEscapedPdfString(sb, text);
        return sb.ToString();
    }

    /// <summary>
    /// Escape a PDF literal string directly into a builder — the hot text
    /// path appends into the content stream without a temporary
    /// StringBuilder + string per run. Plain-ASCII runs append in one call.
    /// </summary>
    private static void AppendEscapedPdfString(StringBuilder sb, string text)
    {
        bool simple = true;
        for (int i = 0; i < text.Length; i++)
        {
            char sc = text[i];
            if (sc >= 128 || sc == '\\' || sc == '(' || sc == ')') { simple = false; break; }
        }
        if (simple)
        {
            sb.Append(text);
            return;
        }

        foreach (char c in text)
        {
            if (c == '\\') sb.Append("\\\\");
            else if (c == '(') sb.Append("\\(");
            else if (c == ')') sb.Append("\\)");
            else if (c < 128)
            {
                sb.Append(c);
            }
            else
            {
                // Map Unicode to WinAnsiEncoding byte
                byte b = MapToWinAnsi(c);
                if (b >= 32)
                    sb.Append((char)b);
                else
                    sb.Append('?'); // unmappable
            }
        }
    }

    /// <summary>Map common Unicode characters to WinAnsiEncoding byte values.</summary>
    private static byte MapToWinAnsi(char c)
    {
        // Characters that map directly (Latin-1 Supplement, 0x80-0xFF)
        if (c >= 0xA0 && c <= 0xFF)
            return (byte)c;

        // Common special characters in WinAnsiEncoding
        switch (c)
        {
            case '\u2022': return 0x95; // bullet •
            case '\u2013': return 0x96; // en-dash –
            case '\u2014': return 0x97; // em-dash —
            case '\u2018': return 0x91; // left single quote '
            case '\u2019': return 0x92; // right single quote '
            case '\u201C': return 0x93; // left double quote "
            case '\u201D': return 0x94; // right double quote "
            case '\u2026': return 0x85; // ellipsis …
            case '\u2020': return 0x86; // dagger †
            case '\u2021': return 0x87; // double dagger ‡
            case '\u2030': return 0x89; // per mille ‰
            case '\u2039': return 0x8B; // single left angle ‹
            case '\u203A': return 0x9B; // single right angle ›
            case '\u0152': return 0x8C; // OE ligature Œ
            case '\u0153': return 0x9C; // oe ligature œ
            case '\u0160': return 0x8A; // S caron Š
            case '\u0161': return 0x9A; // s caron š
            case '\u0178': return 0x9F; // Y diaeresis Ÿ
            case '\u0192': return 0x83; // f hook ƒ
            case '\u02C6': return 0x88; // circumflex ˆ
            case '\u02DC': return 0x98; // tilde ˜
            case '\u2122': return 0x99; // trademark ™
            default: return (byte)'?'; // unmappable
        }
    }
}

internal class PdfLinkAnnotation
{
    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }
    public string Url { get; }

    public PdfLinkAnnotation(float x, float y, float width, float height, string url)
    {
        X = x; Y = y; Width = width; Height = height; Url = url;
    }
}
