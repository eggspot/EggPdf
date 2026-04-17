using System;
using EggPdf.Css;

namespace EggPdf.Layout;

/// <summary>
/// CSS Writing Modes Level 4 support.
/// Handles writing-mode: horizontal-tb (default), vertical-rl, vertical-lr.
/// Used for CJK vertical text and RTL/vertical layouts.
/// </summary>
public static class WritingModeLayout
{
    /// <summary>Writing mode values.</summary>
    public enum WritingMode
    {
        HorizontalTb, // Default: left-to-right, top-to-bottom
        VerticalRl,    // Top-to-bottom, right-to-left (CJK traditional)
        VerticalLr,    // Top-to-bottom, left-to-right (Mongolian)
    }

    /// <summary>Parse writing-mode CSS value.</summary>
    public static WritingMode Parse(string? value)
    {
        switch (value?.ToLowerInvariant())
        {
            case "vertical-rl": return WritingMode.VerticalRl;
            case "vertical-lr": return WritingMode.VerticalLr;
            default: return WritingMode.HorizontalTb;
        }
    }

    /// <summary>Check if a writing mode is vertical.</summary>
    public static bool IsVertical(WritingMode mode)
        => mode == WritingMode.VerticalRl || mode == WritingMode.VerticalLr;

    /// <summary>
    /// For vertical writing, swap width/height of the containing block
    /// and rotate text 90 degrees in the content stream.
    /// Returns the PDF transformation matrix for vertical text.
    /// </summary>
    public static string GetVerticalTextTransform(float x, float y, float fontSize)
    {
        // Rotate 90° clockwise: matrix [0 -1 1 0 x y]
        return $"0 -1 1 0 {F(x)} {F(y)} cm";
    }

    /// <summary>
    /// For vertical writing, swap inline/block dimensions.
    /// In vertical mode: width becomes block size, height becomes inline size.
    /// </summary>
    public static (float inlineSize, float blockSize) ResolveLogicalSizes(
        float width, float height, WritingMode mode)
    {
        if (IsVertical(mode))
            return (height, width); // inline = height, block = width
        return (width, height);     // inline = width, block = height
    }

    /// <summary>Get text-orientation for vertical text.</summary>
    public static string ResolveTextOrientation(string? value)
    {
        switch (value?.ToLowerInvariant())
        {
            case "upright": return "upright";    // Each character upright
            case "sideways": return "sideways";  // All characters rotated
            default: return "mixed";             // CJK upright, Latin sideways
        }
    }

    /// <summary>
    /// Returns true if text-combine-upright should be applied (value is "all" or "digits [N]").
    /// text-combine-upright: all — combine all typographic characters into one unit (tate-chu-yoko).
    /// </summary>
    public static bool ResolveCombineUpright(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var v = value.Trim().ToLowerInvariant();
        return v == "all" || v.StartsWith("digits", StringComparison.Ordinal);
    }

    /// <summary>
    /// Get the PDF transformation matrix to horizontally scale text to fit within one em.
    /// Used with text-combine-upright: compresses multi-char text to occupy a single character slot.
    /// Returns the cm operator string, or null if no scaling needed.
    /// </summary>
    public static string? GetCombineUprightTransform(float textWidth, float emSize, float x, float y)
    {
        if (emSize <= 0f || textWidth <= 0f || textWidth <= emSize)
            return null;

        // Horizontal scale factor: em / textWidth — squish to fit in 1em
        float sx = emSize / textWidth;
        // Scale matrix [sx 0 0 1 tx ty]: scale X, leave Y unchanged
        // tx adjusted so text is centered: shift right by (textWidth - emSize)/2 * ... but simpler: just scale from current x
        float tx = x * (1f - sx);
        return $"{F(sx)} 0 0 1 {F(tx)} 0 cm";
    }

    private static string F(float v) => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
}
