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

        // Collect forced page break Y positions
        var pageBreakYs = new List<float>();
        CollectPageBreaks(layoutRoot, pageBreakYs);
        pageBreakYs.Sort();

        // Determine total content height
        float maxY = allBoxes.Max(b => b.Y + b.Height);

        // Build page boundaries (combining natural page breaks with forced ones)
        var pageBounds = new List<(float top, float bottom)>();
        float currentTop = 0;

        foreach (float breakY in pageBreakYs)
        {
            if (breakY > currentTop && breakY < maxY)
            {
                pageBounds.Add((currentTop, breakY));
                currentTop = breakY;
            }
        }

        // Fill remaining pages using natural page height
        while (currentTop < maxY)
        {
            float bottom = Math.Min(currentTop + pageHeightPx, maxY);
            pageBounds.Add((currentTop, bottom));
            currentTop = bottom;
        }

        if (pageBounds.Count == 0)
            pageBounds.Add((0, maxY));

        // Render each page
        foreach (var (pageTopPx, pageBottomPx) in pageBounds)
        {
            var page = pdfDoc.AddPage(pageWidthPt, pageHeightPt);

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
        // A box is paintable if it has text, background, image, border, or is a link
        var borderStyle = box.Style.Get("border-top-style");
        bool hasBorder = !string.IsNullOrEmpty(borderStyle) && borderStyle != "none";

        bool hasPaint = !string.IsNullOrEmpty(box.Text) ||
                        !string.IsNullOrEmpty(box.ImageSource) ||
                        hasBorder ||
                        !string.IsNullOrEmpty(box.Style.BackgroundColor) &&
                        box.Style.BackgroundColor != "transparent" ||
                        box.Element?.TagName == "a";

        if (hasPaint)
            result.Add(box);

        foreach (var child in box.Children)
            CollectPaintableBoxes(child, result);
    }

    private static void CollectPageBreaks(LayoutBox box, List<float> breakYs)
    {
        // Check page-break-before
        var breakBefore = box.Style.Get("page-break-before") ?? box.Style.Get("break-before");
        if (breakBefore == "always" || breakBefore == "page")
        {
            breakYs.Add(box.Y);
        }

        // Check page-break-after
        var breakAfter = box.Style.Get("page-break-after") ?? box.Style.Get("break-after");
        if (breakAfter == "always" || breakAfter == "page")
        {
            breakYs.Add(box.Y + box.Height);
        }

        foreach (var child in box.Children)
            CollectPageBreaks(child, breakYs);
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
        // Visibility:hidden - box takes space but is not painted
        var visibility = box.Style.Get("visibility");
        if (visibility == "hidden" || visibility == "collapse")
            return;

        // Overflow:hidden clipping
        var overflow = box.Style.Get("overflow");
        bool hasClip = overflow == "hidden" || overflow == "clip";
        if (hasClip)
        {
            float clipX = box.X * PdfCoordinates.PxToPt;
            float clipY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
            float clipW = box.Width * PdfCoordinates.PxToPt;
            float clipH = box.Height * PdfCoordinates.PxToPt;
            page.SaveState();
            page.AddClipRect(clipX, clipY, clipW, clipH);
        }

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
            // Apply text-transform
            var textTransform = box.Style.Get("text-transform");
            var paintText = box.Text;
            if (!string.IsNullOrEmpty(textTransform))
            {
                if (textTransform == "uppercase") paintText = paintText.ToUpperInvariant();
                else if (textTransform == "lowercase") paintText = paintText.ToLowerInvariant();
                else if (textTransform == "capitalize") paintText = CapitalizeText(paintText);
            }

            float fontSize = 12;
            var fsStr = box.Style.FontSize;
            if (!string.IsNullOrEmpty(fsStr))
            {
                float resolved = BlockLayout.ResolveLength(fsStr, 0, 16);
                if (resolved > 0) fontSize = resolved;
            }

            string fontName = StandardFontMetrics.ResolvePdfFontName(
                box.Style.FontFamily, box.Style.FontWeight, box.Style.Get("font-style"));

            float pdfFontSize = fontSize * PdfCoordinates.PxToPt;
            float textX = box.X + box.PaddingLeft;

            // Text alignment
            var textAlign = box.Style.TextAlign;
            if (!string.IsNullOrEmpty(textAlign) && box.ContentWidth < box.Width)
            {
                float availableWidth = box.Width - box.PaddingLeft - box.PaddingRight;
                float textWidth = box.ContentWidth;
                if (textAlign == "center")
                    textX += (availableWidth - textWidth) / 2;
                else if (textAlign == "right")
                    textX += availableWidth - textWidth;
            }

            float pdfX = textX * PdfCoordinates.PxToPt;
            float pdfY = (pageHeightPx - adjustedY - box.PaddingTop - fontSize) * PdfCoordinates.PxToPt;

            // Text color
            var textColor = box.Style.Color;
            Color? color = null;
            if (!string.IsNullOrEmpty(textColor))
                color = ParseColor(textColor);

            // Letter-spacing and word-spacing
            float letterSpacing = 0, wordSpacing = 0;
            var lsStr = box.Style.Get("letter-spacing");
            if (!string.IsNullOrEmpty(lsStr) && lsStr != "normal")
                letterSpacing = BlockLayout.ResolveLength(lsStr, 0, fontSize) * PdfCoordinates.PxToPt;
            var wsStr = box.Style.Get("word-spacing");
            if (!string.IsNullOrEmpty(wsStr) && wsStr != "normal")
                wordSpacing = BlockLayout.ResolveLength(wsStr, 0, fontSize) * PdfCoordinates.PxToPt;

            page.AddText(paintText, pdfX, pdfY, fontName, pdfFontSize,
                color?.R / 255f ?? 0, color?.G / 255f ?? 0, color?.B / 255f ?? 0,
                letterSpacing, wordSpacing);

            // Text decoration (underline, line-through)
            var textDecoration = box.Style.Get("text-decoration");
            if (!string.IsNullOrEmpty(textDecoration) && textDecoration != "none")
            {
                float lineY;
                float textWidth = box.ContentWidth * PdfCoordinates.PxToPt;
                float decoLineWidth = Math.Max(fontSize * 0.05f, 0.5f) * PdfCoordinates.PxToPt;

                float dr = 0, dg = 0, db = 0;
                if (color.HasValue) { dr = color.Value.R / 255f; dg = color.Value.G / 255f; db = color.Value.B / 255f; }

                if (textDecoration.IndexOf("underline", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    lineY = pdfY - fontSize * 0.15f * PdfCoordinates.PxToPt;
                    page.AddLine(pdfX, lineY, pdfX + textWidth, lineY, dr, dg, db, decoLineWidth);
                }
                if (textDecoration.IndexOf("line-through", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    lineY = pdfY + fontSize * 0.3f * PdfCoordinates.PxToPt;
                    page.AddLine(pdfX, lineY, pdfX + textWidth, lineY, dr, dg, db, decoLineWidth);
                }
            }
        }

        // Paint image
        if (!string.IsNullOrEmpty(box.ImageSource) && box.ImageData != null)
        {
            float pdfX = box.X * PdfCoordinates.PxToPt;
            float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
            float pdfW = box.Width * PdfCoordinates.PxToPt;
            float pdfH = box.Height * PdfCoordinates.PxToPt;

            string imgName = "Img" + box.ImageSource.GetHashCode().ToString("X8");
            page.AddImage(imgName, pdfX, pdfY, pdfW, pdfH);
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

        // Restore clipping state
        if (hasClip)
            page.RestoreState();
    }

    private static string CapitalizeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var chars = text.ToCharArray();
        bool capitalizeNext = true;
        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsWhiteSpace(chars[i]))
            {
                capitalizeNext = true;
            }
            else if (capitalizeNext)
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
                capitalizeNext = false;
            }
        }
        return new string(chars);
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
