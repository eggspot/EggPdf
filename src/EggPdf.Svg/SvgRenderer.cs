using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EggPdf.Svg;

/// <summary>
/// Renders inline SVG elements to PDF drawing commands.
/// Converts SVG shapes (rect, circle, ellipse, line, polyline, polygon, path)
/// to PDF path operators, with support for fill, stroke, transforms, and viewBox.
/// </summary>
public static class SvgRenderer
{
    /// <summary>
    /// Render an SVG element tree to PDF content stream commands.
    /// Returns the PDF content stream fragment to insert into the page.
    /// </summary>
    public static string Render(SvgElement svg, float targetX, float targetY, float targetWidth, float targetHeight)
    {
        var sb = new StringBuilder();

        // Save graphics state
        sb.AppendLine("q");

        // Apply viewBox transform if present
        float vbX = 0, vbY = 0, vbW = targetWidth, vbH = targetHeight;
        if (svg.Attributes.TryGetValue("viewBox", out var viewBox) && !string.IsNullOrEmpty(viewBox))
        {
            var parts = viewBox.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out vbX);
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out vbY);
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out vbW);
                float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out vbH);
            }
        }

        // Scale from viewBox to target size
        float scaleX = vbW > 0 ? targetWidth / vbW : 1;
        float scaleY = vbH > 0 ? targetHeight / vbH : 1;

        // Apply translation to target position + viewBox offset
        // PDF transform: translate(targetX, targetY) scale(scaleX, scaleY) translate(-vbX, -vbY)
        // Combined: cm scaleX 0 0 scaleY (targetX - vbX*scaleX) (targetY - vbY*scaleY)
        float tx = targetX - vbX * scaleX;
        float ty = targetY + targetHeight + vbY * scaleY; // PDF Y is bottom-up
        sb.AppendLine($"{F(scaleX)} 0 0 {F(-scaleY)} {F(tx)} {F(ty)} cm");

        // Render child elements
        RenderChildren(svg, sb);

        // Restore graphics state
        sb.AppendLine("Q");

        return sb.ToString();
    }

    private static void RenderChildren(SvgElement parent, StringBuilder sb)
    {
        foreach (var child in parent.Children)
        {
            RenderElement(child, sb);
        }
    }

    private static void RenderElement(SvgElement el, StringBuilder sb)
    {
        // Handle transform attribute
        bool hasTransform = el.Attributes.TryGetValue("transform", out var transformStr) && !string.IsNullOrEmpty(transformStr);
        if (hasTransform)
        {
            sb.AppendLine("q");
            ApplyTransform(sb, transformStr!);
        }

        switch (el.TagName)
        {
            case "g":
            case "svg":
                RenderChildren(el, sb);
                break;
            case "rect":
                RenderRect(el, sb);
                break;
            case "circle":
                RenderCircle(el, sb);
                break;
            case "ellipse":
                RenderEllipse(el, sb);
                break;
            case "line":
                RenderLine(el, sb);
                break;
            case "polyline":
                RenderPolyline(el, sb, false);
                break;
            case "polygon":
                RenderPolyline(el, sb, true);
                break;
            case "path":
                RenderPath(el, sb);
                break;
            case "text":
            case "tspan":
                RenderText(el, sb);
                break;
            case "defs":
            case "clipPath":
            case "mask":
                // Skip definitions (used by reference)
                break;
            case "use":
                // TODO: resolve use references
                break;
            default:
                // Unknown element — render children
                RenderChildren(el, sb);
                break;
        }

        if (hasTransform)
            sb.AppendLine("Q");
    }

    // =====================================================
    // Shape rendering
    // =====================================================

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

        SetFillStroke(el, sb);

        if (rx > 0 || ry > 0)
        {
            // Rounded rectangle using Bézier curves
            float k = 0.5523f; // Bézier approximation of quarter circle
            float kx = rx * k, ky = ry * k;

            sb.Append($"{F(x + rx)} {F(y)} m ");
            sb.Append($"{F(x + w - rx)} {F(y)} l ");
            sb.Append($"{F(x + w - rx + kx)} {F(y)} {F(x + w)} {F(y + ry - ky)} {F(x + w)} {F(y + ry)} c ");
            sb.Append($"{F(x + w)} {F(y + h - ry)} l ");
            sb.Append($"{F(x + w)} {F(y + h - ry + ky)} {F(x + w - rx + kx)} {F(y + h)} {F(x + w - rx)} {F(y + h)} c ");
            sb.Append($"{F(x + rx)} {F(y + h)} l ");
            sb.Append($"{F(x + rx - kx)} {F(y + h)} {F(x)} {F(y + h - ry + ky)} {F(x)} {F(y + h - ry)} c ");
            sb.Append($"{F(x)} {F(y + ry)} l ");
            sb.AppendLine($"{F(x)} {F(y + ry - ky)} {F(x + rx - kx)} {F(y)} {F(x + rx)} {F(y)} c h");
        }
        else
        {
            sb.AppendLine($"{F(x)} {F(y)} {F(w)} {F(h)} re");
        }

        EmitFillStroke(el, sb);
    }

    private static void RenderCircle(SvgElement el, StringBuilder sb)
    {
        float cx = GetFloat(el, "cx");
        float cy = GetFloat(el, "cy");
        float r = GetFloat(el, "r");

        SetFillStroke(el, sb);
        DrawEllipse(sb, cx, cy, r, r);
        EmitFillStroke(el, sb);
    }

    private static void RenderEllipse(SvgElement el, StringBuilder sb)
    {
        float cx = GetFloat(el, "cx");
        float cy = GetFloat(el, "cy");
        float rx = GetFloat(el, "rx");
        float ry = GetFloat(el, "ry");

        SetFillStroke(el, sb);
        DrawEllipse(sb, cx, cy, rx, ry);
        EmitFillStroke(el, sb);
    }

    private static void DrawEllipse(StringBuilder sb, float cx, float cy, float rx, float ry)
    {
        float k = 0.5523f;
        float kx = rx * k, ky = ry * k;

        sb.Append($"{F(cx + rx)} {F(cy)} m ");
        sb.Append($"{F(cx + rx)} {F(cy + ky)} {F(cx + kx)} {F(cy + ry)} {F(cx)} {F(cy + ry)} c ");
        sb.Append($"{F(cx - kx)} {F(cy + ry)} {F(cx - rx)} {F(cy + ky)} {F(cx - rx)} {F(cy)} c ");
        sb.Append($"{F(cx - rx)} {F(cy - ky)} {F(cx - kx)} {F(cy - ry)} {F(cx)} {F(cy - ry)} c ");
        sb.AppendLine($"{F(cx + kx)} {F(cy - ry)} {F(cx + rx)} {F(cy - ky)} {F(cx + rx)} {F(cy)} c h");
    }

    private static void RenderLine(SvgElement el, StringBuilder sb)
    {
        float x1 = GetFloat(el, "x1");
        float y1 = GetFloat(el, "y1");
        float x2 = GetFloat(el, "x2");
        float y2 = GetFloat(el, "y2");

        SetFillStroke(el, sb);
        sb.AppendLine($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S");
    }

    private static void RenderPolyline(SvgElement el, StringBuilder sb, bool close)
    {
        var points = el.GetAttribute("points");
        if (string.IsNullOrEmpty(points)) return;

        var coords = ParsePointsList(points);
        if (coords.Count < 2) return;

        SetFillStroke(el, sb);

        sb.Append($"{F(coords[0].x)} {F(coords[0].y)} m ");
        for (int i = 1; i < coords.Count; i++)
            sb.Append($"{F(coords[i].x)} {F(coords[i].y)} l ");

        if (close) sb.Append("h ");
        EmitFillStroke(el, sb);
    }

    private static void RenderPath(SvgElement el, StringBuilder sb)
    {
        var d = el.GetAttribute("d");
        if (string.IsNullOrEmpty(d)) return;

        SetFillStroke(el, sb);

        // Parse SVG path data and convert to PDF path operators
        ConvertSvgPathToPdf(d, sb);

        EmitFillStroke(el, sb);
    }

    private static void RenderText(SvgElement el, StringBuilder sb)
    {
        float x = GetFloat(el, "x");
        float y = GetFloat(el, "y");
        float fontSize = 16;

        var fsAttr = el.GetAttribute("font-size");
        if (!string.IsNullOrEmpty(fsAttr))
            float.TryParse(fsAttr.Replace("px", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out fontSize);

        var text = el.TextContent;
        if (string.IsNullOrEmpty(text)) return;

        // Set fill color for text
        SetFillColor(el, sb);

        sb.Append("BT ");
        sb.Append($"/Helvetica {F(fontSize)} Tf ");
        sb.Append($"{F(x)} {F(y)} Td ");
        sb.Append($"({EscapePdfString(text)}) Tj ");
        sb.AppendLine("ET");
    }

    // =====================================================
    // SVG Path data → PDF path operators
    // =====================================================

    private static void ConvertSvgPathToPdf(string d, StringBuilder sb)
    {
        float curX = 0, curY = 0;
        float startX = 0, startY = 0;
        float lastCX = 0, lastCY = 0; // last control point for smooth curves
        char lastCmd = ' ';

        int i = 0;
        while (i < d.Length)
        {
            SkipWhitespace(d, ref i);
            if (i >= d.Length) break;

            char cmd = d[i];
            if (char.IsLetter(cmd))
            {
                i++;
                lastCmd = cmd;
            }
            else
            {
                cmd = lastCmd;
            }

            bool relative = char.IsLower(cmd);
            char cmdUpper = char.ToUpper(cmd);

            switch (cmdUpper)
            {
                case 'M':
                {
                    float x = ReadNumber(d, ref i);
                    float y = ReadNumber(d, ref i);
                    if (relative) { x += curX; y += curY; }
                    curX = x; curY = y;
                    startX = x; startY = y;
                    sb.Append($"{F(x)} {F(y)} m ");
                    lastCmd = relative ? 'l' : 'L'; // subsequent coords are line-to
                    break;
                }
                case 'L':
                {
                    float x = ReadNumber(d, ref i);
                    float y = ReadNumber(d, ref i);
                    if (relative) { x += curX; y += curY; }
                    curX = x; curY = y;
                    sb.Append($"{F(x)} {F(y)} l ");
                    break;
                }
                case 'H':
                {
                    float x = ReadNumber(d, ref i);
                    if (relative) x += curX;
                    curX = x;
                    sb.Append($"{F(curX)} {F(curY)} l ");
                    break;
                }
                case 'V':
                {
                    float y = ReadNumber(d, ref i);
                    if (relative) y += curY;
                    curY = y;
                    sb.Append($"{F(curX)} {F(curY)} l ");
                    break;
                }
                case 'C':
                {
                    float x1 = ReadNumber(d, ref i), y1 = ReadNumber(d, ref i);
                    float x2 = ReadNumber(d, ref i), y2 = ReadNumber(d, ref i);
                    float x = ReadNumber(d, ref i), y = ReadNumber(d, ref i);
                    if (relative) { x1 += curX; y1 += curY; x2 += curX; y2 += curY; x += curX; y += curY; }
                    sb.Append($"{F(x1)} {F(y1)} {F(x2)} {F(y2)} {F(x)} {F(y)} c ");
                    lastCX = x2; lastCY = y2;
                    curX = x; curY = y;
                    break;
                }
                case 'S':
                {
                    // Smooth cubic: reflected control point
                    float cx1 = 2 * curX - lastCX;
                    float cy1 = 2 * curY - lastCY;
                    float x2 = ReadNumber(d, ref i), y2 = ReadNumber(d, ref i);
                    float x = ReadNumber(d, ref i), y = ReadNumber(d, ref i);
                    if (relative) { x2 += curX; y2 += curY; x += curX; y += curY; }
                    sb.Append($"{F(cx1)} {F(cy1)} {F(x2)} {F(y2)} {F(x)} {F(y)} c ");
                    lastCX = x2; lastCY = y2;
                    curX = x; curY = y;
                    break;
                }
                case 'Q':
                {
                    // Quadratic Bézier → cubic approximation
                    float qx = ReadNumber(d, ref i), qy = ReadNumber(d, ref i);
                    float x = ReadNumber(d, ref i), y = ReadNumber(d, ref i);
                    if (relative) { qx += curX; qy += curY; x += curX; y += curY; }
                    // Convert quadratic to cubic: cp1 = start + 2/3*(q-start), cp2 = end + 2/3*(q-end)
                    float cx1 = curX + 2f / 3f * (qx - curX);
                    float cy1 = curY + 2f / 3f * (qy - curY);
                    float cx2 = x + 2f / 3f * (qx - x);
                    float cy2 = y + 2f / 3f * (qy - y);
                    sb.Append($"{F(cx1)} {F(cy1)} {F(cx2)} {F(cy2)} {F(x)} {F(y)} c ");
                    lastCX = qx; lastCY = qy;
                    curX = x; curY = y;
                    break;
                }
                case 'Z':
                {
                    sb.Append("h ");
                    curX = startX; curY = startY;
                    break;
                }
                case 'A':
                {
                    // Arc: simplified — treat as line-to for now
                    ReadNumber(d, ref i); ReadNumber(d, ref i); // rx, ry
                    ReadNumber(d, ref i); // x-rotation
                    ReadNumber(d, ref i); ReadNumber(d, ref i); // large-arc, sweep
                    float x = ReadNumber(d, ref i), y = ReadNumber(d, ref i);
                    if (relative) { x += curX; y += curY; }
                    sb.Append($"{F(x)} {F(y)} l ");
                    curX = x; curY = y;
                    break;
                }
                default:
                    i++; // skip unknown
                    break;
            }
        }
        sb.AppendLine();
    }

    // =====================================================
    // Fill/Stroke
    // =====================================================

    private static void SetFillStroke(SvgElement el, StringBuilder sb)
    {
        SetFillColor(el, sb);
        SetStrokeColor(el, sb);
    }

    private static void SetFillColor(SvgElement el, StringBuilder sb)
    {
        var fill = el.GetAttribute("fill");
        if (string.IsNullOrEmpty(fill) || fill == "none") return;

        var (r, g, b) = ParseSvgColor(fill);
        sb.AppendLine($"{F(r)} {F(g)} {F(b)} rg");
    }

    private static void SetStrokeColor(SvgElement el, StringBuilder sb)
    {
        var stroke = el.GetAttribute("stroke");
        if (string.IsNullOrEmpty(stroke) || stroke == "none") return;

        var (r, g, b) = ParseSvgColor(stroke);
        sb.AppendLine($"{F(r)} {F(g)} {F(b)} RG");

        var strokeWidth = el.GetAttribute("stroke-width");
        if (!string.IsNullOrEmpty(strokeWidth))
        {
            if (float.TryParse(strokeWidth.Replace("px", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float sw))
                sb.AppendLine($"{F(sw)} w");
        }
    }

    private static void EmitFillStroke(SvgElement el, StringBuilder sb)
    {
        bool hasFill = el.GetAttribute("fill") != "none";
        bool hasStroke = !string.IsNullOrEmpty(el.GetAttribute("stroke")) && el.GetAttribute("stroke") != "none";

        if (hasFill && hasStroke) sb.AppendLine("B"); // fill and stroke
        else if (hasFill) sb.AppendLine("f");
        else if (hasStroke) sb.AppendLine("S");
        else sb.AppendLine("f"); // default: fill with black
    }

    // =====================================================
    // Transform
    // =====================================================

    private static void ApplyTransform(StringBuilder sb, string transform)
    {
        // Parse simple transforms: translate, scale, rotate, matrix
        int i = 0;
        while (i < transform.Length)
        {
            SkipWhitespace(transform, ref i);
            if (i >= transform.Length) break;

            if (transform.Substring(i).StartsWith("translate(", StringComparison.OrdinalIgnoreCase))
            {
                i += 10;
                float tx = ReadNumber(transform, ref i);
                float ty = ReadNumberOpt(transform, ref i, 0);
                SkipParen(transform, ref i);
                sb.AppendLine($"1 0 0 1 {F(tx)} {F(ty)} cm");
            }
            else if (transform.Substring(i).StartsWith("scale(", StringComparison.OrdinalIgnoreCase))
            {
                i += 6;
                float sx = ReadNumber(transform, ref i);
                float sy = ReadNumberOpt(transform, ref i, sx);
                SkipParen(transform, ref i);
                sb.AppendLine($"{F(sx)} 0 0 {F(sy)} 0 0 cm");
            }
            else if (transform.Substring(i).StartsWith("rotate(", StringComparison.OrdinalIgnoreCase))
            {
                i += 7;
                float angle = ReadNumber(transform, ref i);
                SkipParen(transform, ref i);
                float rad = angle * (float)Math.PI / 180f;
                float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
                sb.AppendLine($"{F(cos)} {F(sin)} {F(-sin)} {F(cos)} 0 0 cm");
            }
            else if (transform.Substring(i).StartsWith("matrix(", StringComparison.OrdinalIgnoreCase))
            {
                i += 7;
                float a = ReadNumber(transform, ref i);
                float b = ReadNumber(transform, ref i);
                float c = ReadNumber(transform, ref i);
                float d = ReadNumber(transform, ref i);
                float e = ReadNumber(transform, ref i);
                float f = ReadNumber(transform, ref i);
                SkipParen(transform, ref i);
                sb.AppendLine($"{F(a)} {F(b)} {F(c)} {F(d)} {F(e)} {F(f)} cm");
            }
            else
            {
                i++;
            }
        }
    }

    // =====================================================
    // Color parsing
    // =====================================================

    private static (float r, float g, float b) ParseSvgColor(string color)
    {
        if (string.IsNullOrEmpty(color)) return (0, 0, 0);
        color = color.Trim();

        // Hex color
        if (color.StartsWith("#"))
        {
            if (color.Length == 4) // #RGB
            {
                int r = Convert.ToInt32(new string(color[1], 2), 16);
                int g = Convert.ToInt32(new string(color[2], 2), 16);
                int b = Convert.ToInt32(new string(color[3], 2), 16);
                return (r / 255f, g / 255f, b / 255f);
            }
            if (color.Length == 7) // #RRGGBB
            {
                int r = Convert.ToInt32(color.Substring(1, 2), 16);
                int g = Convert.ToInt32(color.Substring(3, 2), 16);
                int b = Convert.ToInt32(color.Substring(5, 2), 16);
                return (r / 255f, g / 255f, b / 255f);
            }
        }

        // rgb() function
        if (color.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
        {
            var inner = color.Substring(4, color.Length - 5);
            var parts = inner.Split(',');
            if (parts.Length >= 3)
            {
                float.TryParse(parts[0].Trim(), out float r);
                float.TryParse(parts[1].Trim(), out float g);
                float.TryParse(parts[2].Trim(), out float b);
                return (r / 255f, g / 255f, b / 255f);
            }
        }

        // Named colors (common subset)
        switch (color.ToLowerInvariant())
        {
            case "black": return (0, 0, 0);
            case "white": return (1, 1, 1);
            case "red": return (1, 0, 0);
            case "green": return (0, 0.502f, 0);
            case "blue": return (0, 0, 1);
            case "yellow": return (1, 1, 0);
            case "orange": return (1, 0.647f, 0);
            case "gray": case "grey": return (0.502f, 0.502f, 0.502f);
            case "none": return (0, 0, 0);
            default: return (0, 0, 0);
        }
    }

    // =====================================================
    // Helpers
    // =====================================================

    private static float GetFloat(SvgElement el, string attr, float defaultValue = 0)
    {
        var val = el.GetAttribute(attr);
        if (!string.IsNullOrEmpty(val))
        {
            val = val.Replace("px", "");
            if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                return result;
        }
        return defaultValue;
    }

    private static List<(float x, float y)> ParsePointsList(string points)
    {
        var result = new List<(float x, float y)>();
        var nums = new List<float>();
        int i = 0;
        while (i < points.Length)
        {
            SkipWhitespace(points, ref i);
            if (i >= points.Length) break;
            if (points[i] == ',') { i++; continue; }
            nums.Add(ReadNumber(points, ref i));
        }
        for (int j = 0; j + 1 < nums.Count; j += 2)
            result.Add((nums[j], nums[j + 1]));
        return result;
    }

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n' || s[i] == ','))
            i++;
    }

    private static void SkipParen(string s, ref int i)
    {
        while (i < s.Length && s[i] != ')') i++;
        if (i < s.Length) i++; // skip ')'
    }

    private static float ReadNumber(string s, ref int i)
    {
        SkipWhitespace(s, ref i);
        if (i >= s.Length) return 0;

        int start = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            i++;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && char.IsDigit(s[i])) i++;
        }

        if (i == start) return 0;
        float.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out float val);
        return val;
    }

    private static float ReadNumberOpt(string s, ref int i, float defaultVal)
    {
        SkipWhitespace(s, ref i);
        if (i >= s.Length || s[i] == ')') return defaultVal;
        return ReadNumber(s, ref i);
    }

    private static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);

    private static string EscapePdfString(string text)
        => text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
