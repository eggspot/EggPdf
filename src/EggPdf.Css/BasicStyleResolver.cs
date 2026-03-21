using System;
using System.Collections.Generic;
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
        ["th"] = new() { ["display"] = "table-cell", ["font-weight"] = "bold", ["text-align"] = "center", ["padding-top"] = "1px", ["padding-right"] = "1px", ["padding-bottom"] = "1px", ["padding-left"] = "1px" },
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
        }

        // 3. Apply inline styles (highest priority)
        var inlineCss = element.GetAttribute("style");
        if (!string.IsNullOrEmpty(inlineCss))
        {
            var declarations = CssInlineParser.Parse(inlineCss);
            foreach (var decl in declarations)
                style.Set(decl.Property, decl.Value);
        }

        // 4. Handle hidden attribute
        if (element.HasAttribute("hidden"))
            style.Set("display", "none");

        return style;
    }
}
