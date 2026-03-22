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

    internal PdfPage(float widthPt, float heightPt)
    {
        WidthPt = widthPt;
        HeightPt = heightPt;
    }

    /// <summary>Add text at a position (PDF coordinates, bottom-left origin).</summary>
    public void AddText(string text, float x, float y, string fontName, float fontSize,
        float colorR = 0, float colorG = 0, float colorB = 0)
    {
        UsedFonts.Add(fontName);
        ContentStream.AppendLine($"{F(colorR)} {F(colorG)} {F(colorB)} rg");
        ContentStream.Append("BT ");
        ContentStream.Append($"/{fontName} {F(fontSize)} Tf ");
        ContentStream.Append($"{F(x)} {F(y)} Td ");
        ContentStream.Append($"({EscapePdfString(text)}) Tj ");
        ContentStream.AppendLine("ET");
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
