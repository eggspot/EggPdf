using System;
using System.Collections.Generic;
using EggPdf.Css.Cascade;
using EggPdf.Html.Dom;

namespace EggPdf.Css;

/// <summary>
/// Phase 1 style resolver: applies UA defaults + inline styles.
/// No external stylesheets, no specificity, no cascade. Just inline + defaults.
/// </summary>
public class BasicStyleResolver
{
    // Inherited properties (child inherits from parent if not explicitly set)
    private static readonly HashSet<string> InheritedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "color", "font-family", "font-size", "font-weight", "font-style",
        "line-height", "text-align", "text-indent", "text-transform",
        "letter-spacing", "word-spacing", "white-space", "direction",
        "visibility", "list-style-type", "list-style-position",
        "border-collapse", "border-spacing", "caption-side", "empty-cells"
    };

    // UA defaults per element
    private static readonly Dictionary<string, Dictionary<string, string>> UaDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["html"] = new() { ["display"] = "block" },
        ["body"] = new() { ["display"] = "block", ["margin-top"] = "8px", ["margin-right"] = "8px", ["margin-bottom"] = "8px", ["margin-left"] = "8px" },
        ["div"] = new() { ["display"] = "block" },
        ["section"] = new() { ["display"] = "block" },
        ["article"] = new() { ["display"] = "block" },
        ["aside"] = new() { ["display"] = "block" },
        ["header"] = new() { ["display"] = "block" },
        ["footer"] = new() { ["display"] = "block" },
        ["nav"] = new() { ["display"] = "block" },
        ["main"] = new() { ["display"] = "block" },
        ["p"] = new() { ["display"] = "block", ["margin-top"] = "1em", ["margin-bottom"] = "1em" },
        ["h1"] = new() { ["display"] = "block", ["font-size"] = "2em", ["font-weight"] = "bold", ["margin-top"] = "0.67em", ["margin-bottom"] = "0.67em" },
        ["h2"] = new() { ["display"] = "block", ["font-size"] = "1.5em", ["font-weight"] = "bold", ["margin-top"] = "0.83em", ["margin-bottom"] = "0.83em" },
        ["h3"] = new() { ["display"] = "block", ["font-size"] = "1.17em", ["font-weight"] = "bold", ["margin-top"] = "1em", ["margin-bottom"] = "1em" },
        ["h4"] = new() { ["display"] = "block", ["font-weight"] = "bold", ["margin-top"] = "1.33em", ["margin-bottom"] = "1.33em" },
        ["h5"] = new() { ["display"] = "block", ["font-size"] = "0.83em", ["font-weight"] = "bold", ["margin-top"] = "1.67em", ["margin-bottom"] = "1.67em" },
        ["h6"] = new() { ["display"] = "block", ["font-size"] = "0.67em", ["font-weight"] = "bold", ["margin-top"] = "2.33em", ["margin-bottom"] = "2.33em" },
        ["ul"] = new() { ["display"] = "block", ["margin-top"] = "1em", ["margin-bottom"] = "1em", ["padding-left"] = "40px", ["list-style-type"] = "disc" },
        ["ol"] = new() { ["display"] = "block", ["margin-top"] = "1em", ["margin-bottom"] = "1em", ["padding-left"] = "40px", ["list-style-type"] = "decimal" },
        ["li"] = new() { ["display"] = "list-item" },
        ["blockquote"] = new() { ["display"] = "block", ["margin-top"] = "1em", ["margin-bottom"] = "1em", ["margin-left"] = "40px", ["margin-right"] = "40px" },
        ["pre"] = new() { ["display"] = "block", ["font-family"] = "monospace", ["white-space"] = "pre", ["margin-top"] = "1em", ["margin-bottom"] = "1em" },
        ["hr"] = new() { ["display"] = "block", ["margin-top"] = "0.5em", ["margin-bottom"] = "0.5em", ["border-top-style"] = "inset", ["border-top-width"] = "1px" },
        ["a"] = new() { ["color"] = "blue", ["text-decoration"] = "underline" },
        ["strong"] = new() { ["font-weight"] = "bold" },
        ["b"] = new() { ["font-weight"] = "bold" },
        ["em"] = new() { ["font-style"] = "italic" },
        ["i"] = new() { ["font-style"] = "italic" },
        ["u"] = new() { ["text-decoration"] = "underline" },
        ["s"] = new() { ["text-decoration"] = "line-through" },
        ["del"] = new() { ["text-decoration"] = "line-through" },
        ["small"] = new() { ["font-size"] = "smaller" },
        ["code"] = new() { ["font-family"] = "monospace" },
        ["kbd"] = new() { ["font-family"] = "monospace" },
        ["samp"] = new() { ["font-family"] = "monospace" },
        ["var"] = new() { ["font-style"] = "italic" },
        ["mark"] = new() { ["background-color"] = "yellow", ["color"] = "black" },
        ["table"] = new() { ["display"] = "table", ["border-collapse"] = "separate", ["border-spacing"] = "2px" },
        ["thead"] = new() { ["display"] = "table-header-group" },
        ["tbody"] = new() { ["display"] = "table-row-group" },
        ["tfoot"] = new() { ["display"] = "table-footer-group" },
        ["tr"] = new() { ["display"] = "table-row" },
        ["td"] = new() { ["display"] = "table-cell", ["padding-top"] = "1px", ["padding-right"] = "1px", ["padding-bottom"] = "1px", ["padding-left"] = "1px" },
        ["th"] = new() { ["display"] = "table-cell", ["font-weight"] = "bold", ["padding-top"] = "1px", ["padding-right"] = "1px", ["padding-bottom"] = "1px", ["padding-left"] = "1px" },
        ["caption"] = new() { ["display"] = "table-caption", ["text-align"] = "center" },
        ["img"] = new() { ["display"] = "inline" },
        ["br"] = new() { ["display"] = "inline" },
        ["span"] = new() { ["display"] = "inline" },
        ["sub"] = new() { ["vertical-align"] = "sub", ["font-size"] = "smaller" },
        ["sup"] = new() { ["vertical-align"] = "super", ["font-size"] = "smaller" },
        ["fieldset"] = new() { ["display"] = "block", ["border-top-width"] = "2px", ["border-right-width"] = "2px", ["border-bottom-width"] = "2px", ["border-left-width"] = "2px", ["border-top-style"] = "groove" },
        ["address"] = new() { ["display"] = "block", ["font-style"] = "italic" },
        ["center"] = new() { ["display"] = "block", ["text-align"] = "center" },
        ["figure"] = new() { ["display"] = "block", ["margin-top"] = "1em", ["margin-bottom"] = "1em", ["margin-left"] = "40px", ["margin-right"] = "40px" },
        ["figcaption"] = new() { ["display"] = "block" },
        ["details"] = new() { ["display"] = "block" },
        ["summary"] = new() { ["display"] = "list-item" },
    };

    /// <summary>
    /// Resolve the computed style for an element.
    /// </summary>
    public ComputedStyle Resolve(HtmlElement element, ComputedStyle? parentStyle)
    {
        var style = new ComputedStyle();

        // 1. Apply UA defaults
        if (UaDefaults.TryGetValue(element.TagName, out var defaults))
        {
            foreach (var kv in defaults)
                style.Set(kv.Key, kv.Value);
        }

        // Default display for unknown elements
        if (!style.Has("display"))
            style.Set("display", "inline");

        // 1b. Apply HTML presentational attributes (lowest priority, below UA)
        ApplyPresentationalAttributes(element, style);

        // 2. Inherit from parent
        if (parentStyle != null)
        {
            foreach (var prop in InheritedProperties)
            {
                if (!style.Has(prop))
                {
                    var parentVal = parentStyle.Get(prop);
                    if (parentVal != null)
                        style.Set(prop, parentVal);
                }
            }

            // Inherit custom properties (all custom properties inherit per CSS spec)
            foreach (var kv in parentStyle.All)
            {
                if (CssVariableResolver.IsCustomProperty(kv.Key) && !style.Has(kv.Key))
                    style.Set(kv.Key, kv.Value);
            }
        }

        // 3. Apply inline styles (highest priority)
        var inlineCss = element.GetAttribute("style");
        if (!string.IsNullOrEmpty(inlineCss))
        {
            var declarations = CssInlineParser.Parse(inlineCss);
            foreach (var decl in declarations)
            {
                // Try expanding shorthands (margin, padding, border, background)
                if (!CssShorthandExpander.TryExpand(decl.Property, decl.Value, style))
                {
                    style.Set(decl.Property, decl.Value);
                }
            }
        }

        // 4. Handle hidden attribute
        if (element.HasAttribute("hidden"))
            style.Set("display", "none");

        // 5. Resolve var() references in all non-custom property values
        ResolveCustomProperties(style);

        return style;
    }

    /// <summary>
    /// Resolve all var() references in non-custom property values.
    /// </summary>
    private static void ResolveCustomProperties(ComputedStyle style)
    {
        var toResolve = new List<KeyValuePair<string, string>>();
        foreach (var kv in style.All)
        {
            if (!CssVariableResolver.IsCustomProperty(kv.Key) &&
                kv.Value.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                toResolve.Add(kv);
            }
        }

        foreach (var kv in toResolve)
        {
            var resolved = CssVariableResolver.ResolveVariables(kv.Value, style);
            if (resolved != kv.Value)
                style.Set(kv.Key, resolved);
        }
    }

    /// <summary>Expand border shorthand: "1px solid red" -> individual properties.</summary>
    private static void ExpandBorderShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        string? width = null, borderStyle = null, color = null;

        foreach (var part in parts)
        {
            if (part == "solid" || part == "dashed" || part == "dotted" || part == "double" ||
                part == "groove" || part == "ridge" || part == "inset" || part == "outset" || part == "none")
                borderStyle = part;
            else if (part.EndsWith("px") || part.EndsWith("em") || part.EndsWith("pt") ||
                     part == "thin" || part == "medium" || part == "thick")
                width = part == "thin" ? "1px" : part == "medium" ? "3px" : part == "thick" ? "5px" : part;
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
    /// Apply legacy HTML presentational attributes as CSS equivalents.
    /// These have the lowest priority -- any stylesheet or inline style overrides them.
    /// </summary>
    private static void ApplyPresentationalAttributes(HtmlElement element, ComputedStyle style)
    {
        // width / height attributes (img, table, td, th, col)
        var widthAttr = element.GetAttribute("width");
        if (!string.IsNullOrEmpty(widthAttr) && !style.Has("width"))
        {
            style.Set("width", widthAttr.EndsWith("%") ? widthAttr : widthAttr + "px");
        }

        var heightAttr = element.GetAttribute("height");
        if (!string.IsNullOrEmpty(heightAttr) && !style.Has("height"))
        {
            style.Set("height", heightAttr.EndsWith("%") ? heightAttr : heightAttr + "px");
        }

        // bgcolor
        var bgcolor = element.GetAttribute("bgcolor");
        if (!string.IsNullOrEmpty(bgcolor) && !style.Has("background-color"))
            style.Set("background-color", bgcolor);

        // align
        var align = element.GetAttribute("align");
        if (!string.IsNullOrEmpty(align) && !style.Has("text-align"))
            style.Set("text-align", align.ToLowerInvariant());

        // valign
        var valign = element.GetAttribute("valign");
        if (!string.IsNullOrEmpty(valign) && !style.Has("vertical-align"))
            style.Set("vertical-align", valign.ToLowerInvariant());

        // border (table)
        var border = element.GetAttribute("border");
        if (!string.IsNullOrEmpty(border) && element.TagName == "table")
        {
            if (!style.Has("border-top-width"))
            {
                var bw = border == "0" ? "0" : border + "px";
                style.Set("border-top-width", bw);
                style.Set("border-right-width", bw);
                style.Set("border-bottom-width", bw);
                style.Set("border-left-width", bw);
                if (border != "0")
                {
                    style.Set("border-top-style", "solid");
                    style.Set("border-right-style", "solid");
                    style.Set("border-bottom-style", "solid");
                    style.Set("border-left-style", "solid");
                }
            }
        }

        // Propagate table border attribute to cells (td/th)
        if ((element.TagName == "td" || element.TagName == "th") && !style.Has("border-top-width"))
        {
            var tableBorder = FindAncestorTableBorder(element);
            if (!string.IsNullOrEmpty(tableBorder) && tableBorder != "0")
            {
                var bw = tableBorder + "px";
                style.Set("border-top-width", bw);
                style.Set("border-right-width", bw);
                style.Set("border-bottom-width", bw);
                style.Set("border-left-width", bw);
                style.Set("border-top-style", "solid");
                style.Set("border-right-style", "solid");
                style.Set("border-bottom-style", "solid");
                style.Set("border-left-style", "solid");
            }
        }

        // color (font element)
        var colorAttr = element.GetAttribute("color");
        if (!string.IsNullOrEmpty(colorAttr) && !style.Has("color"))
            style.Set("color", colorAttr);

        // face (font element)
        var face = element.GetAttribute("face");
        if (!string.IsNullOrEmpty(face) && !style.Has("font-family"))
            style.Set("font-family", face);
    }

    /// <summary>Walk up from a td/th to find ancestor table's border attribute.</summary>
    private static string? FindAncestorTableBorder(HtmlElement element)
    {
        var parent = element.Parent as HtmlElement;
        while (parent != null)
        {
            if (parent.TagName == "table")
                return parent.GetAttribute("border");
            parent = parent.Parent as HtmlElement;
        }
        return null;
    }
}
