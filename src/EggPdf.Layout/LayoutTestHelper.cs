using EggPdf.Html;

namespace EggPdf.Layout;

/// <summary>
/// Helper for layout tests: parse HTML and run layout, return queryable layout tree.
/// </summary>
public static class LayoutTestHelper
{
    public static LayoutBox Layout(string html, float pageWidth = 595.28f, float pageHeight = 841.89f)
    {
        var document = HtmlParser.Parse(html);
        return BlockLayout.LayoutDocument(document, pageWidth, pageHeight);
    }
}
