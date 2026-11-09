using System;
using System.Collections.Generic;
using System.Text;
using EggPdf.Core;

namespace EggPdf.Pdf;

/// <summary>
/// Paints CSS <c>radial-gradient()</c> and <c>repeating-radial-gradient()</c> backgrounds as PDF content:
/// concentric bands around the gradient centre, clipped to the box. Supports circle and ellipse shapes,
/// the size keywords and explicit radii, <c>at &lt;position&gt;</c>, stop positions and translucent stops.
/// </summary>
public static class PdfRadialGradient
{
    private const int MinBands = 32;
    private const int MaxBands = 256;
    private const float PointsPerBand = 1.5f;
    private const float MinRadius = 0.01f;
    private static readonly float Sqrt2 = (float)Math.Sqrt(2.0);

    /// <summary>
    /// Parse a CSS radial-gradient() or repeating-radial-gradient() value and return PDF content stream commands
    /// that fill the box (PDF points, bottom-left origin), or null when the value is not a valid gradient.
    /// Translucent stops are drawn opaque; use the overload taking <c>usedExtGStates</c> to keep alpha.
    /// </summary>
    public static string? Render(string cssGradient, float x, float y, float width, float height) =>
        Render(cssGradient, x, y, width, height, null);

    /// <summary>
    /// Parse a CSS radial-gradient() or repeating-radial-gradient() value and return PDF content stream commands
    /// that fill the box (PDF points, bottom-left origin), or null when the value is not a valid gradient.
    /// Translucent bands select opacity graphics states whose names are added to <paramref name="usedExtGStates"/>
    /// (when it is null, bands are drawn opaque); <paramref name="opacity"/> (0-1) multiplies every band's alpha.
    /// </summary>
    public static string? Render(string cssGradient, float x, float y, float width, float height,
        ICollection<string>? usedExtGStates, float opacity = 1f)
    {
        if (string.IsNullOrEmpty(cssGradient) || width <= 0f || height <= 0f) return null;

        var value = cssGradient.Trim();
        bool repeating;
        if (value.StartsWith("radial-gradient(", StringComparison.OrdinalIgnoreCase)) repeating = false;
        else if (value.StartsWith("repeating-radial-gradient(", StringComparison.OrdinalIgnoreCase)) repeating = true;
        else return null;
        int open = value.IndexOf('(');
        int close = value.LastIndexOf(')');
        if (close <= open) return null;
        var parts = PdfGradient.SplitGradientArgs(value.Substring(open + 1, close - open - 1));

        int firstStop = 0;
        string? shapeArgument = null;
        if (!IsColorStop(parts[0]))
        {
            shapeArgument = parts[0];
            firstStop = 1;
        }
        var (fromLeft, fromTop, rx, ry) = EndingShape(shapeArgument, width, height);

        var stops = GradientStops.Parse(parts, firstStop, rx);
        if (stops == null) return null;

        float centerX = x + fromLeft, centerY = y + height - fromTop;
        float reach = FarthestCornerScale(fromLeft, fromTop, width, height, rx, ry);
        float extent = repeating ? reach : 1f;
        int bands = Math.Max(MinBands, Math.Min(MaxBands, (int)(Math.Max(rx, ry) * extent / PointsPerBand)));
        bool translucent = usedExtGStates != null && (opacity < 1f || stops.HasTranslucentStop);

        var sb = new StringBuilder();
        sb.AppendOpLine("q");
        sb.AppendOpLine($"{GradientGeometry.F(x)} {GradientGeometry.F(y)} {GradientGeometry.F(width)} {GradientGeometry.F(height)} re W n");
        string? currentState = null;

        if (!repeating && reach > 1f)
        {
            var outside = stops.ColorAt(1f, false);
            if (SelectFill(sb, outside, opacity, usedExtGStates, ref currentState))
            {
                sb.AppendOpLine($"{GradientGeometry.F(x)} {GradientGeometry.F(y)} {GradientGeometry.F(width)} {GradientGeometry.F(height)} re");
                if (translucent)
                {
                    GradientGeometry.AppendEllipse(sb, centerX, centerY, rx, ry);
                    sb.AppendOpLine("f*");
                }
                else
                {
                    sb.AppendOpLine("f");
                }
            }
        }

        for (int i = bands - 1; i >= 0; i--)
        {
            float inner = extent * i / bands;
            float outer = extent * (i + 1) / bands;
            var color = stops.ColorAt((inner + outer) / 2f, repeating);
            if (!SelectFill(sb, color, opacity, usedExtGStates, ref currentState)) continue;
            GradientGeometry.AppendEllipse(sb, centerX, centerY, Math.Max(MinRadius, rx * outer), Math.Max(MinRadius, ry * outer));
            if (translucent && i > 0)
            {
                GradientGeometry.AppendEllipse(sb, centerX, centerY, rx * inner, ry * inner);
                sb.AppendOpLine("f*");
            }
            else
            {
                sb.AppendOpLine("f");
            }
        }

        sb.AppendOpLine("Q");
        return sb.ToString();
    }

    private static bool SelectFill(StringBuilder sb, (float R, float G, float B, float A) color, float opacity,
        ICollection<string>? usedExtGStates, ref string? currentState)
    {
        float alpha = color.A * opacity;
        if (usedExtGStates != null)
        {
            if (alpha <= 0f) return false;
            string state = PdfGradient.OpacityStateName(alpha);
            if (state != currentState)
            {
                if (!usedExtGStates.Contains(state)) usedExtGStates.Add(state);
                sb.AppendOpLine($"/{state} gs");
                currentState = state;
            }
        }
        sb.AppendOpLine($"{GradientGeometry.F(color.R)} {GradientGeometry.F(color.G)} {GradientGeometry.F(color.B)} rg");
        return true;
    }

    private static bool IsColorStop(string part) =>
        Color.TryParse(GradientStops.ColorToken(part)).HasValue;

    private static (float FromLeft, float FromTop, float Rx, float Ry) EndingShape(string? argument, float width, float height)
    {
        string sizePart = "", positionPart = "";
        if (!string.IsNullOrEmpty(argument))
        {
            var text = " " + argument!.Trim().ToLowerInvariant();
            int at = text.IndexOf(" at ", StringComparison.Ordinal);
            sizePart = at >= 0 ? text.Substring(0, at) : text;
            positionPart = at >= 0 ? text.Substring(at + 4) : "";
        }

        var (fromLeft, fromTop) = GradientGeometry.ParsePosition(positionPart, width, height);
        float left = fromLeft, right = width - fromLeft, top = fromTop, bottom = height - fromTop;

        bool circle = false, ellipse = false;
        string sizeKeyword = "farthest-corner";
        var lengths = new List<string>();
        foreach (var token in sizePart.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == "circle") circle = true;
            else if (token == "ellipse") ellipse = true;
            else if (token == "contain") sizeKeyword = "closest-side";
            else if (token == "cover") sizeKeyword = "farthest-corner";
            else if (token.IndexOf("side", StringComparison.Ordinal) >= 0 || token.IndexOf("corner", StringComparison.Ordinal) >= 0) sizeKeyword = token;
            else lengths.Add(token);
        }
        if (!ellipse && lengths.Count == 1) circle = true;

        float rx, ry;
        if (lengths.Count >= 1)
        {
            rx = GradientGeometry.Length(lengths[0], width);
            ry = circle ? rx : lengths.Count >= 2 ? GradientGeometry.Length(lengths[1], height) : rx;
        }
        else if (circle)
        {
            float closestSide = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
            float farthestSide = Math.Max(Math.Max(left, right), Math.Max(top, bottom));
            float closestCorner = Distance(Math.Min(left, right), Math.Min(top, bottom));
            float farthestCorner = Distance(Math.Max(left, right), Math.Max(top, bottom));
            rx = ry = sizeKeyword switch
            {
                "closest-side" => closestSide,
                "farthest-side" => farthestSide,
                "closest-corner" => closestCorner,
                _ => farthestCorner
            };
        }
        else
        {
            float closeX = Math.Min(left, right), closeY = Math.Min(top, bottom);
            float farX = Math.Max(left, right), farY = Math.Max(top, bottom);
            (rx, ry) = sizeKeyword switch
            {
                "closest-side" => (closeX, closeY),
                "farthest-side" => (farX, farY),
                "closest-corner" => (closeX * Sqrt2, closeY * Sqrt2),
                _ => (farX * Sqrt2, farY * Sqrt2)
            };
        }
        return (fromLeft, fromTop, Math.Max(MinRadius, rx), Math.Max(MinRadius, ry));
    }

    private static float FarthestCornerScale(float fromLeft, float fromTop, float width, float height, float rx, float ry)
    {
        float dx = Math.Max(fromLeft, width - fromLeft) / rx;
        float dy = Math.Max(fromTop, height - fromTop) / ry;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    private static float Distance(float dx, float dy) => (float)Math.Sqrt(dx * dx + dy * dy);
}
