using System;
using System.Collections.Generic;
using System.Globalization;
using EggPdf.Core;

namespace EggPdf.Pdf;

/// <summary>
/// CSS filter effects rendered as PDF ExtGState and color matrix operations.
/// Supports: blur, grayscale, brightness, contrast, sepia, saturate,
/// hue-rotate, invert, opacity, drop-shadow.
///
/// PDF has limited filter support compared to CSS. We approximate:
/// - opacity: via ExtGState ca/CA
/// - grayscale: convert colors to grayscale before painting
/// - blur: approximate with multiple offset copies (limited)
/// - brightness/contrast: adjust color values
/// </summary>
public static class PdfFilterEffects
{
    /// <summary>
    /// Parse CSS filter string and return adjustments to apply to rendering.
    /// Returns a FilterParams object with computed adjustments.
    /// </summary>
    public static FilterParams? Parse(string? filterValue)
    {
        if (string.IsNullOrEmpty(filterValue) || filterValue == "none")
            return null;

        var result = new FilterParams();
        var functions = SplitFilterFunctions(filterValue);

        foreach (var func in functions)
        {
            var name = func.name.ToLowerInvariant();
            switch (name)
            {
                case "opacity":
                    result.Opacity *= func.value;
                    break;
                case "grayscale":
                    result.Grayscale = Math.Min(func.value, 1f);
                    break;
                case "brightness":
                    result.Brightness = func.value;
                    break;
                case "contrast":
                    result.Contrast = func.value;
                    break;
                case "sepia":
                    result.Sepia = Math.Min(func.value, 1f);
                    break;
                case "saturate":
                    result.Saturate = func.value;
                    break;
                case "hue-rotate":
                    result.HueRotateDeg = func.value;
                    break;
                case "invert":
                    result.Invert = Math.Min(func.value, 1f);
                    break;
                case "blur":
                    result.BlurRadius = func.value;
                    break;
                case "drop-shadow":
                    ParseDropShadow(func.rawArgs, result);
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// Apply filter adjustments to an RGB color.
    /// Returns the filtered color values.
    /// </summary>
    public static (float r, float g, float b) ApplyColorFilter(float r, float g, float b, FilterParams filter)
    {
        // Brightness
        if (filter.Brightness != 1f)
        {
            r *= filter.Brightness;
            g *= filter.Brightness;
            b *= filter.Brightness;
        }

        // Contrast
        if (filter.Contrast != 1f)
        {
            r = (r - 0.5f) * filter.Contrast + 0.5f;
            g = (g - 0.5f) * filter.Contrast + 0.5f;
            b = (b - 0.5f) * filter.Contrast + 0.5f;
        }

        // Invert
        if (filter.Invert > 0)
        {
            r = r + (1f - 2f * r) * filter.Invert;
            g = g + (1f - 2f * g) * filter.Invert;
            b = b + (1f - 2f * b) * filter.Invert;
        }

        // Grayscale
        if (filter.Grayscale > 0)
        {
            float gray = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            r = r + (gray - r) * filter.Grayscale;
            g = g + (gray - g) * filter.Grayscale;
            b = b + (gray - b) * filter.Grayscale;
        }

        // Sepia
        if (filter.Sepia > 0)
        {
            float sr = Math.Min(1f, 0.393f * r + 0.769f * g + 0.189f * b);
            float sg = Math.Min(1f, 0.349f * r + 0.686f * g + 0.168f * b);
            float sb = Math.Min(1f, 0.272f * r + 0.534f * g + 0.131f * b);
            r = r + (sr - r) * filter.Sepia;
            g = g + (sg - g) * filter.Sepia;
            b = b + (sb - b) * filter.Sepia;
        }

        // Clamp
        r = Math.Max(0, Math.Min(1, r));
        g = Math.Max(0, Math.Min(1, g));
        b = Math.Max(0, Math.Min(1, b));

        return (r, g, b);
    }

    private static List<(string name, float value, string rawArgs)> SplitFilterFunctions(string filter)
    {
        var result = new List<(string name, float value, string rawArgs)>();
        int i = 0;

        while (i < filter.Length)
        {
            while (i < filter.Length && filter[i] == ' ') i++;
            if (i >= filter.Length) break;

            int nameStart = i;
            while (i < filter.Length && filter[i] != '(') i++;
            if (i >= filter.Length) break;

            string name = filter.Substring(nameStart, i - nameStart).Trim();
            i++; // skip (

            int valStart = i;
            while (i < filter.Length && filter[i] != ')') i++;
            string valStr = filter.Substring(valStart, i - valStart).Trim();
            i++; // skip )

            float value = 1f;
            if (valStr.EndsWith("%"))
            {
                float.TryParse(valStr.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
                value /= 100f;
            }
            else if (valStr.EndsWith("deg"))
            {
                float.TryParse(valStr.Replace("deg", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }
            else if (valStr.EndsWith("px"))
            {
                float.TryParse(valStr.Replace("px", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }
            else
            {
                float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            result.Add((name, value, valStr));
        }

        return result;
    }

    private static void ParseDropShadow(string args, FilterParams result)
    {
        // drop-shadow( <offset-x> <offset-y> [<blur-radius>] [<color>] )
        // tokens are space-separated; color can be a named color, #hex, or rgb()
        var tokens = args.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        float x = 0f, y = 0f, blur = 0f;
        float r = 0f, g = 0f, b = 0f;
        int lengthsFound = 0;

        for (int i = 0; i < tokens.Length; i++)
        {
            var tok = tokens[i];
            float len;
            if (TryParseLength(tok, out len))
            {
                if (lengthsFound == 0) x = len;
                else if (lengthsFound == 1) y = len;
                else if (lengthsFound == 2) blur = len;
                lengthsFound++;
            }
            else
            {
                // Treat as color
                var c = Color.TryParse(tok);
                if (c.HasValue)
                {
                    r = c.Value.R / 255f;
                    g = c.Value.G / 255f;
                    b = c.Value.B / 255f;
                }
            }
        }

        result.DropShadowX = x;
        result.DropShadowY = y;
        result.DropShadowBlur = blur;
        result.DropShadowR = r;
        result.DropShadowG = g;
        result.DropShadowB = b;
    }

    private static bool TryParseLength(string token, out float value)
    {
        if (token.EndsWith("px"))
            return float.TryParse(token.Substring(0, token.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (token.EndsWith("em") || token.EndsWith("rem"))
        {
            // treat as px approximation (1em ≈ 16px) — good enough for shadow offsets
            int unitLen = token.EndsWith("rem") ? 3 : 2;
            if (float.TryParse(token.Substring(0, token.Length - unitLen), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            { value *= 16f; return true; }
        }
        // bare zero
        if (token == "0") { value = 0f; return true; }
        value = 0f;
        return false;
    }
}

/// <summary>Computed filter parameters.</summary>
public class FilterParams
{
    public float Opacity { get; set; } = 1f;
    public float Grayscale { get; set; } = 0f;
    public float Brightness { get; set; } = 1f;
    public float Contrast { get; set; } = 1f;
    public float Sepia { get; set; } = 0f;
    public float Saturate { get; set; } = 1f;
    public float HueRotateDeg { get; set; } = 0f;
    public float Invert { get; set; } = 0f;
    public float BlurRadius { get; set; } = 0f;

    // drop-shadow
    public float DropShadowX { get; set; } = 0f;
    public float DropShadowY { get; set; } = 0f;
    public float DropShadowBlur { get; set; } = 0f;
    public float DropShadowR { get; set; } = 0f;
    public float DropShadowG { get; set; } = 0f;
    public float DropShadowB { get; set; } = 0f;
    public bool HasDropShadow => DropShadowX != 0f || DropShadowY != 0f || DropShadowBlur != 0f;

    public bool HasEffect => Opacity < 1f || Grayscale > 0 || Brightness != 1f ||
        Contrast != 1f || Sepia > 0 || Invert > 0 || BlurRadius > 0 || HasDropShadow;
}
