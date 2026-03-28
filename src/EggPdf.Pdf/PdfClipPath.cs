using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// CSS clip-path support: renders clipping shapes as PDF clipping paths.
/// Supports: circle(), ellipse(), polygon(), inset().
/// </summary>
public static class PdfClipPath
{
    /// <summary>
    /// Generate PDF clipping path commands from a CSS clip-path value.
    /// Returns content stream commands that establish the clipping region.
    /// Caller must wrap in q/Q to scope the clip.
    /// </summary>
    public static string? GenerateClipPath(string clipPath, float x, float y, float width, float height)
    {
        if (string.IsNullOrEmpty(clipPath) || clipPath == "none") return null;

        var trimmed = clipPath.Trim();
        var sb = new StringBuilder();

        if (trimmed.StartsWith("circle(", StringComparison.OrdinalIgnoreCase))
        {
            GenerateCircleClip(trimmed, x, y, width, height, sb);
        }
        else if (trimmed.StartsWith("ellipse(", StringComparison.OrdinalIgnoreCase))
        {
            GenerateEllipseClip(trimmed, x, y, width, height, sb);
        }
        else if (trimmed.StartsWith("polygon(", StringComparison.OrdinalIgnoreCase))
        {
            GeneratePolygonClip(trimmed, x, y, width, height, sb);
        }
        else if (trimmed.StartsWith("inset(", StringComparison.OrdinalIgnoreCase))
        {
            GenerateInsetClip(trimmed, x, y, width, height, sb);
        }
        else
        {
            return null;
        }

        sb.AppendLine("W n"); // Set clipping path, then clear path
        return sb.ToString();
    }

    private static void GenerateCircleClip(string value, float x, float y, float w, float h, StringBuilder sb)
    {
        // circle(50% at 50% 50%) or circle(100px)
        var inner = value.Substring(7, value.Length - 8).Trim();

        float radius = Math.Min(w, h) / 2;
        float cx = x + w / 2;
        float cy = y + h / 2;

        var parts = inner.Split(new[] { " at " }, StringSplitOptions.None);
        if (parts.Length >= 1 && !string.IsNullOrEmpty(parts[0].Trim()))
            radius = ResolveValue(parts[0].Trim(), Math.Min(w, h));

        if (parts.Length >= 2)
        {
            var pos = parts[1].Trim().Split(' ');
            if (pos.Length >= 1) cx = x + ResolveValue(pos[0], w);
            if (pos.Length >= 2) cy = y + ResolveValue(pos[1], h);
        }

        DrawEllipse(sb, cx, cy, radius, radius);
    }

    private static void GenerateEllipseClip(string value, float x, float y, float w, float h, StringBuilder sb)
    {
        var inner = value.Substring(8, value.Length - 9).Trim();
        float rx = w / 2, ry = h / 2;
        float cx = x + w / 2, cy = y + h / 2;

        var parts = inner.Split(new[] { " at " }, StringSplitOptions.None);
        if (parts.Length >= 1)
        {
            var radii = parts[0].Trim().Split(' ');
            if (radii.Length >= 1) rx = ResolveValue(radii[0], w);
            if (radii.Length >= 2) ry = ResolveValue(radii[1], h);
        }
        if (parts.Length >= 2)
        {
            var pos = parts[1].Trim().Split(' ');
            if (pos.Length >= 1) cx = x + ResolveValue(pos[0], w);
            if (pos.Length >= 2) cy = y + ResolveValue(pos[1], h);
        }

        DrawEllipse(sb, cx, cy, rx, ry);
    }

    private static void GeneratePolygonClip(string value, float x, float y, float w, float h, StringBuilder sb)
    {
        var inner = value.Substring(8, value.Length - 9).Trim();
        var points = inner.Split(',');

        bool first = true;
        foreach (var point in points)
        {
            var coords = point.Trim().Split(' ');
            if (coords.Length < 2) continue;

            float px = x + ResolveValue(coords[0], w);
            float py = y + ResolveValue(coords[1], h);

            if (first)
            {
                sb.Append($"{F(px)} {F(py)} m ");
                first = false;
            }
            else
            {
                sb.Append($"{F(px)} {F(py)} l ");
            }
        }
        sb.AppendLine("h");
    }

    private static void GenerateInsetClip(string value, float x, float y, float w, float h, StringBuilder sb)
    {
        var inner = value.Substring(6, value.Length - 7).Trim();
        var parts = inner.Split(' ');

        float top = 0, right = 0, bottom = 0, left = 0;
        if (parts.Length >= 1) top = ResolveValue(parts[0], h);
        if (parts.Length >= 2) right = ResolveValue(parts[1], w);
        if (parts.Length >= 3) bottom = ResolveValue(parts[2], h);
        if (parts.Length >= 4) left = ResolveValue(parts[3], w);
        if (parts.Length == 1) { right = top; bottom = top; left = top; }
        if (parts.Length == 2) { bottom = top; left = right; }

        float ix = x + left;
        float iy = y + top;
        float iw = w - left - right;
        float ih = h - top - bottom;

        sb.AppendLine($"{F(ix)} {F(iy)} {F(iw)} {F(ih)} re");
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

    private static float ResolveValue(string value, float reference)
    {
        value = value.Trim();
        if (value.EndsWith("%"))
        {
            float.TryParse(value.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct);
            return reference * pct / 100f;
        }
        value = value.Replace("px", "");
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float px);
        return px;
    }

    private static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
}
