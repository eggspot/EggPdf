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

        var colors = new List<(float r, float g, float b)>();
        for (int i = colorStart; i < parts.Count; i++)
            colors.Add(ParseColor(parts[i].Trim().Split(' ')[0]));

        if (colors.Count < 2) return null;

        var c1 = colors[0]; // center color
        var c2 = colors[colors.Count - 1]; // edge color

        float cx = x + width / 2;
        float cy = y + height / 2;
        float maxRadius = (float)Math.Sqrt(width * width + height * height) / 2;

        var sb = new StringBuilder();
        sb.AppendLine("q");
        sb.AppendLine($"{F(x)} {F(y)} {F(width)} {F(height)} re W n"); // clip

        // Draw concentric circles from outside in (so inner colors paint over outer)
        int bands = 25;
        for (int i = bands; i >= 0; i--)
        {
            float t = (float)i / bands;
            float r = c2.r + (c1.r - c2.r) * (1 - t);
            float g = c2.g + (c1.g - c2.g) * (1 - t);
            float b = c2.b + (c1.b - c2.b) * (1 - t);
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

    private static (float r, float g, float b) ParseColor(string color)
    {
        color = color.Trim();
        if (color.StartsWith("#") && color.Length == 7)
        {
            return (Convert.ToInt32(color.Substring(1, 2), 16) / 255f,
                    Convert.ToInt32(color.Substring(3, 2), 16) / 255f,
                    Convert.ToInt32(color.Substring(5, 2), 16) / 255f);
        }
        switch (color.ToLowerInvariant())
        {
            case "red": return (1, 0, 0);
            case "blue": return (0, 0, 1);
            case "green": return (0, 0.5f, 0);
            case "yellow": return (1, 1, 0);
            case "white": return (1, 1, 1);
            case "black": return (0, 0, 0);
            case "transparent": return (1, 1, 1);
            default: return (0, 0, 0);
        }
    }

    private static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
}
