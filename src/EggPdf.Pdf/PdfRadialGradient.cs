using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Renders CSS radial-gradient() as PDF filled circles with gradient bands.
/// Approximates radial gradients using concentric circles with interpolated colors.
/// </summary>
public static class PdfRadialGradient
{
    /// <summary>
    /// Render a CSS radial-gradient() as PDF content stream commands.
    /// </summary>
    public static string? Render(string cssGradient, float x, float y, float width, float height)
    {
        if (string.IsNullOrEmpty(cssGradient)) return null;

        var inner = cssGradient.Trim();
        if (inner.StartsWith("radial-gradient(", StringComparison.OrdinalIgnoreCase))
            inner = inner.Substring(16, inner.Length - 17);
        else
            return null;

        // Parse color stops
        var parts = SplitArgs(inner);
        if (parts.Count < 2) return null;

        int colorStart = 0;
        // Skip shape/position keywords
        var first = parts[0].Trim().ToLowerInvariant();
        if (first.Contains("circle") || first.Contains("ellipse") || first.Contains("at ") ||
            first.Contains("closest") || first.Contains("farthest"))
            colorStart = 1;

        // Build stop list with positions (0..1)
        var stops = new List<(float r, float g, float b, float position)>();
        int numColors = parts.Count - colorStart;
        for (int i = colorStart; i < parts.Count; i++)
        {
            var (cr, cg, cb) = PdfGradient.ParseSimpleColor(parts[i].Trim());
            float pos = numColors > 1 ? (float)(i - colorStart) / (numColors - 1) : 0f;
            stops.Add((cr, cg, cb, pos));
        }

        if (stops.Count < 2) return null;

        float cx = x + width / 2;
        float cy = y + height / 2;
        float maxRadius = (float)Math.Sqrt(width * width + height * height) / 2;

        var sb = new StringBuilder();
        sb.AppendLine("q");
        sb.AppendLine($"{F(x)} {F(y)} {F(width)} {F(height)} re W n"); // clip

        // Draw concentric circles from outside in (inner colors paint over outer)
        int bands = 30;
        for (int i = bands; i >= 0; i--)
        {
            float t = (float)i / bands;
            // t=0 → center (stop[0] color), t=1 → edge (stop[last] color)
            var (r, g, b) = InterpolateStops(stops, 1f - t);
            float radius = maxRadius * t;

            if (radius < 0.5f) radius = 0.5f;

            sb.AppendLine($"{F(r)} {F(g)} {F(b)} rg");
            DrawCircle(sb, cx, cy, radius);
            sb.AppendLine("f");
        }

        sb.AppendLine("Q");
        return sb.ToString();
    }

    private static void DrawCircle(StringBuilder sb, float cx, float cy, float r)
    {
        float k = r * 0.5523f;
        sb.Append($"{F(cx + r)} {F(cy)} m ");
        sb.Append($"{F(cx + r)} {F(cy + k)} {F(cx + k)} {F(cy + r)} {F(cx)} {F(cy + r)} c ");
        sb.Append($"{F(cx - k)} {F(cy + r)} {F(cx - r)} {F(cy + k)} {F(cx - r)} {F(cy)} c ");
        sb.Append($"{F(cx - r)} {F(cy - k)} {F(cx - k)} {F(cy - r)} {F(cx)} {F(cy - r)} c ");
        sb.AppendLine($"{F(cx + k)} {F(cy - r)} {F(cx + r)} {F(cy - k)} {F(cx + r)} {F(cy)} c h");
    }

    private static List<string> SplitArgs(string args)
    {
        var result = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == '(') depth++;
            else if (args[i] == ')') depth--;
            else if (args[i] == ',' && depth == 0)
            {
                result.Add(args.Substring(start, i - start));
                start = i + 1;
            }
        }
        result.Add(args.Substring(start));
        return result;
    }

    private static (float r, float g, float b) InterpolateStops(
        List<(float r, float g, float b, float position)> stops, float t)
    {
        if (stops.Count == 1) return (stops[0].r, stops[0].g, stops[0].b);
        if (t <= stops[0].position) return (stops[0].r, stops[0].g, stops[0].b);
        if (t >= stops[stops.Count - 1].position)
            return (stops[stops.Count - 1].r, stops[stops.Count - 1].g, stops[stops.Count - 1].b);

        for (int i = 0; i < stops.Count - 1; i++)
        {
            var s0 = stops[i]; var s1 = stops[i + 1];
            if (t >= s0.position && t <= s1.position)
            {
                float span = s1.position - s0.position;
                float local = span > 0 ? (t - s0.position) / span : 0;
                return (s0.r + (s1.r - s0.r) * local, s0.g + (s1.g - s0.g) * local, s0.b + (s1.b - s0.b) * local);
            }
        }
        return (stops[stops.Count - 1].r, stops[stops.Count - 1].g, stops[stops.Count - 1].b);
    }

    private static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
}
