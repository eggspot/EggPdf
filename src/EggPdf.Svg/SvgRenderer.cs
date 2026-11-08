using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EggPdf.Pdf;

namespace EggPdf.Svg;

/// <summary>
/// Renders inline SVG elements to PDF drawing commands.
/// Converts SVG shapes (rect, circle, ellipse, line, polyline, polygon, path)
/// to PDF path operators, with support for fill, stroke, transforms, and viewBox.
/// Filter effects (filter="url(#id)") are rasterized in SvgRenderer.Filter.cs.
/// </summary>
public static partial class SvgRenderer
{
    /// <summary>
    /// Render an SVG element tree to PDF content stream commands. Returns the content
    /// stream fragment to insert into the page, plus the names of any images it
    /// references (rasterized filter layers -- see <see cref="RenderFilteredElement"/>) so the
    /// caller can register them on the page's resource dictionary without breaking the
    /// document-order z-ordering a direct page.AddImage call mid-render would cause.
    /// </summary>
    public static (string commands, List<string> usedImages) Render(SvgElement svg, float targetX, float targetY,
        float targetWidth, float targetHeight, PdfDocument? pdfDoc = null)
    {
        var result = RenderWithResources(svg, targetX, targetY, targetWidth, targetHeight, pdfDoc);
        return (result.Commands, result.UsedImages);
    }

    /// <summary>
    /// Render an SVG element tree into the target box (PDF points), returning the content stream
    /// fragment plus every image, standard font and graphics state it uses, which the caller registers
    /// on the page. <paramref name="measureTextWidth"/> (text, font size, standard font name) returns a
    /// run's advance width in user units; text anchoring and following <c>tspan</c> runs use it.
    /// </summary>
    public static SvgRenderResult RenderWithResources(SvgElement svg, float targetX, float targetY,
        float targetWidth, float targetHeight, PdfDocument? pdfDoc = null,
        Func<string, float, string, float>? measureTextWidth = null)
    {
        var sb = new StringBuilder();
        var usedImages = new List<string>();
        _usedFonts = new List<string>();
        _usedExtGStates = new List<string>();
        _measureTextWidth = measureTextWidth;
        _inheritedOpacity = 1f;
        _useClonedElements = 0;
        _gradientFallbackColors = CollectGradientFallbackColors(svg);
        _elementsById = CollectElementsById(svg);

        sb.AppendOpLine("q");
        sb.AppendOpLine($"{F(targetX)} {F(targetY)} {F(targetWidth)} {F(targetHeight)} re W n");

        var (scaleX, scaleY, offsetX, offsetY) = ViewportTransform(svg, targetWidth, targetHeight);
        float tx = targetX + offsetX;
        float ty = targetY + targetHeight - offsetY;
        var viewBoxMatrix = new Matrix2D(scaleX, 0, 0, -scaleY, tx, ty);
        AppendMatrix(sb, viewBoxMatrix);
        if (TryReadViewBox(svg, out _, out _, out float viewBoxWidth, out float viewBoxHeight))
        {
            _viewportWidth = viewBoxWidth;
            _viewportHeight = viewBoxHeight;
        }
        else
        {
            _viewportWidth = targetWidth / CssPixelToPoint;
            _viewportHeight = targetHeight / CssPixelToPoint;
        }

        var defs = CollectDefs(svg);
        RenderChildren(svg, sb, pdfDoc, defs, usedImages, viewBoxMatrix);

        sb.AppendOpLine("Q");

        var result = new SvgRenderResult(sb.ToString(), usedImages, _usedFonts, _usedExtGStates);
        _usedFonts = null;
        _usedExtGStates = null;
        _measureTextWidth = null;
        _gradientFallbackColors = null;
        _elementsById = null;
        return result;
    }

    private static void RenderChildren(SvgElement parent, StringBuilder sb, PdfDocument? pdfDoc,
        SvgDefs defs, List<string> usedImages, Matrix2D matrix)
    {
        foreach (var child in parent.Children)
        {
            RenderElement(child, sb, pdfDoc, defs, usedImages, matrix);
        }
    }

    private static void RenderElement(SvgElement el, StringBuilder sb, PdfDocument? pdfDoc,
        SvgDefs defs, List<string> usedImages, Matrix2D matrix)
    {
        if (el.GetAttribute("display").Trim() == "none" || IsNeverRendered(el.TagName)) return;

        bool hasTransform = el.Attributes.TryGetValue("transform", out var transformStr) && !string.IsNullOrEmpty(transformStr);
        if (hasTransform)
        {
            sb.AppendOpLine("q");
            ApplyTransform(sb, transformStr!);
            matrix = MatrixMultiply(ParseTransformToMatrix(transformStr!), matrix);
        }

        float parentOpacity = _inheritedOpacity;
        _inheritedOpacity = parentOpacity * ReadOpacity(el, "opacity");
        try
        {
            if (_inheritedOpacity <= 0f) return;
            if (defs.Filters.Count > 0 && pdfDoc != null)
            {
                var filter = ResolveFilter(el, defs.Filters);
                if (filter != null && RenderFilteredElement(el, sb, pdfDoc, usedImages, filter, defs, matrix))
                    return;
            }

            switch (el.TagName)
            {
                case "text":
                case "tspan":
                    if (!IsInvisible(el)) RenderText(el, sb);
                    break;
                case "use":
                    RenderUse(el, sb, pdfDoc, defs, usedImages, matrix);
                    break;
                default:
                    if (IsShape(el.TagName))
                    {
                        if (IsInvisible(el)) break;
                        sb.AppendOpLine("q");
                        RenderShape(el, sb);
                        sb.AppendOpLine("Q");
                    }
                    else
                    {
                        RenderChildren(el, sb, pdfDoc, defs, usedImages, matrix);
                    }
                    break;
            }
        }
        finally
        {
            _inheritedOpacity = parentOpacity;
            if (hasTransform) sb.AppendOpLine("Q");
        }
    }

    private static bool IsShape(string tagName) =>
        tagName == "rect" || tagName == "circle" || tagName == "ellipse" || tagName == "line" ||
        tagName == "polyline" || tagName == "polygon" || tagName == "path";

    private static bool IsNeverRendered(string tagName)
    {
        switch (tagName)
        {
            case "defs":
            case "clippath":
            case "mask":
            case "filter":
            case "symbol":
            case "marker":
            case "pattern":
            case "lineargradient":
            case "radialgradient":
            case "style":
            case "script":
            case "title":
            case "desc":
            case "metadata":
            case SvgParser.TextRunTagName:
                return true;
            default:
                return false;
        }
    }

    private static void RenderShape(SvgElement el, StringBuilder sb)
    {
        switch (el.TagName)
        {
            case "rect": RenderRect(el, sb); break;
            case "circle": RenderCircle(el, sb); break;
            case "ellipse": RenderEllipse(el, sb); break;
            case "line": RenderLine(el, sb); break;
            case "polyline": RenderPolyline(el, sb, false); break;
            case "polygon": RenderPolyline(el, sb, true); break;
            case "path": RenderPath(el, sb); break;
        }
    }

    private static void RenderRect(SvgElement el, StringBuilder sb)
    {
        float x = GetFloat(el, "x");
        float y = GetFloat(el, "y");
        float w = GetFloat(el, "width");
        float h = GetFloat(el, "height");
        float rx = GetFloat(el, "rx");
        float ry = GetFloat(el, "ry");
        if (ry == 0) ry = rx;
        if (rx == 0) rx = ry;
        rx = Math.Min(rx, w / 2f);
        ry = Math.Min(ry, h / 2f);
        if (w <= 0f || h <= 0f || !ApplyShapePaint(el, sb, canFill: true, out bool fill, out bool stroke)) return;

        if (rx > 0 || ry > 0)
        {
            float kx = rx * BezierCircleFactor, ky = ry * BezierCircleFactor;

            sb.Append($"{F(x + rx)} {F(y)} m ");
            sb.Append($"{F(x + w - rx)} {F(y)} l ");
            sb.Append($"{F(x + w - rx + kx)} {F(y)} {F(x + w)} {F(y + ry - ky)} {F(x + w)} {F(y + ry)} c ");
            sb.Append($"{F(x + w)} {F(y + h - ry)} l ");
            sb.Append($"{F(x + w)} {F(y + h - ry + ky)} {F(x + w - rx + kx)} {F(y + h)} {F(x + w - rx)} {F(y + h)} c ");
            sb.Append($"{F(x + rx)} {F(y + h)} l ");
            sb.Append($"{F(x + rx - kx)} {F(y + h)} {F(x)} {F(y + h - ry + ky)} {F(x)} {F(y + h - ry)} c ");
            sb.Append($"{F(x)} {F(y + ry)} l ");
            sb.AppendOpLine($"{F(x)} {F(y + ry - ky)} {F(x + rx - kx)} {F(y)} {F(x + rx)} {F(y)} c h");
        }
        else
        {
            sb.AppendOpLine($"{F(x)} {F(y)} {F(w)} {F(h)} re");
        }

        EmitPaintOperator(el, sb, fill, stroke);
    }

    private static void RenderCircle(SvgElement el, StringBuilder sb)
    {
        float cx = GetFloat(el, "cx");
        float cy = GetFloat(el, "cy");
        float r = GetFloat(el, "r");
        if (r <= 0f || !ApplyShapePaint(el, sb, canFill: true, out bool fill, out bool stroke)) return;

        DrawEllipse(sb, cx, cy, r, r);
        EmitPaintOperator(el, sb, fill, stroke);
    }

    private static void RenderEllipse(SvgElement el, StringBuilder sb)
    {
        float cx = GetFloat(el, "cx");
        float cy = GetFloat(el, "cy");
        float rx = GetFloat(el, "rx");
        float ry = GetFloat(el, "ry");
        if (rx <= 0f || ry <= 0f || !ApplyShapePaint(el, sb, canFill: true, out bool fill, out bool stroke)) return;

        DrawEllipse(sb, cx, cy, rx, ry);
        EmitPaintOperator(el, sb, fill, stroke);
    }

    private const float BezierCircleFactor = 0.5523f;

    private static void DrawEllipse(StringBuilder sb, float cx, float cy, float rx, float ry)
    {
        float kx = rx * BezierCircleFactor, ky = ry * BezierCircleFactor;

        sb.Append($"{F(cx + rx)} {F(cy)} m ");
        sb.Append($"{F(cx + rx)} {F(cy + ky)} {F(cx + kx)} {F(cy + ry)} {F(cx)} {F(cy + ry)} c ");
        sb.Append($"{F(cx - kx)} {F(cy + ry)} {F(cx - rx)} {F(cy + ky)} {F(cx - rx)} {F(cy)} c ");
        sb.Append($"{F(cx - rx)} {F(cy - ky)} {F(cx - kx)} {F(cy - ry)} {F(cx)} {F(cy - ry)} c ");
        sb.AppendOpLine($"{F(cx + kx)} {F(cy - ry)} {F(cx + rx)} {F(cy - ky)} {F(cx + rx)} {F(cy)} c h");
    }

    private static void RenderLine(SvgElement el, StringBuilder sb)
    {
        float x1 = GetFloat(el, "x1");
        float y1 = GetFloat(el, "y1");
        float x2 = GetFloat(el, "x2");
        float y2 = GetFloat(el, "y2");

        if (!ApplyShapePaint(el, sb, canFill: false, out _, out bool stroke) || !stroke) return;
        sb.AppendOpLine($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S");
    }

    private static void RenderPolyline(SvgElement el, StringBuilder sb, bool close)
    {
        var points = el.GetAttribute("points");
        if (string.IsNullOrEmpty(points)) return;

        var coords = ParsePointsList(points);
        if (coords.Count < 2 || !ApplyShapePaint(el, sb, canFill: true, out bool fill, out bool stroke)) return;

        sb.Append($"{F(coords[0].x)} {F(coords[0].y)} m ");
        for (int i = 1; i < coords.Count; i++)
            sb.Append($"{F(coords[i].x)} {F(coords[i].y)} l ");

        if (close) sb.Append("h ");
        EmitPaintOperator(el, sb, fill, stroke);
    }

    private static void RenderPath(SvgElement el, StringBuilder sb)
    {
        var segments = SvgPathData.Parse(el.GetAttribute("d"));
        if (segments.Count == 0 || !ApplyShapePaint(el, sb, canFill: true, out bool fill, out bool stroke)) return;

        AppendPathOperators(segments, sb);
        EmitPaintOperator(el, sb, fill, stroke);
    }

    private static void AppendPathOperators(List<PathSegment> segments, StringBuilder sb)
    {
        foreach (var segment in segments)
        {
            switch (segment.Kind)
            {
                case PathSegmentKind.MoveTo:
                    sb.Append(F(segment.X)).Append(' ').Append(F(segment.Y)).Append(" m ");
                    break;
                case PathSegmentKind.LineTo:
                    sb.Append(F(segment.X)).Append(' ').Append(F(segment.Y)).Append(" l ");
                    break;
                case PathSegmentKind.CubicTo:
                    sb.Append(F(segment.X1)).Append(' ').Append(F(segment.Y1)).Append(' ')
                      .Append(F(segment.X2)).Append(' ').Append(F(segment.Y2)).Append(' ')
                      .Append(F(segment.X)).Append(' ').Append(F(segment.Y)).Append(" c ");
                    break;
                case PathSegmentKind.Close:
                    sb.Append("h ");
                    break;
            }
        }
        sb.AppendOpLine();
    }

    private static void ApplyTransform(StringBuilder sb, string transform) =>
        AppendMatrix(sb, ParseTransformToMatrix(transform));

    private static void AppendMatrix(StringBuilder sb, Matrix2D m) =>
        sb.AppendOpLine($"{FMatrix(m.A)} {FMatrix(m.B)} {FMatrix(m.C)} {FMatrix(m.D)} {FMatrix(m.E)} {FMatrix(m.F)} cm");

    private static string FMatrix(float v) =>
        Math.Abs(v) < 5e-7f ? "0" : v.ToString("0.######", CultureInfo.InvariantCulture);

    private const float DefaultFontSizePx = 16f;

    [ThreadStatic]
    private static float _viewportWidth;

    [ThreadStatic]
    private static float _viewportHeight;

    private static float GetFloat(SvgElement el, string attr, float defaultValue = 0) =>
        ParseLength(el.GetAttribute(attr), el, attr, defaultValue);

    private static float ParseLength(string value, SvgElement el, string attr, float defaultValue)
    {
        value = value.Trim();
        if (value.Length == 0) return defaultValue;
        int i = 0;
        if (!SvgPathData.TryReadNumber(value, ref i, out float number)) return defaultValue;
        switch (value.Substring(i).Trim().ToLowerInvariant())
        {
            case "":
            case "px": return number;
            case "%": return number / 100f * PercentageBasis(attr);
            case "pt": return number * 96f / 72f;
            case "pc": return number * 16f;
            case "in": return number * 96f;
            case "cm": return number * 96f / 2.54f;
            case "mm": return number * 96f / 25.4f;
            case "q": return number * 96f / 101.6f;
            case "em": return number * FontSizeFor(el, attr);
            case "ex": return number * FontSizeFor(el, attr) / 2f;
            case "rem": return number * DefaultFontSizePx;
            default: return defaultValue;
        }
    }

    private static float FontSizeFor(SvgElement el, string attr) =>
        attr == "font-size" ? DefaultFontSizePx : GetFloat(el, "font-size", DefaultFontSizePx);

    private static float PercentageBasis(string attr)
    {
        switch (attr)
        {
            case "x": case "x1": case "x2": case "cx": case "width": case "rx": case "dx":
                return _viewportWidth;
            case "y": case "y1": case "y2": case "cy": case "height": case "ry": case "dy":
                return _viewportHeight;
            case "font-size":
                return DefaultFontSizePx;
            default:
                return (float)Math.Sqrt((_viewportWidth * _viewportWidth + _viewportHeight * _viewportHeight) / 2.0);
        }
    }

    private static List<(float x, float y)> ParsePointsList(string points)
    {
        var result = new List<(float x, float y)>();
        int i = 0;
        while (SvgPathData.TryReadNumber(points, ref i, out float x) && SvgPathData.TryReadNumber(points, ref i, out float y))
            result.Add((x, y));
        return result;
    }

    private static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
}
