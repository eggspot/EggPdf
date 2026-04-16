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
            case "flex-flow":
                ExpandFlexFlowShorthand(value, style);
                return true;
            case "border-top":
            case "border-right":
            case "border-bottom":
            case "border-left":
                ExpandBorderSideShorthand(value, property, style);
                return true;
            case "outline":
                ExpandOutlineShorthand(value, style);
                return true;
            case "border-radius":
                ExpandBorderRadiusShorthand(value, style);
                return true;
            case "text-decoration":
                ExpandTextDecorationShorthand(value, style);
                return true;
            case "place-items":
                ExpandTwoPartShorthand(value, "align-items", "justify-items", style);
                return true;
            case "place-self":
                ExpandTwoPartShorthand(value, "align-self", "justify-self", style);
                return true;
            case "place-content":
                ExpandTwoPartShorthand(value, "align-content", "justify-content", style);
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

    /// <summary>
    /// Expand background shorthand into individual longhand properties.
    /// Handles: background-image (url() or gradient), background-repeat,
    /// background-position, background-size (after /), and background-color.
    /// The color, if present, must appear last per the CSS spec.
    /// </summary>
    private static void ExpandBackgroundShorthand(string value, ComputedStyle style)
    {
        var trimmed = value.Trim();

        // "none" keyword — clear the image, leave color as transparent
        if (trimmed == "none")
        {
            style.Set("background-image", "none");
            return;
        }

        // Extract url(...) or gradient functions first (they may contain spaces/commas)
        string? image = null;
        string remaining = trimmed;

        int urlIdx = remaining.IndexOf("url(", StringComparison.OrdinalIgnoreCase);
        if (urlIdx >= 0)
        {
            int closeIdx = remaining.IndexOf(')', urlIdx);
            if (closeIdx >= 0)
            {
                image = remaining.Substring(urlIdx, closeIdx - urlIdx + 1);
                remaining = remaining.Substring(0, urlIdx) + remaining.Substring(closeIdx + 1);
            }
        }
        else
        {
            // Check for CSS gradient functions
            foreach (var fn in new[] { "linear-gradient(", "radial-gradient(", "conic-gradient(" })
            {
                int fnIdx = remaining.IndexOf(fn, StringComparison.OrdinalIgnoreCase);
                if (fnIdx < 0) continue;

                // Find matching closing paren (may contain nested parens)
                int depth = 0;
                int end = fnIdx;
                for (int i = fnIdx; i < remaining.Length; i++)
                {
                    if (remaining[i] == '(') depth++;
                    else if (remaining[i] == ')') { depth--; if (depth == 0) { end = i; break; } }
                }
                image = remaining.Substring(fnIdx, end - fnIdx + 1);
                remaining = remaining.Substring(0, fnIdx) + remaining.Substring(end + 1);
                break;
            }
        }

        if (image != null)
            style.Set("background-image", image.Trim());

        // Now tokenize the remainder by spaces
        var parts = remaining.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        string? position = null;
        string? size = null;
        string? repeat = null;
        string? color = null;

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim('/');
            var raw  = parts[i];

            // position/size split by slash: "center/cover" or "50%/contain"
            int slashIdx = raw.IndexOf('/');
            if (slashIdx >= 0)
            {
                position = raw.Substring(0, slashIdx);
                size     = raw.Substring(slashIdx + 1);
                continue;
            }

            if (IsBackgroundRepeat(part))  { repeat = part; continue; }
            if (IsBackgroundPosition(part)) { position = position == null ? part : position + " " + part; continue; }
            if (IsBackgroundSize(part))    { size = part; continue; }

            // Anything else at the end treated as color
            color = part;
        }

        if (repeat   != null) style.Set("background-repeat", repeat);
        if (position != null) style.Set("background-position", position);
        if (size     != null) style.Set("background-size", size);
        if (color    != null) style.Set("background-color", color);
    }

    private static bool IsBackgroundRepeat(string part)
    {
        return part == "repeat" || part == "no-repeat" || part == "repeat-x" ||
               part == "repeat-y" || part == "round" || part == "space";
    }

    private static bool IsBackgroundPosition(string part)
    {
        return part == "center" || part == "top" || part == "bottom" ||
               part == "left"   || part == "right" ||
               part.EndsWith("%") || part.EndsWith("px") || part.EndsWith("em");
    }

    private static bool IsBackgroundSize(string part)
    {
        return part == "cover" || part == "contain" || part == "auto";
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

    /// <summary>
    /// Expand flex shorthand per CSS Flexible Box Layout spec §7.2.
    /// <list type="bullet">
    ///   <item>flex: none        → 0 0 auto</item>
    ///   <item>flex: auto        → 1 1 auto</item>
    ///   <item>flex: &lt;n&gt;        → flex-grow:n, flex-shrink:1, flex-basis:0</item>
    ///   <item>flex: &lt;g&gt; &lt;s&gt;    → flex-grow:g, flex-shrink:s, flex-basis:0</item>
    ///   <item>flex: &lt;g&gt; &lt;basis&gt; → flex-grow:g, flex-shrink:1, flex-basis:basis</item>
    ///   <item>flex: &lt;g&gt; &lt;s&gt; &lt;b&gt; → all three set explicitly</item>
    /// </list>
    /// </summary>
    private static void ExpandFlexShorthand(string value, ComputedStyle style)
    {
        // Keywords
        if (value == "none")  { style.Set("flex-grow", "0"); style.Set("flex-shrink", "0"); style.Set("flex-basis", "auto"); return; }
        if (value == "auto")  { style.Set("flex-grow", "1"); style.Set("flex-shrink", "1"); style.Set("flex-basis", "auto"); return; }
        if (value == "initial") { style.Set("flex-grow", "0"); style.Set("flex-shrink", "1"); style.Set("flex-basis", "auto"); return; }

        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 1)
        {
            // Single value: unitless number → flex-grow, flex-shrink=1, flex-basis=0
            // Otherwise treat as flex-basis with grow=1, shrink=1
            if (float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                style.Set("flex-grow", parts[0]);
                style.Set("flex-shrink", "1");
                style.Set("flex-basis", "0");
            }
            else
            {
                style.Set("flex-grow", "1");
                style.Set("flex-shrink", "1");
                style.Set("flex-basis", parts[0]);
            }
        }
        else if (parts.Length == 2)
        {
            bool p1IsNumber = float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _);

            style.Set("flex-grow", parts[0]);
            if (p1IsNumber)
            {
                // flex: <grow> <shrink> → flex-basis:0
                style.Set("flex-shrink", parts[1]);
                style.Set("flex-basis", "0");
            }
            else
            {
                // flex: <grow> <basis>
                style.Set("flex-shrink", "1");
                style.Set("flex-basis", parts[1]);
            }
        }
        else
        {
            // 3 values: flex-grow flex-shrink flex-basis
            style.Set("flex-grow", parts[0]);
            style.Set("flex-shrink", parts[1]);
            style.Set("flex-basis", parts[2]);
        }
    }

    /// <summary>
    /// Expand flex-flow shorthand: "flex-direction flex-wrap".
    /// Each token is classified as a direction keyword or a wrap keyword; defaults are "row" and "nowrap".
    /// </summary>
    private static void ExpandFlexFlowShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string direction = "row";
        string wrap = "nowrap";

        foreach (var part in parts)
        {
            if (part == "row" || part == "column" || part == "row-reverse" || part == "column-reverse")
                direction = part;
            else if (part == "wrap" || part == "nowrap" || part == "wrap-reverse")
                wrap = part;
        }

        style.Set("flex-direction", direction);
        style.Set("flex-wrap", wrap);
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

    /// <summary>Expand outline shorthand: "2px solid blue" -> outline-width + style + color.</summary>
    private static void ExpandOutlineShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string? width = null, outlineStyle = null, color = null;

        foreach (var part in parts)
        {
            if (IsBorderStyle(part))
                outlineStyle = part;
            else if (IsBorderWidth(part))
                width = NormalizeBorderWidth(part);
            else
                color = part;
        }

        if (width != null) style.Set("outline-width", width);
        if (outlineStyle != null) style.Set("outline-style", outlineStyle);
        if (color != null) style.Set("outline-color", color);
    }

    /// <summary>
    /// Expand text-decoration shorthand: "underline dashed red 2px"
    /// → text-decoration-line, text-decoration-style, text-decoration-color, text-decoration-thickness.
    /// </summary>
    private static void ExpandTextDecorationShorthand(string value, ComputedStyle style)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (value == "none") { style.Set("text-decoration-line", "none"); style.Set("text-decoration", "none"); return; }

        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string? line = null, decoStyle = null, color = null, thickness = null;

        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower == "underline" || lower == "overline" || lower == "line-through" || lower == "blink")
                line = (line == null) ? part : line + " " + part;
            else if (lower == "solid" || lower == "double" || lower == "dotted" || lower == "dashed" || lower == "wavy")
                decoStyle = part;
            else if (lower == "auto" || lower == "from-font")
                thickness = part;
            else if (IsLength(part) || part.EndsWith("px") || part.EndsWith("em"))
                thickness = part;
            else
                color = part;
        }

        if (line != null) style.Set("text-decoration-line", line);
        if (decoStyle != null) style.Set("text-decoration-style", decoStyle);
        if (color != null) style.Set("text-decoration-color", color);
        if (thickness != null) style.Set("text-decoration-thickness", thickness);

        // Keep the original value on text-decoration for backward compat
        style.Set("text-decoration", value);
    }

    private static bool IsLength(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (s.EndsWith("px") || s.EndsWith("em") || s.EndsWith("rem") ||
            s.EndsWith("pt") || s.EndsWith("cm") || s.EndsWith("mm") || s.EndsWith("%"))
            return true;
        return float.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    /// <summary>
    /// Expand border-radius shorthand.
    /// CSS spec: 1-4 values before optional '/' map to horizontal radii;
    /// 1-4 values after '/' map to vertical radii.
    /// Each corner property receives both values as "Xpx Ypx" when vertical radii differ.
    /// </summary>
    private static void ExpandBorderRadiusShorthand(string value, ComputedStyle style)
    {
        var slashIdx = value.IndexOf('/');
        var hPart = slashIdx >= 0 ? value.Substring(0, slashIdx).Trim() : value.Trim();
        var vPart = slashIdx >= 0 ? value.Substring(slashIdx + 1).Trim() : null;

        var hParts = hPart.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (hParts.Length == 0) return;

        string tlH, trH, brH, blH;
        switch (hParts.Length)
        {
            case 1:  tlH = trH = brH = blH = hParts[0]; break;
            case 2:  tlH = brH = hParts[0]; trH = blH = hParts[1]; break;
            case 3:  tlH = hParts[0]; trH = blH = hParts[1]; brH = hParts[2]; break;
            default: tlH = hParts[0]; trH = hParts[1]; brH = hParts[2]; blH = hParts[3]; break;
        }

        if (vPart != null && vPart.Length > 0)
        {
            var vParts = vPart.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string tlV, trV, brV, blV;
            switch (vParts.Length)
            {
                case 1:  tlV = trV = brV = blV = vParts[0]; break;
                case 2:  tlV = brV = vParts[0]; trV = blV = vParts[1]; break;
                case 3:  tlV = vParts[0]; trV = blV = vParts[1]; brV = vParts[2]; break;
                default: tlV = vParts[0]; trV = vParts[1]; brV = vParts[2]; blV = vParts[3]; break;
            }
            // Store both radii as "H V" (two-value individual corner property)
            style.Set("border-top-left-radius",     tlH + " " + tlV);
            style.Set("border-top-right-radius",    trH + " " + trV);
            style.Set("border-bottom-right-radius", brH + " " + brV);
            style.Set("border-bottom-left-radius",  blH + " " + blV);
        }
        else
        {
            style.Set("border-top-left-radius",     tlH);
            style.Set("border-top-right-radius",    trH);
            style.Set("border-bottom-right-radius", brH);
            style.Set("border-bottom-left-radius",  blH);
        }
    }

    /// <summary>
    /// Expand a two-part shorthand where the second value defaults to the first.
    /// Used for place-items, place-self, place-content.
    /// </summary>
    private static void ExpandTwoPartShorthand(string value, string firstProp, string secondProp, ComputedStyle style)
    {
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        var first = parts[0];
        var second = parts.Length >= 2 ? parts[1] : first;
        style.Set(firstProp, first);
        style.Set(secondProp, second);
    }
}
