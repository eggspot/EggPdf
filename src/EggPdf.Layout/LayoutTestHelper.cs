using System.Collections.Generic;
using EggPdf.Css.Cascade;
using EggPdf.Css.Parser;
using EggPdf.Html;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

/// <summary>
/// Helper for layout tests: parse HTML and run layout, return queryable layout tree.
/// Always uses CascadeResolver so that &lt;style&gt; tags, selectors, pseudo-elements,
/// and CSS counters work in tests.
/// </summary>
public static class LayoutTestHelper
{
    public static LayoutBox Layout(string html, float pageWidth = 595.28f, float pageHeight = 841.89f)
    {
        var document = HtmlParser.Parse(html);
        var stylesheets = ExtractStyleSheets(document);
        var cascadeResolver = new CascadeResolver(stylesheets, mediaType: "print");
        return BlockLayout.LayoutDocument(document, pageWidth, pageHeight, cascadeResolver);
    }

    private static List<CssStyleSheet> ExtractStyleSheets(HtmlDocument document)
    {
        var sheets = new List<CssStyleSheet>();
        var head = document.Head;
        if (head == null) return sheets;

        foreach (var node in head.ChildNodes)
        {
            if (node is HtmlElement elem && elem.TagName == "style")
            {
                var cssText = GetElementText(elem);
                if (!string.IsNullOrWhiteSpace(cssText))
                    sheets.Add(CssStyleSheetParser.Parse(cssText));
            }
        }
        return sheets;
    }

    private static string GetElementText(HtmlElement elem)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var child in elem.ChildNodes)
        {
            if (child is EggPdf.Html.Dom.HtmlTextNode t)
                sb.Append(t.Data);
        }
        return sb.ToString();
    }
}
