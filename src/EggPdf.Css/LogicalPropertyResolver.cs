using System;
using System.Collections.Generic;

namespace EggPdf.Css;

/// <summary>
/// CSS Logical Properties Level 1: maps logical properties (margin-inline-start, padding-block-end, inset-inline,
/// inline-size, ...) to physical ones for the element's direction and writing mode. In horizontal-tb, inline-start is
/// left (right in RTL) and block-start is top; in vertical writing modes (vertical-rl) inline-start is top (bottom in
/// RTL) and block-start is right. The cascade applies each logical declaration in cascade order, so a later logical
/// declaration overrides an earlier physical one and the other way round; <see cref="Resolve"/> then fills in
/// physical properties that are still unset (user-agent styles, and the style resolver without a cascade).
/// </summary>
public static class LogicalPropertyResolver
{
    private static readonly string[] Boxes = { "margin", "padding" };
    private static readonly string[] BorderParts = { "width", "color", "style" };
    private static readonly string[] Sides = { "inline-start", "inline-end", "block-start", "block-end" };
    private static readonly HashSet<string> LogicalNames = BuildLogicalNames();

    /// <summary>
    /// Fill in physical properties from logical ones the element declares, where the physical property is still unset,
    /// and resolve <c>float: inline-start/inline-end</c>. Call after cascade resolution, before layout.
    /// </summary>
    public static void Resolve(ComputedStyle style, bool isRTL = false, bool isVertical = false)
    {
        List<KeyValuePair<string, string>>? logical = null;
        foreach (var kv in style.All)
        {
            if (LogicalNames.Contains(kv.Key))
                (logical ??= new List<KeyValuePair<string, string>>()).Add(kv);
        }
        if (logical != null)
        {
            foreach (var kv in logical)
                Apply(style, kv.Key, kv.Value, isRTL, isVertical, overwrite: false, protectedProperties: null, applied: null);
        }

        var floatValue = style.Get("float");
        if (floatValue == "inline-start") style.Set("float", isRTL ? "right" : "left");
        else if (floatValue == "inline-end") style.Set("float", isRTL ? "left" : "right");
    }

    /// <summary>
    /// The element's text-align as a physical value: <c>start</c>/<c>end</c> (and an unset value in an
    /// RTL context, whose initial value is <c>start</c>) resolve against the element's own direction.
    /// text-align is inherited, so it is never rewritten in the style itself: a <c>dir="rtl"</c> subtree of an LTR
    /// root must still be able to right-align.
    /// </summary>
    public static string? PhysicalTextAlign(ComputedStyle style)
    {
        var value = style.Get("text-align");
        bool rtl = style.Get("direction") == "rtl";
        if (string.IsNullOrEmpty(value)) return rtl ? "right" : value;
        if (value == "start") return rtl ? "right" : "left";
        if (value == "end") return rtl ? "left" : "right";
        return value;
    }

    /// <summary>Whether <paramref name="property"/> is a logical property this resolver maps.</summary>
    internal static bool IsLogical(string property) => LogicalNames.Contains(property);

    /// <summary>
    /// Apply one logical declaration to the physical properties it stands for. With <paramref name="overwrite"/> the
    /// physical values are replaced (the cascade, in declaration order); without it only unset ones are filled.
    /// Properties in <paramref name="protectedProperties"/> are left alone, and every physical property written is
    /// added to <paramref name="applied"/>. Returns false when the property is not logical.
    /// </summary>
    internal static bool Apply(ComputedStyle style, string property, string value, bool isRTL, bool isVertical,
        bool overwrite, ISet<string>? protectedProperties, ICollection<string>? applied)
    {
        if (!LogicalNames.Contains(property)) return false;

        void Set(string physical, string physicalValue)
        {
            if (protectedProperties != null && protectedProperties.Contains(physical)) return;
            if (!overwrite && style.Has(physical)) return;
            if (!CssShorthandExpander.TryExpand(physical, physicalValue, style))
                style.Set(physical, physicalValue);
            applied?.Add(physical);
        }

        foreach (var box in Boxes)
        {
            if (TryMapSide(property, box + "-", "", isRTL, isVertical, out var side)) { Set(box + "-" + side, value); return true; }
            if (property == box + "-inline") { SetPair(value, box + "-" + Physical("inline-start", isRTL, isVertical), box + "-" + Physical("inline-end", isRTL, isVertical), Set); return true; }
            if (property == box + "-block") { SetPair(value, box + "-" + Physical("block-start", isRTL, isVertical), box + "-" + Physical("block-end", isRTL, isVertical), Set); return true; }
        }

        if (TryMapSide(property, "inset-", "", isRTL, isVertical, out var insetSide)) { Set(insetSide, value); return true; }
        if (property == "inset-inline") { SetPair(value, Physical("inline-start", isRTL, isVertical), Physical("inline-end", isRTL, isVertical), Set); return true; }
        if (property == "inset-block") { SetPair(value, Physical("block-start", isRTL, isVertical), Physical("block-end", isRTL, isVertical), Set); return true; }

        foreach (var part in BorderParts)
        {
            if (TryMapSide(property, "border-", "-" + part, isRTL, isVertical, out var borderSide)) { Set("border-" + borderSide + "-" + part, value); return true; }
        }
        if (TryMapSide(property, "border-", "", isRTL, isVertical, out var borderShorthandSide)) { Set("border-" + borderShorthandSide, value); return true; }
        if (property == "border-inline") { Set("border-" + Physical("inline-start", isRTL, isVertical), value); Set("border-" + Physical("inline-end", isRTL, isVertical), value); return true; }
        if (property == "border-block") { Set("border-" + Physical("block-start", isRTL, isVertical), value); Set("border-" + Physical("block-end", isRTL, isVertical), value); return true; }

        switch (property)
        {
            case "border-start-start-radius": Set(Corner("top", isRTL ? "right" : "left", isVertical), value); return true;
            case "border-start-end-radius": Set(Corner("top", isRTL ? "left" : "right", isVertical), value); return true;
            case "border-end-start-radius": Set(Corner("bottom", isRTL ? "right" : "left", isVertical), value); return true;
            case "border-end-end-radius": Set(Corner("bottom", isRTL ? "left" : "right", isVertical), value); return true;
            case "inline-size": Set(isVertical ? "height" : "width", value); return true;
            case "block-size": Set(isVertical ? "width" : "height", value); return true;
            case "min-inline-size": Set(isVertical ? "min-height" : "min-width", value); return true;
            case "min-block-size": Set(isVertical ? "min-width" : "min-height", value); return true;
            case "max-inline-size": Set(isVertical ? "max-height" : "max-width", value); return true;
            case "max-block-size": Set(isVertical ? "max-width" : "max-height", value); return true;
        }
        return false;
    }

    /// <summary>The physical side of a logical side (inline-start, inline-end, block-start, block-end).</summary>
    private static string Physical(string logicalSide, bool isRTL, bool isVertical)
    {
        switch (logicalSide)
        {
            case "inline-start": return isVertical ? (isRTL ? "bottom" : "top") : (isRTL ? "right" : "left");
            case "inline-end": return isVertical ? (isRTL ? "top" : "bottom") : (isRTL ? "left" : "right");
            case "block-start": return isVertical ? "right" : "top";
            default: return isVertical ? "left" : "bottom";
        }
    }

    /// <summary>
    /// The physical corner for a horizontal-tb corner (block side, inline side); in vertical-rl the block axis runs
    /// right to left and the inline axis top to bottom, so the corner turns a quarter.
    /// </summary>
    private static string Corner(string blockSide, string inlineSide, bool isVertical)
    {
        if (!isVertical) return "border-" + blockSide + "-" + inlineSide + "-radius";
        string vertical = inlineSide == "left" ? "top" : "bottom";
        string horizontal = blockSide == "top" ? "right" : "left";
        return "border-" + vertical + "-" + horizontal + "-radius";
    }

    private static bool TryMapSide(string property, string prefix, string suffix, bool isRTL, bool isVertical, out string physicalSide)
    {
        foreach (var side in Sides)
        {
            if (property.Length == prefix.Length + side.Length + suffix.Length &&
                property.StartsWith(prefix, StringComparison.Ordinal) &&
                string.CompareOrdinal(property, prefix.Length, side, 0, side.Length) == 0 &&
                property.EndsWith(suffix, StringComparison.Ordinal))
            {
                physicalSide = Physical(side, isRTL, isVertical);
                return true;
            }
        }
        physicalSide = "";
        return false;
    }

    /// <summary>A two-value logical shorthand: "a" sets start and end to a; "a b" sets start to a and end to b.</summary>
    private static void SetPair(string value, string start, string end, Action<string, string> set)
    {
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && value.IndexOf('(') < 0)
        {
            set(start, parts[0]);
            set(end, parts[1]);
            return;
        }
        set(start, value);
        set(end, value);
    }

    private static HashSet<string> BuildLogicalNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var side in Sides)
        {
            foreach (var box in Boxes) names.Add(box + "-" + side);
            names.Add("inset-" + side);
            names.Add("border-" + side);
            foreach (var part in BorderParts) names.Add("border-" + side + "-" + part);
        }
        foreach (var box in Boxes)
        {
            names.Add(box + "-inline");
            names.Add(box + "-block");
        }
        names.Add("inset-inline"); names.Add("inset-block");
        names.Add("border-inline"); names.Add("border-block");
        names.Add("border-start-start-radius"); names.Add("border-start-end-radius");
        names.Add("border-end-start-radius"); names.Add("border-end-end-radius");
        names.Add("inline-size"); names.Add("block-size");
        names.Add("min-inline-size"); names.Add("min-block-size");
        names.Add("max-inline-size"); names.Add("max-block-size");
        return names;
    }
}
