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

    private static string F(float v) => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
}
