using System.Collections.Generic;

namespace EggPdf.Svg;

/// <summary>
/// Parses an HTML <svg> element tree into SvgElement objects.
/// Works with EggPdf's HtmlElement DOM.
/// </summary>
public static class SvgParser
{
    /// <summary>
    /// Convert an HtmlElement (svg tag) to an SvgElement tree.
    /// </summary>
    public static SvgElement? Parse(Html.Dom.HtmlElement htmlElement)
    {
        if (htmlElement == null) return null;
        return ConvertElement(htmlElement);
    }

    private static SvgElement ConvertElement(Html.Dom.HtmlElement elem)
    {
        var svg = new SvgElement { TagName = elem.TagName };

        // Copy attributes
        foreach (var attr in elem.Attributes)
            svg.Attributes[attr.Key] = attr.Value;

        // Convert children
        foreach (var child in elem.ChildNodes)
        {
            if (child is Html.Dom.HtmlElement childElem)
            {
                svg.Children.Add(ConvertElement(childElem));
            }
            else if (child is Html.Dom.HtmlTextNode textNode)
            {
                svg.TextContent += textNode.Data;
            }
        }

        return svg;
    }
}
