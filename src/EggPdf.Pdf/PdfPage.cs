using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EggPdf.Pdf;

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
        ContentStream.AppendLine($"{F(colorR)} {F(colorG)} {F(colorB)} rg");
        ContentStream.Append($"BT /{fontName} {F(fontSize)} Tf ");
        ContentStream.Append($"{F(letterSpacing)} Tc ");
        ContentStream.Append($"{F(wordSpacing)} Tw ");
        ContentStream.Append($"{F(x)} {F(y)} Td ");
        ContentStream.Append($"({EscapePdfString(text)}) Tj ");
        ContentStream.AppendLine("ET");
    }

    /// <summary>Add text using CIDFont glyph IDs (for embedded TrueType fonts with full Unicode).</summary>
    public void AddTextCID(ushort[] glyphIds, float x, float y, string fontName, float fontSize,
        float colorR = 0, float colorG = 0, float colorB = 0,
        float letterSpacing = 0, float wordSpacing = 0)
    {
        UsedFonts.Add(fontName);
        ContentStream.AppendLine($"{F(colorR)} {F(colorG)} {F(colorB)} rg");
        ContentStream.Append($"BT /{fontName} {F(fontSize)} Tf ");
        ContentStream.Append($"{F(letterSpacing)} Tc ");
        ContentStream.Append($"{F(wordSpacing)} Tw ");
        ContentStream.Append($"{F(x)} {F(y)} Td ");
        // Encode glyph IDs as hex string: <001A002B003C>
        ContentStream.Append('<');
        foreach (var gid in glyphIds)
            ContentStream.Append(gid.ToString("X4"));
        ContentStream.Append("> Tj ");
        ContentStream.AppendLine("ET");
    }

    /// <summary>Append raw PDF content stream commands (for SVG rendering etc.).</summary>
    public void AppendRawContent(string commands)
    {
        ContentStream.Append(commands);
    }

    /// <summary>Add a filled rectangle.</summary>
    public void AddRectangle(float x, float y, float width, float height, float r, float g, float b)
    {
        ContentStream.AppendLine($"{F(r)} {F(g)} {F(b)} rg");
        ContentStream.AppendLine($"{F(x)} {F(y)} {F(width)} {F(height)} re f");
    }

    /// <summary>Add a stroked rectangle (border).</summary>
    public void AddStrokeRectangle(float x, float y, float width, float height, float r, float g, float b, float lineWidth)
    {
        ContentStream.AppendLine($"{F(lineWidth)} w");
        ContentStream.AppendLine($"{F(r)} {F(g)} {F(b)} RG");
        ContentStream.AppendLine($"{F(x)} {F(y)} {F(width)} {F(height)} re S");
    }

    /// <summary>Set dash pattern for subsequent strokes. Empty array = solid.</summary>
    public void SetDashPattern(float[] dashArray, float dashPhase = 0)
    {
        if (dashArray == null || dashArray.Length == 0)
        {
            ContentStream.AppendLine("[] 0 d");
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
        ContentStream.AppendLine(sb.ToString());
    }

    /// <summary>Set line cap style: 0=butt, 1=round, 2=square.</summary>
    public void SetLineCap(int cap)
    {
        ContentStream.AppendLine($"{cap} J");
    }

    /// <summary>Add a stroked line between two points (border side).</summary>
    public void AddBorderLine(float x1, float y1, float x2, float y2,
        float r, float g, float b, float lineWidth, string borderStyle)
    {
        ContentStream.AppendLine($"{F(lineWidth)} w");
        ContentStream.AppendLine($"{F(r)} {F(g)} {F(b)} RG");

        switch (borderStyle)
        {
            case "dashed":
                // Dash pattern: 3x line width on, 3x off
                float dashLen = lineWidth * 3;
                ContentStream.AppendLine($"[{F(dashLen)} {F(dashLen)}] 0 d");
                ContentStream.AppendLine("0 J"); // butt cap
                ContentStream.AppendLine($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S");
                ContentStream.AppendLine("[] 0 d"); // reset dash
                break;

            case "dotted":
                // Dotted: round cap with 0-length dash
                ContentStream.AppendLine($"[0 {F(lineWidth * 2)}] 0 d");
                ContentStream.AppendLine("1 J"); // round cap
                ContentStream.AppendLine($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S");
                ContentStream.AppendLine("[] 0 d"); // reset dash
                ContentStream.AppendLine("0 J"); // reset cap
                break;

            case "double":
            {
                // Double: two lines at 1/3 width with 1/3 gap
                float third = lineWidth / 3;
                if (third < 0.5f) third = 0.5f;
                ContentStream.AppendLine($"{F(third)} w");

                // Calculate perpendicular offset for the two lines
                float dx = x2 - x1, dy = y2 - y1;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len < 0.001f) break;
                float nx = -dy / len * third, ny = dx / len * third;

                // First line (offset outward)
                ContentStream.AppendLine($"{F(x1 + nx)} {F(y1 + ny)} m {F(x2 + nx)} {F(y2 + ny)} l S");
                // Second line (offset inward)
                ContentStream.AppendLine($"{F(x1 - nx)} {F(y1 - ny)} m {F(x2 - nx)} {F(y2 - ny)} l S");
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

                ContentStream.AppendLine($"{F(half)} w");
                ContentStream.AppendLine($"{F(r1)} {F(g1)} {F(b1)} RG");
                ContentStream.AppendLine($"{F(x1 + nx)} {F(y1 + ny)} m {F(x2 + nx)} {F(y2 + ny)} l S");
                ContentStream.AppendLine($"{F(r2)} {F(g2)} {F(b2)} RG");
                ContentStream.AppendLine($"{F(x1 - nx)} {F(y1 - ny)} m {F(x2 - nx)} {F(y2 - ny)} l S");
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
                ContentStream.AppendLine($"{F(adjR)} {F(adjG)} {F(adjB)} RG");
                ContentStream.AppendLine($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S");
                break;
            }

            default: // solid
                ContentStream.AppendLine($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S");
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

        ContentStream.AppendLine($"{F(r)} {F(g)} {F(b)} rg");
        AppendRoundedRectPath(x, y, w, h, tlr, trr, brr, blr);
        ContentStream.AppendLine("h f");
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

        ContentStream.AppendLine($"{F(lineWidth)} w");
        ContentStream.AppendLine($"{F(r)} {F(g)} {F(b)} RG");
        AppendRoundedRectPath(x, y, w, h, tlr, trr, brr, blr);
        ContentStream.AppendLine("h S");
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
        ContentStream.AppendLine($"{F(x + tlr)} {F(y + h)} m");

        // Top edge line to start of TR radius
        ContentStream.AppendLine($"{F(x + w - trr)} {F(y + h)} l");

        // TR corner curve
        if (trr > 0)
            ContentStream.AppendLine($"{F(x + w - trr + trr * k)} {F(y + h)} {F(x + w)} {F(y + h - trr + trr * k)} {F(x + w)} {F(y + h - trr)} c");
        else
            ContentStream.AppendLine($"{F(x + w)} {F(y + h)} l");

        // Right edge line to start of BR radius
        ContentStream.AppendLine($"{F(x + w)} {F(y + brr)} l");

        // BR corner curve
        if (brr > 0)
            ContentStream.AppendLine($"{F(x + w)} {F(y + brr - brr * k)} {F(x + w - brr + brr * k)} {F(y)} {F(x + w - brr)} {F(y)} c");
        else
            ContentStream.AppendLine($"{F(x + w)} {F(y)} l");

        // Bottom edge line to start of BL radius
        ContentStream.AppendLine($"{F(x + blr)} {F(y)} l");

        // BL corner curve
        if (blr > 0)
            ContentStream.AppendLine($"{F(x + blr - blr * k)} {F(y)} {F(x)} {F(y + blr - blr * k)} {F(x)} {F(y + blr)} c");
        else
            ContentStream.AppendLine($"{F(x)} {F(y)} l");

        // Left edge line to start of TL radius
        ContentStream.AppendLine($"{F(x)} {F(y + h - tlr)} l");

        // TL corner curve
        if (tlr > 0)
            ContentStream.AppendLine($"{F(x)} {F(y + h - tlr + tlr * k)} {F(x + tlr - tlr * k)} {F(y + h)} {F(x + tlr)} {F(y + h)} c");
        else
            ContentStream.AppendLine($"{F(x)} {F(y + h)} l");
    }

    /// <summary>Set fill and stroke opacity (0.0-1.0). Creates an ExtGState reference.</summary>
    public void SetOpacity(float opacity)
    {
        if (opacity >= 1.0f) return; // fully opaque, no ExtGState needed
        var gsName = $"GS{(int)(opacity * 100)}";
        UsedExtGStates.Add(gsName);
        ContentStream.AppendLine($"/{gsName} gs");
    }

    /// <summary>Save graphics state.</summary>
    public void SaveState()
    {
        ContentStream.AppendLine("q");
    }

    /// <summary>Restore graphics state.</summary>
    public void RestoreState()
    {
        ContentStream.AppendLine("Q");
    }

    /// <summary>Add a clipping rectangle (clips all subsequent drawing).</summary>
    public void AddClipRect(float x, float y, float width, float height)
    {
        ContentStream.AppendLine($"{F(x)} {F(y)} {F(width)} {F(height)} re W n");
    }

    /// <summary>Concatenate a transformation matrix (PDF cm operator).</summary>
    public void ConcatMatrix(float a, float b, float c, float d, float e, float f)
    {
        ContentStream.AppendLine($"{F(a)} {F(b)} {F(c)} {F(d)} {F(e)} {F(f)} cm");
    }

    /// <summary>Add an image at a position with specified dimensions (PDF coordinates).</summary>
    public void AddImage(string imageName, float x, float y, float width, float height)
    {
        if (!UsedImages.Contains(imageName))
            UsedImages.Add(imageName);

        // Save graphics state, apply transformation matrix, draw image, restore
        ContentStream.AppendLine("q");
        ContentStream.AppendLine($"{F(width)} 0 0 {F(height)} {F(x)} {F(y)} cm");
        ContentStream.AppendLine($"/{imageName} Do");
        ContentStream.AppendLine("Q");
    }

    /// <summary>Add a stroked line between two points.</summary>
    public void AddLine(float x1, float y1, float x2, float y2, float r, float g, float b, float lineWidth)
    {
        ContentStream.AppendLine($"{F(lineWidth)} w");
        ContentStream.AppendLine($"{F(r)} {F(g)} {F(b)} RG");
        ContentStream.AppendLine($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S");
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
        return sb.ToString();
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
