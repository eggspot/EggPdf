using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EggPdf.Core;

namespace EggPdf.Pdf;

/// <summary>
/// Paints CSS <c>conic-gradient()</c> and <c>repeating-conic-gradient()</c> backgrounds as PDF content: thin
/// pie sectors around the gradient centre, starting at 12 o'clock (or the <c>from</c> angle) and going clockwise.
/// Supports <c>at &lt;position&gt;</c>, stop positions in degrees, turns or percentages, and translucent stops.
/// </summary>
public static class PdfConicGradient
{
    private const int Sectors = 180;
    private const float DegreesToRadians = (float)(Math.PI / 180.0);

    /// <summary>
    /// Parse a CSS conic-gradient() or repeating-conic-gradient() value and return PDF content stream commands
    /// that fill the box (PDF points, bottom-left origin), or null when the value is not a valid gradient.
    /// Translucent stops are drawn opaque; use the overload taking <c>usedExtGStates</c> to keep alpha.
    /// </summary>
    public static string? Render(string cssGradient, float x, float y, float width, float height) =>
        Render(cssGradient, x, y, width, height, null);

    /// <summary>
    /// Parse a CSS conic-gradient() or repeating-conic-gradient() value and return PDF content stream commands
    /// that fill the box (PDF points, bottom-left origin), or null when the value is not a valid gradient.
    /// Translucent sectors select opacity graphics states whose names are added to <paramref name="usedExtGStates"/>
    /// (when it is null, sectors are drawn opaque); <paramref name="opacity"/> (0-1) multiplies every sector's alpha.
    /// </summary>
    public static string? Render(string cssGradient, float x, float y, float width, float height,
        ICollection<string>? usedExtGStates, float opacity = 1f)
    {
        if (string.IsNullOrEmpty(cssGradient) || width <= 0f || height <= 0f) return null;

        var value = cssGradient.Trim();
        bool repeating;
        if (value.StartsWith("conic-gradient(", StringComparison.OrdinalIgnoreCase)) repeating = false;
        else if (value.StartsWith("repeating-conic-gradient(", StringComparison.OrdinalIgnoreCase)) repeating = true;
        else return null;
        int open = value.IndexOf('(');
        int close = value.LastIndexOf(')');
        if (close <= open) return null;
        var parts = PdfGradient.SplitGradientArgs(value.Substring(open + 1, close - open - 1));

        float startDegrees = 0f;
        string? position = null;
        int firstStop = 0;
        if (!Color.TryParse(GradientStops.ColorToken(parts[0])).HasValue)
        {
            ParseFromAndAt(parts[0], out startDegrees, out position);
            firstStop = 1;
        }

        for (int i = firstStop; i < parts.Count; i++)
            parts[i] = AnglesAsPercentages(parts[i]);
        var stops = GradientStops.Parse(parts, firstStop, 1f);
        if (stops == null) return null;

        var (fromLeft, fromTop) = GradientGeometry.ParsePosition(position, width, height);
        float cx = x + fromLeft, cy = y + height - fromTop;
        float farX = Math.Max(Math.Abs(fromLeft), Math.Abs(width - fromLeft));
        float farY = Math.Max(Math.Abs(fromTop), Math.Abs(height - fromTop));
        float radius = (float)Math.Sqrt(farX * farX + farY * farY) + 1f;

        var sb = new StringBuilder();
        sb.AppendOpLine("q");
        sb.AppendOpLine($"{F(x)} {F(y)} {F(width)} {F(height)} re W n");
        string? currentState = null;
        for (int i = 0; i < Sectors; i++)
        {
            float t0 = (float)i / Sectors, t1 = (float)(i + 1) / Sectors;
            var color = stops.ColorAt((t0 + t1) / 2f, repeating);
            float alpha = color.A * opacity;
            if (usedExtGStates != null)
            {
                if (alpha <= 0f) continue;
                string state = PdfGradient.OpacityStateName(alpha);
                if (state != currentState)
                {
                    if (!usedExtGStates.Contains(state)) usedExtGStates.Add(state);
                    sb.AppendOpLine($"/{state} gs");
                    currentState = state;
                }
            }

            float a0 = (startDegrees + t0 * 360f) * DegreesToRadians;
            float a1 = (startDegrees + t1 * 360f) * DegreesToRadians;
            sb.AppendOpLine($"{F(color.R)} {F(color.G)} {F(color.B)} rg");
            sb.AppendOpLine($"{F(cx)} {F(cy)} m {F(cx + radius * (float)Math.Sin(a0))} {F(cy + radius * (float)Math.Cos(a0))} l " +
                            $"{F(cx + radius * (float)Math.Sin(a1))} {F(cy + radius * (float)Math.Cos(a1))} l h f");
        }
        sb.AppendOpLine("Q");
        return sb.ToString();
    }

    private static void ParseFromAndAt(string argument, out float startDegrees, out string? position)
    {
        startDegrees = 0f;
        position = null;
        var text = " " + argument.Trim().ToLowerInvariant();
        int at = text.IndexOf(" at ", StringComparison.Ordinal);
        if (at >= 0)
        {
            position = text.Substring(at + 4);
            text = text.Substring(0, at);
        }
        int from = text.IndexOf(" from ", StringComparison.Ordinal);
        if (from >= 0 && PdfGradient.TryParseAngle(text.Substring(from + 6), out float degrees))
            startDegrees = degrees;
    }

    private static string AnglesAsPercentages(string stop)
    {
        var colorToken = GradientStops.ColorToken(stop);
        var rest = stop.Trim().Substring(colorToken.Length).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new StringBuilder(colorToken);
        foreach (var token in rest)
        {
            result.Append(' ');
            if (PdfGradient.TryParseAngle(token, out float degrees) && token != "0")
                result.Append((degrees / 3.6f).ToString(CultureInfo.InvariantCulture)).Append('%');
            else
                result.Append(token);
        }
        return result.ToString();
    }

    private static string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);
}
