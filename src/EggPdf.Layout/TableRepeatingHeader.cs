using System.Collections.Generic;
using System.Linq;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

/// <summary>
/// Handles repeating table headers (thead) and footers (tfoot) across pages.
/// When a table spans multiple pages, the thead is repeated at the top
/// and tfoot at the bottom of each subsequent page.
/// </summary>
public static class TableRepeatingHeader
{
    /// <summary>
    /// Post-process a layout tree to duplicate thead/tfoot boxes on subsequent pages.
    /// Call this after initial layout and pagination.
    /// </summary>
    public static void ApplyRepeatingHeaders(LayoutBox root, float pageHeight, float marginTop)
    {
        var tables = new List<LayoutBox>();
        FindTableBoxes(root, tables);

        foreach (var table in tables)
        {
            ProcessTable(table, pageHeight, marginTop);
        }
    }

    private static void FindTableBoxes(LayoutBox box, List<LayoutBox> tables)
    {
        if (box.Element?.TagName == "table")
        {
            tables.Add(box);
            return; // Don't recurse into nested tables
        }

        foreach (var child in box.Children)
        {
            if (child is LayoutBox childBox)
                FindTableBoxes(childBox, tables);
        }
    }

    private static void ProcessTable(LayoutBox table, float pageHeight, float marginTop)
    {
        // Find thead and tfoot children
        LayoutBox? theadBox = null;
        LayoutBox? tfootBox = null;

        foreach (var child in table.Children)
        {
            if (child is LayoutBox lb)
            {
                if (lb.Element?.TagName == "thead") theadBox = lb;
                else if (lb.Element?.TagName == "tfoot") tfootBox = lb;
            }
        }

        if (theadBox == null) return; // No header to repeat

        // Check if table spans multiple pages
        float tableTop = table.Y;
        float tableBottom = table.Y + table.Height;
        float contentHeight = pageHeight - marginTop;

        int startPage = (int)(tableTop / contentHeight);
        int endPage = (int)(tableBottom / contentHeight);

        if (startPage >= endPage) return; // Table fits on one page

        float theadHeight = theadBox.Height;

        // Mark the original thead with page info
        theadBox.Style?.Set("_repeating-header", "true");
        theadBox.Style?.Set("_header-height", theadHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Store info for the renderer to use when paginating
        table.Style?.Set("_has-repeating-header", "true");
        table.Style?.Set("_header-height", theadHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (tfootBox != null)
        {
            table.Style?.Set("_has-repeating-footer", "true");
            table.Style?.Set("_footer-height", tfootBox.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
