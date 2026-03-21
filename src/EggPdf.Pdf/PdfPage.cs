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
    public void AddText(string text, float x, float y, string fontName, float fontSize)
    {
        UsedFonts.Add(fontName);
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
        return text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
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
