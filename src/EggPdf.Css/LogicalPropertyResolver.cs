using System.Collections.Generic;

namespace EggPdf.Css;

/// <summary>
/// CSS Logical Properties Level 1 support.
/// Maps logical properties (margin-inline-start, padding-block-end, etc.)
/// to their physical counterparts based on writing direction.
///
/// In LTR horizontal-tb (default):
///   inline-start = left, inline-end = right
///   block-start = top, block-end = bottom
///
/// In RTL: inline-start = right, inline-end = left
/// In vertical-rl: inline = top/bottom, block = right/left
/// </summary>
public static class LogicalPropertyResolver
{
    /// <summary>
    /// Expand logical properties to physical properties in a computed style.
    /// Call after cascade resolution, before layout.
    /// </summary>
    public static void Resolve(ComputedStyle style, bool isRTL = false, bool isVertical = false)
    {
        // Margin logical properties
        MapProperty(style, "margin-inline-start", isRTL ? "margin-right" : "margin-left", isVertical);
        MapProperty(style, "margin-inline-end", isRTL ? "margin-left" : "margin-right", isVertical);
        MapProperty(style, "margin-block-start", "margin-top", isVertical);
        MapProperty(style, "margin-block-end", "margin-bottom", isVertical);
        MapShorthand(style, "margin-inline", isRTL ? "margin-right" : "margin-left", isRTL ? "margin-left" : "margin-right");
        MapShorthand(style, "margin-block", "margin-top", "margin-bottom");

        // Padding logical properties
        MapProperty(style, "padding-inline-start", isRTL ? "padding-right" : "padding-left", isVertical);
        MapProperty(style, "padding-inline-end", isRTL ? "padding-left" : "padding-right", isVertical);
        MapProperty(style, "padding-block-start", "padding-top", isVertical);
        MapProperty(style, "padding-block-end", "padding-bottom", isVertical);
        MapShorthand(style, "padding-inline", isRTL ? "padding-right" : "padding-left", isRTL ? "padding-left" : "padding-right");
        MapShorthand(style, "padding-block", "padding-top", "padding-bottom");

        // Border logical properties (width, color, and style -- all three physical
        // sub-properties a logical border-inline-start/end/block-start/end can imply)
        MapProperty(style, "border-inline-start-width", isRTL ? "border-right-width" : "border-left-width", isVertical);
        MapProperty(style, "border-inline-end-width", isRTL ? "border-left-width" : "border-right-width", isVertical);
        MapProperty(style, "border-block-start-width", "border-top-width", isVertical);
        MapProperty(style, "border-block-end-width", "border-bottom-width", isVertical);
        MapProperty(style, "border-inline-start-color", isRTL ? "border-right-color" : "border-left-color", isVertical);
        MapProperty(style, "border-inline-end-color", isRTL ? "border-left-color" : "border-right-color", isVertical);
        MapProperty(style, "border-block-start-color", "border-top-color", isVertical);
        MapProperty(style, "border-block-end-color", "border-bottom-color", isVertical);
        MapProperty(style, "border-inline-start-style", isRTL ? "border-right-style" : "border-left-style", isVertical);
        MapProperty(style, "border-inline-end-style", isRTL ? "border-left-style" : "border-right-style", isVertical);
        MapProperty(style, "border-block-start-style", "border-top-style", isVertical);
        MapProperty(style, "border-block-end-style", "border-bottom-style", isVertical);

        // Logical border-radius corners. Scoped to horizontal-tb (the common case): a
        // vertical-writing-mode + RTL/LTR combination for corner radii is a genuinely rare
        // pairing and is left unmapped rather than guessed at.
        if (!isVertical)
        {
            MapProperty(style, "border-start-start-radius", isRTL ? "border-top-right-radius" : "border-top-left-radius", false);
            MapProperty(style, "border-start-end-radius", isRTL ? "border-top-left-radius" : "border-top-right-radius", false);
            MapProperty(style, "border-end-start-radius", isRTL ? "border-bottom-right-radius" : "border-bottom-left-radius", false);
            MapProperty(style, "border-end-end-radius", isRTL ? "border-bottom-left-radius" : "border-bottom-right-radius", false);
        }

        // Size logical properties
        MapProperty(style, "inline-size", isVertical ? "height" : "width", false);
        MapProperty(style, "block-size", isVertical ? "width" : "height", false);
        MapProperty(style, "min-inline-size", isVertical ? "min-height" : "min-width", false);
        MapProperty(style, "min-block-size", isVertical ? "min-width" : "min-height", false);
        MapProperty(style, "max-inline-size", isVertical ? "max-height" : "max-width", false);
        MapProperty(style, "max-block-size", isVertical ? "max-width" : "max-height", false);

        // Inset logical properties
        MapProperty(style, "inset-inline-start", isRTL ? "right" : "left", isVertical);
        MapProperty(style, "inset-inline-end", isRTL ? "left" : "right", isVertical);
        MapProperty(style, "inset-block-start", "top", isVertical);
        MapProperty(style, "inset-block-end", "bottom", isVertical);

        // text-align logical values
        var textAlign = style.Get("text-align");
        if (textAlign == "start") style.Set("text-align", isRTL ? "right" : "left");
        else if (textAlign == "end") style.Set("text-align", isRTL ? "left" : "right");

        // float: inline-start/inline-end -- resolved here so every consumer of the `float`
        // property downstream (layout, painting) only ever sees "left"/"right"/"none".
        var floatValue = style.Get("float");
        if (floatValue == "inline-start") style.Set("float", isRTL ? "right" : "left");
        else if (floatValue == "inline-end") style.Set("float", isRTL ? "left" : "right");
    }

    private static void MapProperty(ComputedStyle style, string logicalProp, string physicalProp, bool isVertical)
    {
        var value = style.Get(logicalProp);
        if (!string.IsNullOrEmpty(value) && !style.Has(physicalProp))
        {
            style.Set(physicalProp, value);
        }
    }

    private static void MapShorthand(ComputedStyle style, string shorthand, string start, string end)
    {
        var value = style.Get(shorthand);
        if (!string.IsNullOrEmpty(value))
        {
            if (!style.Has(start)) style.Set(start, value);
            if (!style.Has(end)) style.Set(end, value);
        }
    }
}
