using System;
using System.Collections.Generic;

namespace EggPdf.Svg;

/// <summary>
/// Converts a parsed HTML <c>&lt;svg&gt;</c> element tree into <see cref="SvgElement"/> objects whose
/// attributes hold each element's effective presentation properties: the <c>style</c> attribute
/// overrides presentation attributes, inherited properties (fill, stroke, fonts, ...) flow down from
/// the parent, and <c>currentColor</c> resolves to the inherited <c>color</c>.
/// </summary>
public static class SvgParser
{
    private const string DefaultCurrentColor = "black";
    private const string CurrentColorKeyword = "currentColor";
    internal const string TextRunTagName = "#text";

    internal static readonly string[] InheritedProperties =
    {
        "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-opacity", "stroke-linecap",
        "stroke-linejoin", "stroke-miterlimit", "stroke-dasharray", "stroke-dashoffset", "font-family",
        "font-size", "font-weight", "font-style", "text-anchor", "color", "visibility", "letter-spacing",
        "dominant-baseline",
    };

    /// <summary>
    /// Convert an HtmlElement (svg tag) to an SvgElement tree.
    /// </summary>
    public static SvgElement? Parse(Html.Dom.HtmlElement htmlElement) => Parse(htmlElement, null);

    /// <summary>
    /// Convert an inline HtmlElement (svg tag) to an SvgElement tree whose root inherits from the HTML
    /// around it: <paramref name="hostProperty"/> returns the computed value of an inherited property
    /// (<c>color</c>, <c>fill</c>, <c>font-size</c>, ...) on the <c>&lt;svg&gt;</c> element, or null.
    /// It fills in only what the root does not set itself.
    /// </summary>
    public static SvgElement? Parse(Html.Dom.HtmlElement htmlElement, Func<string, string?>? hostProperty)
    {
        if (htmlElement == null) return null;
        return ConvertElement(htmlElement, HostElement(hostProperty));
    }

    private static SvgElement? HostElement(Func<string, string?>? hostProperty)
    {
        if (hostProperty == null) return null;
        var host = new SvgElement();
        foreach (var property in InheritedProperties)
        {
            var value = hostProperty(property);
            if (!string.IsNullOrEmpty(value)) host.Attributes[property] = value!;
        }
        return host;
    }

    private static SvgElement ConvertElement(Html.Dom.HtmlElement elem, SvgElement? parent)
    {
        var svg = new SvgElement { TagName = elem.TagName };

        foreach (var attr in elem.Attributes)
            svg.Attributes[attr.Key] = attr.Value;

        ApplyStyleAttribute(svg);
        InheritFrom(svg, parent);
        ResolveCurrentColor(svg, "fill");
        ResolveCurrentColor(svg, "stroke");

        bool keepsTextRuns = HoldsTextRuns(svg.TagName);
        foreach (var child in elem.ChildNodes)
        {
            if (child is Html.Dom.HtmlElement childElem)
            {
                svg.Children.Add(ConvertElement(childElem, svg));
            }
            else if (child is Html.Dom.HtmlTextNode textNode)
            {
                svg.TextContent += textNode.Data;
                if (keepsTextRuns)
                    svg.Children.Add(new SvgElement { TagName = TextRunTagName, TextContent = textNode.Data });
            }
        }

        return svg;
    }

    internal static bool HoldsTextRuns(string tagName) =>
        tagName == "text" || tagName == "tspan" || tagName == "textpath";

    private static void ApplyStyleAttribute(SvgElement svg)
    {
        if (!svg.Attributes.TryGetValue("style", out var style) || string.IsNullOrEmpty(style)) return;
        foreach (var declaration in style.Split(';'))
        {
            int colon = declaration.IndexOf(':');
            if (colon <= 0) continue;
            var property = declaration.Substring(0, colon).Trim().ToLowerInvariant();
            var value = declaration.Substring(colon + 1).Trim();
            if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - "!important".Length).Trim();
            if (property.Length > 0 && value.Length > 0)
                svg.Attributes[property] = value;
        }
    }

    internal static void InheritFrom(SvgElement svg, SvgElement? parent)
    {
        if (parent == null) return;
        foreach (var property in InheritedProperties)
        {
            if (svg.Attributes.TryGetValue(property, out var own) && own != "inherit") continue;
            if (parent.Attributes.TryGetValue(property, out var inherited))
            {
                svg.Attributes[property] = inherited;
                (svg.InheritedPropertyNames ??= new HashSet<string>(StringComparer.Ordinal)).Add(property);
            }
            else
            {
                svg.Attributes.Remove(property);
            }
        }
    }

    private static void ResolveCurrentColor(SvgElement svg, string property)
    {
        if (svg.Attributes.TryGetValue(property, out var value) &&
            string.Equals(value, CurrentColorKeyword, StringComparison.OrdinalIgnoreCase))
        {
            svg.Attributes[property] = svg.Attributes.TryGetValue("color", out var color) ? color : DefaultCurrentColor;
        }
    }
}
