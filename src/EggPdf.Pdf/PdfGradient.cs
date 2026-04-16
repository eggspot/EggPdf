using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Generates PDF shading patterns for CSS linear-gradient().
/// Converts CSS gradient syntax to PDF Type 2 (axial) shading.
/// </summary>
public static class PdfGradient
{
    /// <summary>
    /// Parse a CSS linear-gradient() value and return PDF content stream commands
    /// to render it as a filled rectangle with gradient shading.
    /// </summary>
    public static string? RenderLinearGradient(string cssGradient, float x, float y, float width, float height)
    {
        if (string.IsNullOrEmpty(cssGradient)) return null;

        // Parse: linear-gradient(direction, color1, color2, ...)
        var inner = cssGradient.Trim();
        if (inner.StartsWith("linear-gradient(", StringComparison.OrdinalIgnoreCase))
            inner = inner.Substring(16, inner.Length - 17); // strip "linear-gradient(" and ")"
        else if (inner.StartsWith("repeating-linear-gradient(", StringComparison.OrdinalIgnoreCase))
            inner = inner.Substring(26, inner.Length - 27);
        else
            return null;

        // Parse direction and color stops
        var parts = SplitGradientArgs(inner);
        if (parts.Count < 2) return null;

        float angle = 180; // default: top to bottom
        int colorStart = 0;

        // Check if first part is a direction
        var first = parts[0].Trim();
        if (first.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
        {
            float.TryParse(first.Replace("deg", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out angle);
            colorStart = 1;
        }
        else if (first.StartsWith("to ", StringComparison.OrdinalIgnoreCase))
        {
            angle = ParseDirection(first);
            colorStart = 1;
        }

        // Parse color stops
        var stops = new List<(float r, float g, float b, float position)>();
        int numColors = parts.Count - colorStart;
        for (int i = colorStart; i < parts.Count; i++)
        {
            var stopStr = parts[i].Trim();
            var (r, g, b) = ParseSimpleColor(stopStr);
            float pos = numColors > 1 ? (float)(i - colorStart) / (numColors - 1) : 0;
            stops.Add((r, g, b, pos));
        }

        if (stops.Count < 2) return null;

        // For PDF, we approximate with a simple two-color gradient using the first and last stops
        var c1 = stops[0];
        var c2 = stops[stops.Count - 1];

        // Calculate gradient axis based on angle
        float rad = (90 - angle) * (float)Math.PI / 180f;
        float cos = (float)Math.Cos(rad);
        float sin = (float)Math.Sin(rad);

        float cx = x + width / 2;
        float cy = y + height / 2;
        float halfDiag = (float)Math.Sqrt(width * width + height * height) / 2;

        float x0 = cx - cos * halfDiag;
        float y0 = cy - sin * halfDiag;
        float x1 = cx + cos * halfDiag;
        float y1 = cy + sin * halfDiag;

        // Build PDF content: clip to rect, fill with gradient approximation
        // For simplicity, render as multiple thin filled rectangles (gradient simulation)
        var sb = new StringBuilder();
        sb.AppendLine("q");

        // Clip to the target rectangle
        sb.AppendLine($"{F(x)} {F(y)} {F(width)} {F(height)} re W n");

        // Render gradient as 20 color bands
        int bands = 20;
        for (int i = 0; i < bands; i++)
        {
            float t = (float)i / bands;
            float r = c1.r + (c2.r - c1.r) * t;
            float g = c1.g + (c2.g - c1.g) * t;
            float b = c1.b + (c2.b - c1.b) * t;

            float bandY = y + height * i / bands;
            float bandH = height / bands + 0.5f; // slight overlap to avoid gaps

            sb.AppendLine($"{F(r)} {F(g)} {F(b)} rg");
            sb.AppendLine($"{F(x)} {F(bandY)} {F(width)} {F(bandH)} re f");
        }

        sb.AppendLine("Q");
        return sb.ToString();
    }

    /// <summary>
    /// Render a CSS repeating-radial-gradient() as PDF content stream commands.
    /// Approximated as radial gradient bands (same as radial-gradient, patterns not repeated).
    /// </summary>
    public static string? RenderRepeatingRadialGradient(string cssGradient, float x, float y, float width, float height)
    {
        if (string.IsNullOrEmpty(cssGradient)) return null;
        // Re-map prefix so PdfRadialGradient.Render can parse the color stops
        var remapped = "radial-gradient(" +
            cssGradient.Substring("repeating-radial-gradient(".Length);
        return PdfRadialGradient.Render(remapped, x, y, width, height);
    }

    private static List<string> SplitGradientArgs(string args)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;
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

    private static float ParseDirection(string dir)
    {
        dir = dir.Trim().ToLowerInvariant();
        if (dir.Contains("top") && dir.Contains("right")) return 45;
        if (dir.Contains("bottom") && dir.Contains("right")) return 135;
        if (dir.Contains("bottom") && dir.Contains("left")) return 225;
        if (dir.Contains("top") && dir.Contains("left")) return 315;
        if (dir.Contains("top")) return 0;
        if (dir.Contains("right")) return 90;
        if (dir.Contains("bottom")) return 180;
        if (dir.Contains("left")) return 270;
        return 180;
    }

    private static (float r, float g, float b) ParseSimpleColor(string color)
    {
        color = color.Trim().Split(' ')[0]; // strip position if present

        if (color.StartsWith("#") && color.Length == 7)
        {
            int r = Convert.ToInt32(color.Substring(1, 2), 16);
            int g = Convert.ToInt32(color.Substring(3, 2), 16);
            int b = Convert.ToInt32(color.Substring(5, 2), 16);
            return (r / 255f, g / 255f, b / 255f);
        }
        if (color.StartsWith("#") && color.Length == 4)
        {
            int r = Convert.ToInt32(new string(color[1], 2), 16);
            int g = Convert.ToInt32(new string(color[2], 2), 16);
            int b = Convert.ToInt32(new string(color[3], 2), 16);
            return (r / 255f, g / 255f, b / 255f);
        }

        switch (color.ToLowerInvariant())
        {
            case "red": return (1, 0, 0);
            case "blue": return (0, 0, 1);
            case "green": return (0, 0.5f, 0);
            case "yellow": return (1, 1, 0);
            case "orange": return (1, 0.647f, 0);
            case "purple": return (0.5f, 0, 0.5f);
            case "white": return (1, 1, 1);
            case "black": return (0, 0, 0);
            case "gray": case "grey": return (0.5f, 0.5f, 0.5f);
            case "transparent": return (1, 1, 1);
            default: return (0, 0, 0);
        }
    }

    private static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
}
