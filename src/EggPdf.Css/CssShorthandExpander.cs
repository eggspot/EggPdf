using System;

namespace EggPdf.Css;

/// <summary>
/// Expands CSS shorthand properties into their longhand equivalents.
/// Handles margin, padding, border, and background shorthands.
/// </summary>
public static class CssShorthandExpander
{
    /// <summary>
    /// Expand a property+value pair into a computed style.
    /// Returns true if the property was a shorthand and was expanded.
    /// </summary>
    public static bool TryExpand(string property, string value, ComputedStyle style)
    {
        switch (property)
        {
            case "margin":
                ExpandBoxShorthand(value, "margin", style);
                return true;
            case "padding":
                ExpandBoxShorthand(value, "padding", style);
                return true;
            case "border":
                ExpandBorderShorthand(value, style);
                return true;
            case "border-width":
                ExpandBoxShorthand(value, "border", style, "-width");
                return true;
            case "border-style":
                ExpandBoxShorthand(value, "border", style, "-style");
                return true;
            case "border-color":
                ExpandBoxShorthand(value, "border", style, "-color");
                return true;
            case "background":
                ExpandBackgroundShorthand(value, style);
                return true;
            case "font":
                ExpandFontShorthand(value, style);
                return true;
            case "list-style":
                ExpandListStyleShorthand(value, style);
                return true;
            case "flex":
                ExpandFlexShorthand(value, style);
                return true;
            case "border-top":
            case "border-right":
            case "border-bottom":
            case "border-left":
                ExpandBorderSideShorthand(value, property, style);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Expand margin/padding shorthand: 1-4 values to top/right/bottom/left.
    /// CSS spec: 1 value = all, 2 = vert horiz, 3 = top horiz bottom, 4 = top right bottom left
    /// </summary>
    private static void ExpandBoxShorthand(string value, string prefix, ComputedStyle style, string suffix = "")
    {
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        string top, right, bottom, left;

        switch (parts.Length)
        {
            case 1:
                top = right = bottom = left = parts[0];
                break;
            case 2:
                top = bottom = parts[0];
                right = left = parts[1];
                break;
            case 3:
                top = parts[0];
                right = left = parts[1];
                bottom = parts[2];
                break;
            default: // 4+
                top = parts[0];
                right = parts[1];
                bottom = parts[2];
                left = parts[3];
                break;
        }

        style.Set($"{prefix}-top{suffix}", top);
        style.Set($"{prefix}-right{suffix}", right);
        style.Set($"{prefix}-bottom{suffix}", bottom);
        style.Set($"{prefix}-left{suffix}", left);
    }

    /// <summary>Expand border shorthand: "1px solid red" -> width + style + color for all sides.</summary>
    private static void ExpandBorderShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string? width = null, borderStyle = null, color = null;

        foreach (var part in parts)
        {
            if (IsBorderStyle(part))
                borderStyle = part;
            else if (IsBorderWidth(part))
                width = NormalizeBorderWidth(part);
            else
                color = part;
        }

        if (width != null)
        {
            style.Set("border-top-width", width);
            style.Set("border-right-width", width);
            style.Set("border-bottom-width", width);
            style.Set("border-left-width", width);
        }
        if (borderStyle != null)
        {
            style.Set("border-top-style", borderStyle);
            style.Set("border-right-style", borderStyle);
            style.Set("border-bottom-style", borderStyle);
            style.Set("border-left-style", borderStyle);
        }
        if (color != null)
        {
            style.Set("border-top-color", color);
            style.Set("border-right-color", color);
            style.Set("border-bottom-color", color);
            style.Set("border-left-color", color);
        }
    }

    /// <summary>Expand background shorthand: just extract color for now.</summary>
    private static void ExpandBackgroundShorthand(string value, ComputedStyle style)
    {
        // Simple: treat the whole value as background-color
        // Full parsing of background shorthand (image, position, repeat) deferred
        style.Set("background-color", value.Trim());
    }

    private static bool IsBorderStyle(string part)
    {
        return part == "solid" || part == "dashed" || part == "dotted" || part == "double" ||
               part == "groove" || part == "ridge" || part == "inset" || part == "outset" || part == "none";
    }

    private static bool IsBorderWidth(string part)
    {
        return part.EndsWith("px") || part.EndsWith("em") || part.EndsWith("pt") ||
               part == "thin" || part == "medium" || part == "thick";
    }

    private static string NormalizeBorderWidth(string part)
    {
        if (part == "thin") return "1px";
        if (part == "medium") return "3px";
        if (part == "thick") return "5px";
        return part;
    }

    /// <summary>
    /// Expand font shorthand: "bold 14px/1.5 Arial, sans-serif"
    /// Format: [style] [variant] [weight] [stretch] size[/line-height] family
    /// </summary>
    private static void ExpandFontShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        int i = 0;

        // Parse optional style, variant, weight (before size)
        while (i < parts.Length)
        {
            var part = parts[i].ToLowerInvariant();
            if (part == "italic" || part == "oblique")
            {
                style.Set("font-style", part);
                i++;
            }
            else if (part == "small-caps")
            {
                style.Set("font-variant", part);
                i++;
            }
            else if (part == "bold" || part == "bolder" || part == "lighter" || part == "normal" ||
                     (part.Length <= 3 && int.TryParse(part, out int w) && w >= 100 && w <= 900))
            {
                style.Set("font-weight", part);
                i++;
            }
            else
            {
                break; // must be size
            }
        }

        // Parse size (required) and optional /line-height
        if (i < parts.Length)
        {
            var sizePart = parts[i];
            int slashIdx = sizePart.IndexOf('/');
            if (slashIdx >= 0)
            {
                style.Set("font-size", sizePart.Substring(0, slashIdx));
                style.Set("line-height", sizePart.Substring(slashIdx + 1));
            }
            else
            {
                style.Set("font-size", sizePart);
            }
            i++;
        }

        // Remaining parts are font-family (may contain commas)
        if (i < parts.Length)
        {
            var family = string.Join(" ", parts, i, parts.Length - i);
            style.Set("font-family", family.Trim().Trim(','));
        }
    }

    /// <summary>Expand list-style shorthand: "disc outside" -> type + position.</summary>
    private static void ExpandListStyleShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part == "inside" || part == "outside")
                style.Set("list-style-position", part);
            else if (part == "none")
                style.Set("list-style-type", "none");
            else
                style.Set("list-style-type", part);
        }
    }

    /// <summary>Expand flex shorthand: "1 0 auto" -> grow + shrink + basis.</summary>
    private static void ExpandFlexShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1) style.Set("flex-grow", parts[0]);
        if (parts.Length >= 2) style.Set("flex-shrink", parts[1]);
        if (parts.Length >= 3) style.Set("flex-basis", parts[2]);
    }

    /// <summary>Expand border-top/right/bottom/left: "1px solid red" -> width + style + color for one side.</summary>
    private static void ExpandBorderSideShorthand(string value, string property, ComputedStyle style)
    {
        // property is "border-top", "border-right", etc.
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string? width = null, borderStyle = null, color = null;

        foreach (var part in parts)
        {
            if (IsBorderStyle(part))
                borderStyle = part;
            else if (IsBorderWidth(part))
                width = NormalizeBorderWidth(part);
            else
                color = part;
        }

        if (width != null) style.Set(property + "-width", width);
        if (borderStyle != null) style.Set(property + "-style", borderStyle);
        if (color != null) style.Set(property + "-color", color);
    }
}
