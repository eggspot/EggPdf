using System;
using System.Globalization;

namespace EggPdf.Core;

/// <summary>
/// CMYK color space support for prepress/commercial printing workflows.
/// Converts between RGB and CMYK, handles device-cmyk() CSS function,
/// and generates PDF DeviceCMYK color operators.
/// </summary>
public struct CmykColor
{
    public float C { get; set; } // Cyan 0-1
    public float M { get; set; } // Magenta 0-1
    public float Y { get; set; } // Yellow 0-1
    public float K { get; set; } // Key (Black) 0-1

    public CmykColor(float c, float m, float y, float k)
    {
        C = Math.Max(0, Math.Min(1, c));
        M = Math.Max(0, Math.Min(1, m));
        Y = Math.Max(0, Math.Min(1, y));
        K = Math.Max(0, Math.Min(1, k));
    }

    /// <summary>Convert RGB (0-255) to CMYK.</summary>
    public static CmykColor FromRgb(int r, int g, int b)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float k = 1f - Math.Max(rf, Math.Max(gf, bf));
        if (k >= 1f) return new CmykColor(0, 0, 0, 1);
        float c = (1f - rf - k) / (1f - k);
        float m = (1f - gf - k) / (1f - k);
        float y = (1f - bf - k) / (1f - k);
        return new CmykColor(c, m, y, k);
    }

    /// <summary>Convert CMYK to RGB (0-255).</summary>
    public (int r, int g, int b) ToRgb()
    {
        int r = (int)(255 * (1 - C) * (1 - K));
        int g = (int)(255 * (1 - M) * (1 - K));
        int b = (int)(255 * (1 - Y) * (1 - K));
        return (Math.Max(0, Math.Min(255, r)), Math.Max(0, Math.Min(255, g)), Math.Max(0, Math.Min(255, b)));
    }

    /// <summary>
    /// Parse CSS device-cmyk() function.
    /// Format: device-cmyk(0.2 0.4 0.6 0.1) or device-cmyk(20% 40% 60% 10%)
    /// </summary>
    public static CmykColor? ParseDeviceCmyk(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("device-cmyk(", StringComparison.OrdinalIgnoreCase)) return null;

        var inner = trimmed.Substring(12, trimmed.Length - 13).Trim();
        var parts = inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) return null;

        float c = ParseComponent(parts[0]);
        float m = ParseComponent(parts[1]);
        float y = ParseComponent(parts[2]);
        float k = ParseComponent(parts[3]);

        return new CmykColor(c, m, y, k);
    }

    /// <summary>Generate PDF DeviceCMYK fill color operator.</summary>
    public string ToPdfFillOperator()
        => $"{F(C)} {F(M)} {F(Y)} {F(K)} k";

    /// <summary>Generate PDF DeviceCMYK stroke color operator.</summary>
    public string ToPdfStrokeOperator()
        => $"{F(C)} {F(M)} {F(Y)} {F(K)} K";

    private static float ParseComponent(string value)
    {
        value = value.Trim();
        if (value.EndsWith("%"))
        {
            float.TryParse(value.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct);
            return pct / 100f;
        }
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v);
        return v;
    }

    private static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
}
