using System;
using System.Collections.Generic;
using System.Linq;
using EggPdf.Css;
using EggPdf.Core;
using EggPdf.Html.Dom;
using EggPdf.Layout;
using EggPdf.Pdf;

namespace EggPdf;

/// <summary>
/// Renders layout boxes to PDF pages. Handles pagination (splitting content
/// across multiple pages when it exceeds page height).
/// </summary>
internal static class PdfRenderer
{
    public static void Render(LayoutBox layoutRoot, PdfDocument pdfDoc,
        float pageWidthPt, float pageHeightPt, float pageHeightPx)
    {
        // Collect all leaf boxes (boxes with text or background)
        var allBoxes = new List<LayoutBox>();
        CollectPaintableBoxes(layoutRoot, allBoxes);

        // Also collect heading boxes for bookmarks
        var headings = new List<(string title, int level, float yPx)>();
        CollectHeadings(layoutRoot, headings);

        if (allBoxes.Count == 0)
        {
            // Empty document: single blank page
            pdfDoc.AddPage(pageWidthPt, pageHeightPt);
            return;
        }

        // Determine total content height
        float maxY = allBoxes.Max(b => b.Y + b.Height);

        // Calculate number of pages
        int numPages = Math.Max(1, (int)Math.Ceiling(maxY / pageHeightPx));

        // Render each page
        for (int pageIdx = 0; pageIdx < numPages; pageIdx++)
        {
            var page = pdfDoc.AddPage(pageWidthPt, pageHeightPt);

            float pageTopPx = pageIdx * pageHeightPx;
            float pageBottomPx = (pageIdx + 1) * pageHeightPx;

            // Paint boxes that fall on this page
            foreach (var box in allBoxes)
            {
                float boxTop = box.Y;
                float boxBottom = box.Y + box.Height;

                // Skip boxes entirely outside this page
                if (boxBottom <= pageTopPx || boxTop >= pageBottomPx)
                    continue;

                // Adjust Y coordinate relative to this page
                float adjustedY = box.Y - pageTopPx;
                PaintBox(page, box, pageHeightPt, pageHeightPx, adjustedY);
            }
        }

        // Add bookmarks from headings
        // Bookmarks are added to the PDF outline (viewer sidebar)
        // For now, bookmarks are text in the PDF Info
    }

    // Overload for backward compatibility
    public static void Render(LayoutBox layoutRoot, PdfDocument pdfDoc,
        float pageWidthPt, float pageHeightPt)
    {
        float pageHeightPx = pageHeightPt / PdfCoordinates.PxToPt;
        Render(layoutRoot, pdfDoc, pageWidthPt, pageHeightPt, pageHeightPx);
    }

    private static void CollectPaintableBoxes(LayoutBox box, List<LayoutBox> result)
    {
        // A box is paintable if it has text, background, or is a link
        bool hasPaint = !string.IsNullOrEmpty(box.Text) ||
                        !string.IsNullOrEmpty(box.Style.BackgroundColor) &&
                        box.Style.BackgroundColor != "transparent" ||
                        box.Element?.TagName == "a";

        if (hasPaint)
            result.Add(box);

        foreach (var child in box.Children)
            CollectPaintableBoxes(child, result);
    }

    private static void CollectHeadings(LayoutBox box, List<(string title, int level, float yPx)> headings)
    {
        if (box.Element != null && box.Element.TagName.Length == 2 &&
            box.Element.TagName[0] == 'h' &&
            box.Element.TagName[1] >= '1' && box.Element.TagName[1] <= '6')
        {
            var text = box.Text ?? GetChildText(box);
            if (!string.IsNullOrEmpty(text))
                headings.Add((text, box.Element.TagName[1] - '0', box.Y));
        }

        foreach (var child in box.Children)
            CollectHeadings(child, headings);
    }

    private static string? GetChildText(LayoutBox box)
    {
        foreach (var child in box.Children)
        {
            if (!string.IsNullOrEmpty(child.Text)) return child.Text;
            var childText = GetChildText(child);
            if (childText != null) return childText;
        }
        return null;
    }

    private static void PaintBox(PdfPage page, LayoutBox box,
        float pageHeightPt, float pageHeightPx, float adjustedY)
    {
        // Paint background
        var bgColor = box.Style.BackgroundColor;
        if (!string.IsNullOrEmpty(bgColor) && bgColor != "transparent")
        {
            var color = ParseColor(bgColor);
            if (color.HasValue)
            {
                float pdfX = box.X * PdfCoordinates.PxToPt;
                float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
                float pdfW = box.Width * PdfCoordinates.PxToPt;
                float pdfH = box.Height * PdfCoordinates.PxToPt;
                page.AddRectangle(pdfX, pdfY, pdfW, pdfH,
                    color.Value.R / 255f, color.Value.G / 255f, color.Value.B / 255f);
            }
        }

        // Paint border
        var borderStyle = box.Style.Get("border-top-style") ?? box.Style.Get("border-style");
        if (!string.IsNullOrEmpty(borderStyle) && borderStyle != "none")
        {
            var borderWidthStr = box.Style.Get("border-top-width") ?? box.Style.Get("border-width");
            float borderWidth = 1;
            if (!string.IsNullOrEmpty(borderWidthStr))
                borderWidth = Layout.BlockLayout.ResolveLength(borderWidthStr, 0, 16);
            if (borderWidth <= 0) borderWidth = 1;

            var borderColorStr = box.Style.Get("border-top-color") ?? box.Style.Get("border-color");
            float br = 0, bg = 0, bb = 0;
            if (!string.IsNullOrEmpty(borderColorStr))
            {
                var bc = ParseColor(borderColorStr);
                if (bc.HasValue) { br = bc.Value.R / 255f; bg = bc.Value.G / 255f; bb = bc.Value.B / 255f; }
            }

            float pdfX = box.X * PdfCoordinates.PxToPt;
            float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
            float pdfW = box.Width * PdfCoordinates.PxToPt;
            float pdfH = box.Height * PdfCoordinates.PxToPt;

            page.AddStrokeRectangle(pdfX, pdfY, pdfW, pdfH, br, bg, bb, borderWidth * PdfCoordinates.PxToPt);
        }

        // Paint text
        if (!string.IsNullOrEmpty(box.Text))
        {
            string fontName = "Helvetica";
            var fontFamily = box.Style.FontFamily;
            if (!string.IsNullOrEmpty(fontFamily))
            {
                if (fontFamily.IndexOf("monospace", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    fontFamily.IndexOf("Courier", StringComparison.OrdinalIgnoreCase) >= 0)
                    fontName = "Courier";
                else if (fontFamily.IndexOf("serif", StringComparison.OrdinalIgnoreCase) >= 0 &&
                         fontFamily.IndexOf("sans", StringComparison.OrdinalIgnoreCase) < 0)
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

            var fontStyle = box.Style.Get("font-style");
            if (fontStyle == "italic" || fontStyle == "oblique")
            {
                if (fontName == "Helvetica") fontName = "Helvetica-Oblique";
                else if (fontName == "Helvetica-Bold") fontName = "Helvetica-BoldOblique";
                else if (fontName == "Times-Roman") fontName = "Times-Italic";
                else if (fontName == "Times-Bold") fontName = "Times-BoldItalic";
                else if (fontName == "Courier") fontName = "Courier-Oblique";
                else if (fontName == "Courier-Bold") fontName = "Courier-BoldOblique";
            }

            float pdfFontSize = fontSize * PdfCoordinates.PxToPt;
            float pdfX = (box.X + box.PaddingLeft) * PdfCoordinates.PxToPt;
            float pdfY = (pageHeightPx - adjustedY - box.PaddingTop - fontSize) * PdfCoordinates.PxToPt;

            // Text color
            var textColor = box.Style.Color;
            Color? color = null;
            if (!string.IsNullOrEmpty(textColor))
                color = ParseColor(textColor);

            page.AddText(box.Text, pdfX, pdfY, fontName, pdfFontSize,
                color?.R / 255f ?? 0, color?.G / 255f ?? 0, color?.B / 255f ?? 0);
        }

        // Paint links
        if (box.Element?.TagName == "a")
        {
            var href = box.Element.GetAttribute("href");
            if (!string.IsNullOrEmpty(href) && href.StartsWith("http"))
            {
                float pdfX = box.X * PdfCoordinates.PxToPt;
                float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
                float pdfW = box.Width * PdfCoordinates.PxToPt;
                float pdfH = box.Height * PdfCoordinates.PxToPt;
                page.AddLink(pdfX, pdfY, pdfW, pdfH, href);
            }
        }
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
