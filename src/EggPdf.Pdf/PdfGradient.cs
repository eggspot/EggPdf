using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Paints CSS <c>linear-gradient()</c> and <c>repeating-linear-gradient()</c> backgrounds as PDF
/// content: thin bands perpendicular to the gradient line, clipped to the box.
/// </summary>
public static class PdfGradient
{
    private const int MinBands = 32;
    private const int MaxBands = 256;
    private const float PointsPerBand = 1.5f;

    /// <summary>
    /// Parse a CSS linear-gradient() value and return PDF content stream commands that fill the box
    /// (PDF points, bottom-left origin) with it, or null when the value is not a valid gradient.
    /// Translucent stops are drawn opaque; use the overload taking <c>usedExtGStates</c> to keep alpha.
    /// </summary>
    public static string? RenderLinearGradient(string cssGradient, float x, float y, float width, float height) =>
        RenderLinearGradient(cssGradient, x, y, width, height, null);

    /// <summary>
    /// Parse a CSS linear-gradient() value and return PDF content stream commands that fill the box
    /// (PDF points, bottom-left origin) with it, or null when the value is not a valid gradient.
    /// Translucent bands select opacity graphics states; their names are added to
    /// <paramref name="usedExtGStates"/> for the caller to register on the page (see
    /// <see cref="PdfPage.RegisterUsedExtGState"/>). When it is null, bands are drawn opaque.
    /// </summary>
    public static string? RenderLinearGradient(string cssGradient, float x, float y, float width, float height,
        ICollection<string>? usedExtGStates)
    {
        if (string.IsNullOrEmpty(cssGradient) || width <= 0f || height <= 0f) return null;

        var inner = cssGradient.Trim();
        bool repeating;
        if (inner.StartsWith("linear-gradient(", StringComparison.OrdinalIgnoreCase)) repeating = false;
        else if (inner.StartsWith("repeating-linear-gradient(", StringComparison.OrdinalIgnoreCase)) repeating = true;
        else return null;
        int open = inner.IndexOf('(');
        int close = inner.LastIndexOf(')');
        if (close <= open) return null;
        var parts = SplitGradientArgs(inner.Substring(open + 1, close - open - 1));

        float angleDegrees = 180f;
        int firstStop = 0;
        var first = parts[0].Trim();
        if (TryParseAngle(first, out float parsedAngle))
        {
            angleDegrees = parsedAngle;
            firstStop = 1;
        }
        else if (first.StartsWith("to ", StringComparison.OrdinalIgnoreCase))
        {
            angleDegrees = DirectionAngle(first, width, height);
            firstStop = 1;
        }

        double radians = angleDegrees * Math.PI / 180.0;
        float sin = (float)Math.Sin(radians), cos = (float)Math.Cos(radians);
        float lineLength = Math.Abs(width * sin) + Math.Abs(height * cos);
        if (lineLength <= 0f) return null;

        var stops = GradientStops.Parse(parts, firstStop, lineLength);
        if (stops == null) return null;

        float centerX = x + width / 2f, centerY = y + height / 2f;
        float reach = (float)Math.Sqrt(width * width + height * height);
        int bands = Math.Max(MinBands, Math.Min(MaxBands, (int)(lineLength / PointsPerBand)));
        float bandLength = lineLength / bands;

        var sb = new StringBuilder();
        sb.AppendOpLine("q");
        sb.AppendOpLine($"{F(x)} {F(y)} {F(width)} {F(height)} re W n");
        sb.AppendOpLine($"{FMatrix(sin)} {FMatrix(cos)} {FMatrix(-cos)} {FMatrix(sin)} {FMatrix(centerX)} {FMatrix(centerY)} cm");
        string? currentState = null;
        for (int i = 0; i < bands; i++)
        {
            float t = (float)i / (bands - 1);
            var (r, g, b, a) = stops.ColorAt(t, repeating);
            if (a <= 0f && usedExtGStates != null) continue;
            if (usedExtGStates != null)
            {
                string state = OpacityStateName(a);
                if (state != currentState)
                {
                    if (!usedExtGStates.Contains(state)) usedExtGStates.Add(state);
                    sb.AppendOpLine($"/{state} gs");
                    currentState = state;
                }
            }
            float start = -lineLength / 2f + i * bandLength;
            sb.AppendOpLine($"{F(r)} {F(g)} {F(b)} rg");
            sb.AppendOpLine($"{F(start)} {F(-reach)} {F(bandLength + 0.1f)} {F(2f * reach)} re f");
        }
        sb.AppendOpLine("Q");
        return sb.ToString();
    }

    /// <summary>
    /// Render a CSS repeating-radial-gradient() as PDF content stream commands.
    /// Approximated as radial gradient bands (same as radial-gradient, patterns not repeated).
    /// </summary>
    public static string? RenderRepeatingRadialGradient(string cssGradient, float x, float y, float width, float height)
    {
        if (string.IsNullOrEmpty(cssGradient)) return null;
        var remapped = "radial-gradient(" +
            cssGradient.Substring("repeating-radial-gradient(".Length);
        return PdfRadialGradient.Render(remapped, x, y, width, height);
    }

    private static string OpacityStateName(float alpha) =>
        "GS" + ((int)Math.Round(Math.Max(0f, Math.Min(1f, alpha)) * 100f)).ToString(CultureInfo.InvariantCulture);

    private static bool TryParseAngle(string value, out float degrees)
    {
        degrees = 0f;
        value = value.Trim().ToLowerInvariant();
        float factor;
        string number;
        if (value.EndsWith("deg", StringComparison.Ordinal)) { factor = 1f; number = value.Substring(0, value.Length - 3); }
        else if (value.EndsWith("grad", StringComparison.Ordinal)) { factor = 0.9f; number = value.Substring(0, value.Length - 4); }
        else if (value.EndsWith("rad", StringComparison.Ordinal)) { factor = (float)(180.0 / Math.PI); number = value.Substring(0, value.Length - 3); }
        else if (value.EndsWith("turn", StringComparison.Ordinal)) { factor = 360f; number = value.Substring(0, value.Length - 4); }
        else if (value == "0") { return true; }
        else return false;
        if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)) return false;
        degrees = parsed * factor;
        return true;
    }

    private static float DirectionAngle(string direction, float width, float height)
    {
        direction = direction.Trim().ToLowerInvariant();
        int horizontal = direction.IndexOf("right", StringComparison.Ordinal) >= 0 ? 1
            : direction.IndexOf("left", StringComparison.Ordinal) >= 0 ? -1 : 0;
        int vertical = direction.IndexOf("bottom", StringComparison.Ordinal) >= 0 ? 1
            : direction.IndexOf("top", StringComparison.Ordinal) >= 0 ? -1 : 0;
        if (horizontal != 0 && vertical != 0)
            return (float)(Math.Atan2(horizontal * height, -vertical * width) * 180.0 / Math.PI);
        if (horizontal > 0) return 90f;
        if (horizontal < 0) return 270f;
        if (vertical < 0) return 0f;
        return 180f;
    }

    internal static List<string> SplitGradientArgs(string args)
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

    internal static (float r, float g, float b) ParseSimpleColor(string color)
    {
        var parsed = EggPdf.Core.Color.TryParse(GradientStops.ColorToken(color));
        if (parsed.HasValue)
            return (parsed.Value.R / 255f, parsed.Value.G / 255f, parsed.Value.B / 255f);

        return (0, 0, 0);
    }

    private static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);

    private static string FMatrix(float v) =>
        Math.Abs(v) < 5e-7f ? "0" : v.ToString("0.######", CultureInfo.InvariantCulture);
}
