using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EggPdf.Pdf;

namespace EggPdf.Svg;

public static partial class SvgRenderer
{
    private const float CssPixelToPoint = 0.75f;
    private const int MaxUseDepth = 16;

    [ThreadStatic]
    private static List<string>? _usedFonts;

    [ThreadStatic]
    private static Dictionary<string, string>? _gradientFallbackColors;

    [ThreadStatic]
    private static Dictionary<string, SvgElement>? _elementsById;

    [ThreadStatic]
    private static int _useDepth;

    private static (float ScaleX, float ScaleY, float OffsetX, float OffsetY) ViewportTransform(
        SvgElement svg, float targetWidth, float targetHeight)
    {
        if (!TryReadViewBox(svg, out float viewBoxX, out float viewBoxY, out float viewBoxWidth, out float viewBoxHeight))
            return (CssPixelToPoint, CssPixelToPoint, 0f, 0f);

        float scaleX = targetWidth / viewBoxWidth;
        float scaleY = targetHeight / viewBoxHeight;
        var preserve = svg.GetAttribute("preserveaspectratio").Trim();
        if (preserve.StartsWith("none", StringComparison.OrdinalIgnoreCase))
            return (scaleX, scaleY, -viewBoxX * scaleX, -viewBoxY * scaleY);

        bool slice = preserve.IndexOf("slice", StringComparison.OrdinalIgnoreCase) >= 0;
        float scale = slice ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
        float alignX = AlignmentFraction(preserve, "xMin", "xMax");
        float alignY = AlignmentFraction(preserve, "YMin", "YMax");
        float offsetX = (targetWidth - viewBoxWidth * scale) * alignX - viewBoxX * scale;
        float offsetY = (targetHeight - viewBoxHeight * scale) * alignY - viewBoxY * scale;
        return (scale, scale, offsetX, offsetY);
    }

    private static float AlignmentFraction(string preserveAspectRatio, string minToken, string maxToken)
    {
        if (preserveAspectRatio.IndexOf(minToken, StringComparison.OrdinalIgnoreCase) >= 0) return 0f;
        if (preserveAspectRatio.IndexOf(maxToken, StringComparison.OrdinalIgnoreCase) >= 0) return 1f;
        return 0.5f;
    }

    private static bool TryReadViewBox(SvgElement svg, out float x, out float y, out float width, out float height)
    {
        x = y = width = height = 0f;
        var viewBox = svg.GetAttribute("viewbox");
        if (string.IsNullOrEmpty(viewBox)) return false;
        var parts = viewBox.Split(new[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4 &&
               float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
               float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) &&
               float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out width) &&
               float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out height) &&
               width > 0f && height > 0f;
    }

    private static Dictionary<string, string> CollectGradientFallbackColors(SvgElement root)
    {
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        CollectGradientFallbackColors(root, colors);
        return colors;
    }

    private static void CollectGradientFallbackColors(SvgElement element, Dictionary<string, string> colors)
    {
        if ((element.TagName == "lineargradient" || element.TagName == "radialgradient") &&
            element.Attributes.TryGetValue("id", out var id) && !string.IsNullOrEmpty(id))
        {
            foreach (var stop in element.Children)
            {
                if (stop.TagName != "stop") continue;
                var color = stop.GetAttribute("stop-color");
                colors[id] = string.IsNullOrEmpty(color) ? "black" : color;
                break;
            }
        }
        foreach (var child in element.Children)
            CollectGradientFallbackColors(child, colors);
    }

    private static Dictionary<string, SvgElement> CollectElementsById(SvgElement root)
    {
        var byId = new Dictionary<string, SvgElement>(StringComparer.Ordinal);
        var pending = new Stack<SvgElement>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var element = pending.Pop();
            if (element.Attributes.TryGetValue("id", out var id) && !string.IsNullOrEmpty(id) && !byId.ContainsKey(id))
                byId[id] = element;
            foreach (var child in element.Children)
                pending.Push(child);
        }
        return byId;
    }

    private static string ResolvePaint(string paint)
    {
        if (!paint.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) return paint;
        int hash = paint.IndexOf('#');
        int close = paint.IndexOf(')');
        if (hash < 0 || close < hash) return "black";
        var id = paint.Substring(hash + 1, close - hash - 1).Trim('\'', '"', ' ');
        return _gradientFallbackColors != null && _gradientFallbackColors.TryGetValue(id, out var color) ? color : "black";
    }

    private static void RenderUse(SvgElement use, StringBuilder sb, PdfDocument? pdfDoc,
        SvgDefs defs, List<string> usedImages, Matrix2D matrix)
    {
        var href = use.GetAttribute("href");
        if (string.IsNullOrEmpty(href)) href = use.GetAttribute("xlink:href");
        if (string.IsNullOrEmpty(href) || href[0] != '#' || _elementsById == null ||
            !_elementsById.TryGetValue(href.Substring(1), out var referenced) || _useDepth >= MaxUseDepth)
            return;

        float x = GetFloat(use, "x");
        float y = GetFloat(use, "y");
        var instance = new SvgElement
        {
            TagName = referenced.TagName,
            Attributes = new Dictionary<string, string>(referenced.Attributes),
            Children = referenced.Children,
            TextContent = referenced.TextContent,
        };
        foreach (var attribute in use.Attributes)
        {
            if (attribute.Key == "x" || attribute.Key == "y" || attribute.Key == "href" || attribute.Key == "xlink:href" ||
                attribute.Key == "transform" || attribute.Key == "id")
                continue;
            if (!instance.Attributes.ContainsKey(attribute.Key))
                instance.Attributes[attribute.Key] = attribute.Value;
        }

        _useDepth++;
        try
        {
            sb.AppendLine("q");
            sb.AppendLine($"1 0 0 1 {F(x)} {F(y)} cm");
            RenderElement(instance, sb, pdfDoc, defs, usedImages, MatrixMultiply(new Matrix2D(1, 0, 0, 1, x, y), matrix));
            sb.AppendLine("Q");
        }
        finally
        {
            _useDepth--;
        }
    }

    private static string StandardFontFor(SvgElement text)
    {
        var family = text.GetAttribute("font-family").ToLowerInvariant();
        var weight = text.GetAttribute("font-weight");
        bool bold = weight == "bold" || weight == "bolder" || (int.TryParse(weight, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) && w >= 600);
        var style = text.GetAttribute("font-style");
        bool italic = style == "italic" || style == "oblique";

        bool monospace = family.IndexOf("mono", StringComparison.Ordinal) >= 0 || family.IndexOf("courier", StringComparison.Ordinal) >= 0;
        bool serif = (family.IndexOf("serif", StringComparison.Ordinal) >= 0 && family.IndexOf("sans", StringComparison.Ordinal) < 0)
                     || family.IndexOf("times", StringComparison.Ordinal) >= 0;
        string baseName = monospace ? "Courier" : serif ? "Times" : "Helvetica";

        string font = baseName switch
        {
            "Times" => bold && italic ? "Times-BoldItalic" : bold ? "Times-Bold" : italic ? "Times-Italic" : "Times-Roman",
            "Courier" => bold && italic ? "Courier-BoldOblique" : bold ? "Courier-Bold" : italic ? "Courier-Oblique" : "Courier",
            _ => bold && italic ? "Helvetica-BoldOblique" : bold ? "Helvetica-Bold" : italic ? "Helvetica-Oblique" : "Helvetica",
        };
        if (_usedFonts != null && !_usedFonts.Contains(font)) _usedFonts.Add(font);
        return font;
    }
}
