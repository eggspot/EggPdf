using EggPdf.Css;
using EggPdf.Core;
using EggPdf.Html.Dom;
using EggPdf.Layout;
using EggPdf.Pdf;

namespace EggPdf;

/// <summary>
/// Phase 1 renderer: walks the layout tree and writes to PdfDocument.
/// Handles text, backgrounds, and link annotations.
/// </summary>
internal static class PdfRenderer
{
    public static void Render(LayoutBox layoutRoot, PdfDocument pdfDoc, float pageWidthPt, float pageHeightPt)
    {
        var page = pdfDoc.AddPage(pageWidthPt, pageHeightPt);

        // Walk the layout tree and paint
        PaintBox(page, layoutRoot, pageHeightPt);
    }

    private static void PaintBox(PdfPage page, LayoutBox box, float pageHeightPt)
    {
        // Paint background
        var bgColor = box.Style.BackgroundColor;
        if (!string.IsNullOrEmpty(bgColor) && bgColor != "transparent")
        {
            var color = ParseColor(bgColor);
            if (color.HasValue)
            {
                float pdfX = box.X * PdfCoordinates.PxToPt;
                float pdfY = (pageHeightPt / PdfCoordinates.PxToPt - box.Y - box.Height) * PdfCoordinates.PxToPt;
                float pdfW = box.Width * PdfCoordinates.PxToPt;
                float pdfH = box.Height * PdfCoordinates.PxToPt;
                page.AddRectangle(pdfX, pdfY, pdfW, pdfH,
                    color.Value.R / 255f, color.Value.G / 255f, color.Value.B / 255f);
            }
        }

        // Paint text
        if (!string.IsNullOrEmpty(box.Text))
        {
            string fontName = "Helvetica";
            var fontFamily = box.Style.FontFamily;
            if (!string.IsNullOrEmpty(fontFamily))
            {
                if (fontFamily.Contains("monospace") || fontFamily.Contains("Courier"))
                    fontName = "Courier";
                else if (fontFamily.Contains("serif") && !fontFamily.Contains("sans"))
                    fontName = "Times-Roman";
            }

            float fontSize = 12;
            var fsStr = box.Style.FontSize;
            if (!string.IsNullOrEmpty(fsStr))
            {
                float resolved = BlockLayout.ResolveLength(fsStr, 0, 16);
                if (resolved > 0) fontSize = resolved;
            }

            var fontWeight = box.Style.FontWeight;
            if (fontWeight == "bold" || fontWeight == "700" || fontWeight == "800" || fontWeight == "900")
            {
                if (fontName == "Helvetica") fontName = "Helvetica-Bold";
                else if (fontName == "Times-Roman") fontName = "Times-Bold";
                else if (fontName == "Courier") fontName = "Courier-Bold";
            }

            float pdfFontSize = fontSize * PdfCoordinates.PxToPt;
            float pdfX = (box.X + box.PaddingLeft) * PdfCoordinates.PxToPt;
            float pdfY = (pageHeightPt / PdfCoordinates.PxToPt - box.Y - box.PaddingTop - fontSize) * PdfCoordinates.PxToPt;

            page.AddText(box.Text, pdfX, pdfY, fontName, pdfFontSize);
        }

        // Paint links
        if (box.Element?.TagName == "a")
        {
            var href = box.Element.GetAttribute("href");
            if (!string.IsNullOrEmpty(href) && href.StartsWith("http"))
            {
                float pdfX = box.X * PdfCoordinates.PxToPt;
                float pdfY = (pageHeightPt / PdfCoordinates.PxToPt - box.Y - box.Height) * PdfCoordinates.PxToPt;
                float pdfW = box.Width * PdfCoordinates.PxToPt;
                float pdfH = box.Height * PdfCoordinates.PxToPt;
                page.AddLink(pdfX, pdfY, pdfW, pdfH, href);
            }
        }

        // Recurse into children
        foreach (var child in box.Children)
            PaintBox(page, child, pageHeightPt);
    }

    private static Color? ParseColor(string value)
    {
        if (value.StartsWith("#"))
        {
            try { return Color.FromHex(value); }
            catch { return null; }
        }

        return Color.TryParseNamed(value);
    }
}
