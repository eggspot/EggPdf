using System.Collections.Generic;

namespace EggPdf.Svg;

/// <summary>
/// Converts a parsed HTML <c>&lt;svg&gt;</c> element tree into <see cref="SvgElement"/> objects.
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

        foreach (var attr in elem.Attributes)
            svg.Attributes[attr.Key] = attr.Value;

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
