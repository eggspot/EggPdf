using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EggPdf.Core;

namespace EggPdf.Pdf;

/// <summary>
/// Renders CSS conic-gradient() as PDF filled pie sectors.
/// PDF has no native conic gradient support; we approximate with N filled triangles
/// centered on the element, each colored with the interpolated stop color at that angle.
/// </summary>
public static class PdfConicGradient
{
    private const int Sectors = 36; // 10° per sector — good balance of quality vs output size

    /// <summary>
    /// Parse a CSS conic-gradient() and return PDF content stream commands.
    /// </summary>
    public static string? Render(string cssGradient, float x, float y, float width, float height)
    {
        if (string.IsNullOrEmpty(cssGradient)) return null;

        var inner = cssGradient.Trim();
        if (!inner.StartsWith("conic-gradient(", StringComparison.OrdinalIgnoreCase)) return null;
        inner = inner.Substring(15, inner.Length - 16); // strip "conic-gradient(" and ")"

        // Parse optional "from <angle>" prefix
        float startDeg = 0f;
        if (inner.StartsWith("from ", StringComparison.OrdinalIgnoreCase))
        {
            int commaIdx = inner.IndexOf(',');
            if (commaIdx > 0)
            {
                var fromClause = inner.Substring(0, commaIdx).Trim();
                inner = inner.Substring(commaIdx + 1).Trim();
                // "from 90deg" or "from 90deg at center"
                var atIdx = fromClause.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
                var fromPart = atIdx > 0 ? fromClause.Substring(0, atIdx) : fromClause;
                fromPart = fromPart.Substring(5).Trim(); // strip "from "
                if (fromPart.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
                    float.TryParse(fromPart.Substring(0, fromPart.Length - 3),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out startDeg);
            }
        }

        // Parse color stops: "red", "blue 50%", "yellow 120deg"
        var stops = ParseColorStops(inner);
        if (stops.Count < 2) return null;

        float cx = x + width / 2f;
        float cy = y + height / 2f;
        float radius = (float)Math.Sqrt(width * width + height * height) / 2f + 1f;

        var sb = new StringBuilder();
        sb.AppendLine("q");
        // Clip to bounding rectangle
        sb.AppendLine($"{F(x)} {F(y)} {F(width)} {F(height)} re W n");

        for (int i = 0; i < Sectors; i++)
        {
            float t1 = (float)i / Sectors;
            float t2 = (float)(i + 1) / Sectors;
            float tMid = (t1 + t2) / 2f;

            var (r, g, b) = InterpolateStops(stops, tMid);

            float angle1 = (startDeg + t1 * 360f) * (float)Math.PI / 180f;
            float angle2 = (startDeg + t2 * 360f) * (float)Math.PI / 180f;

            // Pie slice: move to center, line to arc start, arc end, close
            float px1 = cx + radius * (float)Math.Cos(angle1);
            float py1 = cy + radius * (float)Math.Sin(angle1);
            float px2 = cx + radius * (float)Math.Cos(angle2);
            float py2 = cy + radius * (float)Math.Sin(angle2);

            sb.AppendLine($"{F(r)} {F(g)} {F(b)} rg");
            sb.AppendLine($"{F(cx)} {F(cy)} m {F(px1)} {F(py1)} l {F(px2)} {F(py2)} l h f");
        }

        sb.AppendLine("Q");
        return sb.ToString();
    }

    private static List<(float r, float g, float b, float t)> ParseColorStops(string inner)
    {
        var result = new List<(float r, float g, float b, float t)>();
        var parts = SplitArgs(inner);
        int numStops = parts.Count;

        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i].Trim();

            // Split off trailing position: "red 30%" or "blue 120deg"
            float? position = null;
            var spaceIdx = part.LastIndexOf(' ');
            if (spaceIdx > 0)
            {
                var posPart = part.Substring(spaceIdx + 1);
                if (posPart.EndsWith("%"))
                {
                    if (float.TryParse(posPart.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                    { position = pct / 100f; part = part.Substring(0, spaceIdx).Trim(); }
                }
                else if (posPart.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(posPart.Substring(0, posPart.Length - 3), NumberStyles.Float, CultureInfo.InvariantCulture, out float deg))
                    { position = deg / 360f; part = part.Substring(0, spaceIdx).Trim(); }
                }
            }

            var c = Color.TryParse(part);
            if (c == null) continue;

            float t = position ?? (numStops > 1 ? (float)i / (numStops - 1) : 0f);
            result.Add((c.Value.R / 255f, c.Value.G / 255f, c.Value.B / 255f, t));
        }

        return result;
    }

    private static (float r, float g, float b) InterpolateStops(
        List<(float r, float g, float b, float t)> stops, float t)
    {
        if (stops.Count == 0) return (0, 0, 0);
        if (t <= stops[0].t) return (stops[0].r, stops[0].g, stops[0].b);
        if (t >= stops[stops.Count - 1].t) return (stops[stops.Count - 1].r, stops[stops.Count - 1].g, stops[stops.Count - 1].b);

        for (int i = 0; i < stops.Count - 1; i++)
        {
            var s1 = stops[i];
            var s2 = stops[i + 1];
            if (t >= s1.t && t <= s2.t)
            {
                float range = s2.t - s1.t;
                float f = range > 0 ? (t - s1.t) / range : 0f;
                return (
                    s1.r + (s2.r - s1.r) * f,
                    s1.g + (s2.g - s1.g) * f,
                    s1.b + (s2.b - s1.b) * f);
            }
        }
        return (stops[stops.Count - 1].r, stops[stops.Count - 1].g, stops[stops.Count - 1].b);
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
            { result.Add(args.Substring(start, i - start)); start = i + 1; }
        }
        result.Add(args.Substring(start));
        return result;
    }

    private static string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);
}
