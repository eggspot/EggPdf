using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EggPdf.Core;
using EggPdf.Pdf;

namespace EggPdf.Svg;

public static partial class SvgRenderer
{
    private const float CssPixelToPoint = 0.75f;
    private const int MaxUseDepth = 16;
    private const int MaxUseClonedElements = 100_000;

    [ThreadStatic]
    private static List<string>? _usedFonts;

    [ThreadStatic]
    private static List<string>? _usedExtGStates;

    [ThreadStatic]
    private static Dictionary<string, string>? _gradientFallbackColors;

    [ThreadStatic]
    private static Dictionary<string, SvgElement>? _elementsById;

    [ThreadStatic]
    private static int _useDepth;

    [ThreadStatic]
    private static int _useClonedElements;

    [ThreadStatic]
    private static float _inheritedOpacity;

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

    private static Color? ResolvePaintColor(string paint, Color? whenUnset)
    {
        paint = paint.Trim();
        if (paint.Length == 0) return whenUnset;
        if (paint.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        if (paint.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            return Color.TryParse(GradientFallbackColor(paint)) ?? Color.Black;
        return Color.TryParse(paint) ?? whenUnset;
    }

    private static string GradientFallbackColor(string paintServerReference)
    {
        int hash = paintServerReference.IndexOf('#');
        int close = paintServerReference.IndexOf(')');
        if (hash < 0 || close < hash) return "black";
        var id = paintServerReference.Substring(hash + 1, close - hash - 1).Trim('\'', '"', ' ');
        return _gradientFallbackColors != null && _gradientFallbackColors.TryGetValue(id, out var color) ? color : "black";
    }

    private static float ReadOpacity(SvgElement el, string property)
    {
        var value = el.GetAttribute(property).Trim();
        if (value.Length == 0) return 1f;
        bool percent = value.EndsWith("%", StringComparison.Ordinal);
        if (percent) value = value.Substring(0, value.Length - 1);
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float opacity) || float.IsNaN(opacity))
            return 1f;
        if (percent) opacity /= 100f;
        return Math.Max(0f, Math.Min(1f, opacity));
    }

    private static bool IsInvisible(SvgElement el)
    {
        var visibility = el.GetAttribute("visibility");
        return visibility == "hidden" || visibility == "collapse";
    }

    private static bool ApplyShapePaint(SvgElement el, StringBuilder sb, bool canFill, out bool fill, out bool stroke)
    {
        var fillColor = canFill ? ResolvePaintColor(el.GetAttribute("fill"), Color.Black) : null;
        var strokeColor = ResolvePaintColor(el.GetAttribute("stroke"), null);
        float fillAlpha = fillColor.HasValue ? fillColor.Value.A / 255f * ReadOpacity(el, "fill-opacity") * _inheritedOpacity : 1f;
        float strokeAlpha = strokeColor.HasValue ? strokeColor.Value.A / 255f * ReadOpacity(el, "stroke-opacity") * _inheritedOpacity : 1f;
        if (fillAlpha <= 0f) fillColor = null;
        if (strokeAlpha <= 0f || StrokeWidth(el) <= 0f) strokeColor = null;
        fill = fillColor.HasValue;
        stroke = strokeColor.HasValue;
        if (!fill && !stroke) return false;

        ApplyAlpha(sb, fill ? fillAlpha : 1f, stroke ? strokeAlpha : 1f);
        if (fillColor is { } f)
            sb.AppendOpLine($"{F(f.R / 255f)} {F(f.G / 255f)} {F(f.B / 255f)} rg");
        if (strokeColor is { } s)
        {
            sb.AppendOpLine($"{F(s.R / 255f)} {F(s.G / 255f)} {F(s.B / 255f)} RG");
            ApplyStrokeStyle(el, sb);
        }
        return true;
    }

    private static bool ApplyTextPaint(SvgElement el, StringBuilder sb)
    {
        var fillColor = ResolvePaintColor(el.GetAttribute("fill"), Color.Black);
        if (fillColor is not { } f) return false;
        float alpha = f.A / 255f * ReadOpacity(el, "fill-opacity") * _inheritedOpacity;
        if (alpha <= 0f) return false;
        ApplyAlpha(sb, alpha, 1f);
        sb.AppendOpLine($"{F(f.R / 255f)} {F(f.G / 255f)} {F(f.B / 255f)} rg");
        return true;
    }

    private static void ApplyAlpha(StringBuilder sb, float fillAlpha, float strokeAlpha)
    {
        if (fillAlpha >= 1f && strokeAlpha >= 1f) return;
        var stateName = PdfPage.SeparateOpacityStateName(fillAlpha, strokeAlpha);
        if (_usedExtGStates != null && !_usedExtGStates.Contains(stateName)) _usedExtGStates.Add(stateName);
        sb.AppendOpLine($"/{stateName} gs");
    }

    private static float StrokeWidth(SvgElement el) => GetFloat(el, "stroke-width", 1f);

    private static void ApplyStrokeStyle(SvgElement el, StringBuilder sb)
    {
        float width = StrokeWidth(el);
        if (width != 1f) sb.AppendOpLine($"{F(width)} w");

        switch (el.GetAttribute("stroke-linecap"))
        {
            case "round": sb.AppendOpLine("1 J"); break;
            case "square": sb.AppendOpLine("2 J"); break;
        }
        switch (el.GetAttribute("stroke-linejoin"))
        {
            case "round": sb.AppendOpLine("1 j"); break;
            case "bevel": sb.AppendOpLine("2 j"); break;
        }
        float miterLimit = GetFloat(el, "stroke-miterlimit", 4f);
        if (miterLimit >= 1f) sb.AppendOpLine($"{F(miterLimit)} M");

        var dashes = ParseDashArray(el.GetAttribute("stroke-dasharray"));
        if (dashes != null)
        {
            var pattern = new StringBuilder("[");
            for (int i = 0; i < dashes.Count; i++)
            {
                if (i > 0) pattern.Append(' ');
                pattern.Append(F(dashes[i]));
            }
            pattern.Append("] ").Append(F(GetFloat(el, "stroke-dashoffset"))).Append(" d");
            sb.AppendOpLine(pattern.ToString());
        }
    }

    private static List<float>? ParseDashArray(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Trim() == "none") return null;
        var parts = value.Split(new[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var dashes = new List<float>(parts.Length * 2);
        float total = 0f;
        foreach (var part in parts)
        {
            var number = part.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? part.Substring(0, part.Length - 2) : part;
            if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out float dash) || dash < 0f || float.IsNaN(dash))
                return null;
            dashes.Add(dash);
            total += dash;
        }
        if (dashes.Count == 0 || total <= 0f) return null;
        if (dashes.Count % 2 == 1) dashes.AddRange(dashes.ToArray());
        return dashes;
    }

    private static void EmitPaintOperator(SvgElement el, StringBuilder sb, bool fill, bool stroke)
    {
        bool evenOdd = el.GetAttribute("fill-rule") == "evenodd";
        if (fill && stroke) sb.AppendOpLine(evenOdd ? "B*" : "B");
        else if (fill) sb.AppendOpLine(evenOdd ? "f*" : "f");
        else if (stroke) sb.AppendOpLine("S");
        else sb.AppendOpLine("n");
    }

    private static void RenderUse(SvgElement use, StringBuilder sb, PdfDocument? pdfDoc,
        SvgDefs defs, List<string> usedImages, Matrix2D matrix)
    {
        var href = use.GetAttribute("href");
        if (string.IsNullOrEmpty(href)) href = use.GetAttribute("xlink:href");
        if (string.IsNullOrEmpty(href) || href[0] != '#' || _elementsById == null ||
            !_elementsById.TryGetValue(href.Substring(1), out var referenced) || _useDepth >= MaxUseDepth)
            return;

        var instance = CloneForUse(referenced, use);
        if (instance == null) return;
        if (instance.TagName == "symbol") instance.TagName = "g";

        float x = GetFloat(use, "x");
        float y = GetFloat(use, "y");
        _useDepth++;
        try
        {
            sb.AppendOpLine("q");
            sb.AppendOpLine($"1 0 0 1 {F(x)} {F(y)} cm");
            RenderElement(instance, sb, pdfDoc, defs, usedImages, MatrixMultiply(new Matrix2D(1, 0, 0, 1, x, y), matrix));
            sb.AppendOpLine("Q");
        }
        finally
        {
            _useDepth--;
        }
    }

    private static SvgElement? CloneForUse(SvgElement source, SvgElement parent)
    {
        if (++_useClonedElements > MaxUseClonedElements) return null;
        var clone = new SvgElement { TagName = source.TagName, TextContent = source.TextContent };
        foreach (var attribute in source.Attributes)
        {
            if (source.InheritedPropertyNames == null || !source.InheritedPropertyNames.Contains(attribute.Key))
                clone.Attributes[attribute.Key] = attribute.Value;
        }
        SvgParser.InheritFrom(clone, parent);
        foreach (var child in source.Children)
        {
            var childClone = CloneForUse(child, clone);
            if (childClone == null) return null;
            clone.Children.Add(childClone);
        }
        return clone;
    }
}
