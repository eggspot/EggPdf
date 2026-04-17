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

        // Calculate gradient axis based on angle (CSS: 0deg = top, 90deg = right, 180deg = bottom)
        // Convert to math angle: rad = (90 - angle) degrees
        float rad = (90f - angle) * (float)Math.PI / 180f;
        float cos = (float)Math.Cos(rad);
        float sin = (float)Math.Sin(rad);

        // Build PDF content: clip to rect, fill with gradient approximation (N color bands)
        var sb = new StringBuilder();
        sb.AppendLine("q");
        sb.AppendLine($"{F(x)} {F(y)} {F(width)} {F(height)} re W n");

        // Determine dominant direction
        bool horizontal = Math.Abs(cos) >= Math.Abs(sin);

        int bands = 40; // more bands = smoother gradient
        for (int i = 0; i <= bands; i++)
        {
            float t = (float)i / bands;
            // Interpolate across all stops using t position
            var (r, g, b) = InterpolateStops(stops, t);

            float bandX, bandY, bandW, bandH;
            if (horizontal)
            {
                // Horizontal: vertical strips varying in X direction
                bool rightward = cos >= 0;
                float tBand = rightward ? t : 1f - t;
                bandX = x + width * tBand;
                bandY = y;
                bandW = width / bands + 0.5f;
                bandH = height;
            }
            else
            {
                // Vertical: horizontal strips varying in Y direction
                bool downward = sin <= 0; // In PDF y-axis, sin <= 0 means downward in page coords
                float tBand = downward ? t : 1f - t;
                bandX = x;
                bandY = y + height * tBand;
                bandW = width;
                bandH = height / bands + 0.5f;
            }

            sb.AppendLine($"{F(r)} {F(g)} {F(b)} rg");
            sb.AppendLine($"{F(bandX)} {F(bandY)} {F(bandW)} {F(bandH)} re f");
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

    /// <summary>Interpolate color at position t (0..1) across a multi-stop list.</summary>
    private static (float r, float g, float b) InterpolateStops(List<(float r, float g, float b, float position)> stops, float t)
    {
        if (stops.Count == 1) return (stops[0].r, stops[0].g, stops[0].b);
        if (t <= stops[0].position) return (stops[0].r, stops[0].g, stops[0].b);
        if (t >= stops[stops.Count - 1].position) return (stops[stops.Count - 1].r, stops[stops.Count - 1].g, stops[stops.Count - 1].b);

        for (int i = 0; i < stops.Count - 1; i++)
        {
            var s0 = stops[i];
            var s1 = stops[i + 1];
            if (t >= s0.position && t <= s1.position)
            {
                float span = s1.position - s0.position;
                float local = span > 0 ? (t - s0.position) / span : 0;
                return (
                    s0.r + (s1.r - s0.r) * local,
                    s0.g + (s1.g - s0.g) * local,
                    s0.b + (s1.b - s0.b) * local
                );
            }
        }
        return (stops[stops.Count - 1].r, stops[stops.Count - 1].g, stops[stops.Count - 1].b);
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

    internal static (float r, float g, float b) ParseSimpleColor(string color)
    {
        color = color.Trim();
        // Strip position hint if present (e.g. "red 20%", "blue 80px")
        int spaceIdx = color.IndexOf(' ');
        if (spaceIdx > 0) color = color.Substring(0, spaceIdx);

        // Use the full EggPdf.Core color parser (handles all CSS color formats)
        var parsed = EggPdf.Core.Color.TryParse(color);
        if (parsed.HasValue)
            return (parsed.Value.R / 255f, parsed.Value.G / 255f, parsed.Value.B / 255f);

        return (0, 0, 0); // fallback: black
    }

    private static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
}
