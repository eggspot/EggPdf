using System;
using System.Globalization;

namespace EggPdf.Layout;

/// <summary>
/// CSS aspect-ratio property support (Chrome 88+).
/// Maintains width/height ratio for elements when one dimension is auto.
/// Example: aspect-ratio: 16/9 with width: 320px → height: 180px.
/// </summary>
public static class AspectRatioLayout
{
    /// <summary>
    /// Parse CSS aspect-ratio value and return width/height ratio.
    /// Formats: "16/9", "1", "auto", "auto 16/9".
    /// Returns null if auto or invalid.
    /// </summary>
    public static float? ParseAspectRatio(string? value)
    {
        if (string.IsNullOrEmpty(value) || value == "auto") return null;

        var trimmed = value.Trim();

        // Handle "auto 16/9" (preferred ratio with auto fallback)
        if (trimmed.StartsWith("auto ", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed.Substring(5).Trim();

        // Handle "16/9" format
        int slashIdx = trimmed.IndexOf('/');
        if (slashIdx > 0)
        {
            if (float.TryParse(trimmed.Substring(0, slashIdx).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float w) &&
                float.TryParse(trimmed.Substring(slashIdx + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float h) &&
                h > 0)
            {
                return w / h;
            }
        }

        // Handle single number (e.g., "1" = 1/1 = square)
        if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float ratio) && ratio > 0)
            return ratio;

        return null;
    }

    /// <summary>
    /// Apply aspect ratio to resolve auto dimension.
    /// If width is specified but height is auto, compute height from ratio.
    /// If height is specified but width is auto, compute width from ratio.
    /// </summary>
    public static (float width, float height) ApplyAspectRatio(
        float? specifiedWidth, float? specifiedHeight, float ratio, float containingWidth)
    {
        if (specifiedWidth.HasValue && !specifiedHeight.HasValue)
        {
            // Width specified, compute height
            return (specifiedWidth.Value, specifiedWidth.Value / ratio);
        }
        if (!specifiedWidth.HasValue && specifiedHeight.HasValue)
        {
            // Height specified, compute width
            return (specifiedHeight.Value * ratio, specifiedHeight.Value);
        }
        if (!specifiedWidth.HasValue && !specifiedHeight.HasValue)
        {
            // Both auto: use containing width and compute height
            return (containingWidth, containingWidth / ratio);
        }
        // Both specified: ratio is ignored
        return (specifiedWidth!.Value, specifiedHeight!.Value);
    }
}
