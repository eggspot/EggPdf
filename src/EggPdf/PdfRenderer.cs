using System;
using System.Collections.Generic;
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
    // Static arrays to avoid per-call heap allocations in hot paths
    private static readonly string[] BorderSides = { "top", "right", "bottom", "left" };
    private static readonly char[] SpaceSep = { ' ' };
    private static readonly char[] CommaSpaceSep = { ',', ' ' };

    /// <summary>Page margin offsets in CSS pixels, applied when painting boxes.</summary>
    [ThreadStatic]
    private static float _marginLeftPx;
    [ThreadStatic]
    private static float _marginTopPx;
    [ThreadStatic]
    private static PdfDocument? _currentPdfDoc;

    public static void Render(LayoutBox layoutRoot, PdfDocument pdfDoc,
        float pageWidthPt, float pageHeightPt, float pageHeightPx,
        float marginLeftPx = 0, float marginTopPx = 0)
    {
        _marginLeftPx = marginLeftPx;
        _marginTopPx = marginTopPx;
        _currentPdfDoc = pdfDoc;

        try
        {
            RenderCore(layoutRoot, pdfDoc, pageWidthPt, pageHeightPt, pageHeightPx);
        }
        finally
        {
            _marginLeftPx = 0;
            _marginTopPx = 0;
            _currentPdfDoc = null;
        }
    }

    // Overload for backward compatibility
    public static void Render(LayoutBox layoutRoot, PdfDocument pdfDoc,
        float pageWidthPt, float pageHeightPt)
    {
        float pageHeightPx = pageHeightPt / PdfCoordinates.PxToPt;
        Render(layoutRoot, pdfDoc, pageWidthPt, pageHeightPt, pageHeightPx);
    }

    private static void RenderCore(LayoutBox layoutRoot, PdfDocument pdfDoc,
        float pageWidthPt, float pageHeightPt, float pageHeightPx)
    {
        // Collect all leaf boxes (boxes with text or background)
        var allBoxes = new List<LayoutBox>();
        CollectPaintableBoxes(layoutRoot, allBoxes);

        // Sort by z-index stacking order: non-positioned first (doc order),
        // then positioned elements sorted by z-index ascending (higher = painted later = on top)
        SortByZIndex(allBoxes);

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
        float maxY = 0;
        for (int i = 0; i < allBoxes.Count; i++)
        {
            float bottom = allBoxes[i].Y + allBoxes[i].Height;
            if (bottom > maxY) maxY = bottom;
        }

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

        // Content area height for pagination (page height minus vertical margins)
        float paginationHeight = pageHeightPx - _marginTopPx * 2;
        if (paginationHeight <= 0) paginationHeight = pageHeightPx;

        // Find body's margin-bottom so we can use it as a bottom snap zone:
        // content should not start within this margin of the page bottom edge.
        float bodyMarginBottomPx = 0;
        foreach (var child in layoutRoot.Children)
        {
            if (child is LayoutBox lb && lb.Element?.TagName == "body")
            {
                bodyMarginBottomPx = lb.MarginBottom;
                break;
            }
        }
        // Clamp so the snap zone can't exceed half the page (degenerate case)
        if (bodyMarginBottomPx > paginationHeight / 2)
            bodyMarginBottomPx = 0;

        // Fill remaining pages using content area height, with smart page breaking
        // that avoids cutting through content boxes OR placing content within the
        // body's bottom margin zone (which would leave no visual padding at the page bottom).
        while (currentTop < maxY)
        {
            float naiveBottom = currentTop + paginationHeight;
            // Effective soft boundary: boxes must not START in the bottom margin zone
            float effectiveBottom = naiveBottom - bodyMarginBottomPx;
            float smartBottom = naiveBottom;

            foreach (var box in allBoxes)
            {
                float bTop = box.Y;
                float bBottom = box.Y + box.Height;
                if (bTop > currentTop && bTop < naiveBottom && box.Height <= paginationHeight)
                {
                    // Case 1: box straddles the hard page boundary
                    bool straddles = bBottom > naiveBottom;
                    // Case 2: box starts inside the bottom margin zone (too close to page edge)
                    bool inMarginZone = bTop >= effectiveBottom;
                    if ((straddles || inMarginZone) && bTop < smartBottom)
                        smartBottom = bTop;
                }
            }

            // Guard against degenerate case where smartBottom snapped to currentTop
            if (smartBottom <= currentTop)
                smartBottom = naiveBottom;

            float bottom = Math.Min(smartBottom, maxY);
            pageBounds.Add((currentTop, bottom));
            currentTop = bottom;
        }

        if (pageBounds.Count == 0)
            pageBounds.Add((0, maxY));

        // Render each page
        foreach (var (pageTopPx, pageBottomPx) in pageBounds)
        {
            var page = pdfDoc.AddPage(pageWidthPt, pageHeightPt);

            // Paint white page canvas background (matches browser default canvas color).
            // Without this, PDF viewers render the transparent page as off-white, causing
            // visible differences against explicitly white-background elements in browsers.
            page.AddRectangle(0, 0, pageWidthPt, pageHeightPt, 1f, 1f, 1f);

            // Paint boxes that fall on this page
            foreach (var box in allBoxes)
            {
                float boxTop = box.Y;
                float boxBottom = box.Y + box.Height;

                // Text boxes are assigned to exactly one page: the page where their top falls.
                // This prevents text from appearing duplicated when a text line straddles a page boundary.
                // Non-text boxes (backgrounds, borders) use overlap check so they cover their full area.
                bool skip;
                if (!string.IsNullOrEmpty(box.Text))
                    skip = boxTop < pageTopPx || boxTop >= pageBottomPx;
                else
                    skip = boxBottom <= pageTopPx || boxTop >= pageBottomPx;

                if (skip) continue;

                // Adjust Y coordinate relative to this page, offset by top margin
                float adjustedY = box.Y - pageTopPx + _marginTopPx;
                PaintBox(page, box, pageHeightPt, pageHeightPx, adjustedY);
            }
        }

        // Convert headings to PDF bookmarks
        if (headings.Count > 0)
        {
            var bookmarks = new List<PdfBookmark>();
            foreach (var (title, level, yPx) in headings)
            {
                // Determine which page this heading falls on
                int pageIndex = 0;
                float localYPx = yPx;
                for (int i = 0; i < pageBounds.Count; i++)
                {
                    if (yPx >= pageBounds[i].top && yPx < pageBounds[i].bottom)
                    {
                        pageIndex = i;
                        localYPx = yPx - pageBounds[i].top;
                        break;
                    }
                    // If heading Y is beyond the last page, assign to last page
                    if (i == pageBounds.Count - 1)
                    {
                        pageIndex = i;
                        localYPx = yPx - pageBounds[i].top;
                    }
                }

                // Convert from CSS px (top-left origin) to PDF pt (bottom-left origin)
                float topPt = (pageHeightPx - localYPx - _marginTopPx) * PdfCoordinates.PxToPt;

                bookmarks.Add(new PdfBookmark
                {
                    Title = title,
                    Level = level,
                    PageIndex = pageIndex,
                    TopPt = topPt
                });
            }
            pdfDoc.SetBookmarks(bookmarks);
        }
    }

    private static void CollectPaintableBoxes(LayoutBox box, List<LayoutBox> result)
    {
        // A box is paintable if it has text, background, image, border, or is a link
        bool hasBorder = false;
        foreach (var side in BorderSides)
        {
            var sideStyle = box.Style.Get($"border-{side}-style") ?? box.Style.Get("border-style");
            if (!string.IsNullOrEmpty(sideStyle) && sideStyle != "none" && sideStyle != "hidden")
            { hasBorder = true; break; }
        }

        var bgImageStyle = box.Style.Get("background-image");
        bool hasBgImage = !string.IsNullOrEmpty(bgImageStyle) && bgImageStyle != "none";

        bool hasPaint = !string.IsNullOrEmpty(box.Text) ||
                        !string.IsNullOrEmpty(box.ImageSource) ||
                        hasBorder || hasBgImage ||
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
        // Apply margin left offset: shift all X coordinates by the page margin
        float effectiveX = box.X + _marginLeftPx;

        // Visibility:hidden - box takes space but is not painted
        var visibility = box.Style.Get("visibility");
        if (visibility == "hidden" || visibility == "collapse")
            return;

        // CSS transform: wrap entire box painting in SaveState/cm/RestoreState
        bool hasTransform = ApplyTransform(page, box, pageHeightPx, adjustedY, effectiveX);

        // Overflow:hidden clipping
        var overflow = box.Style.Get("overflow");
        bool hasClip = overflow == "hidden" || overflow == "clip";
        if (hasClip)
        {
            float clipX = effectiveX * PdfCoordinates.PxToPt;
            float clipY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
            float clipW = box.Width * PdfCoordinates.PxToPt;
            float clipH = box.Height * PdfCoordinates.PxToPt;
            page.SaveState();
            page.AddClipRect(clipX, clipY, clipW, clipH);
        }

        // CSS opacity property
        var opacityStr = box.Style.Get("opacity");
        float cssOpacity = 1f;
        if (!string.IsNullOrEmpty(opacityStr) && float.TryParse(opacityStr,
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float op))
            cssOpacity = Math.Max(0, Math.Min(1, op));

        // Resolve border-radius values
        float tlr = ResolveBorderRadius(box.Style, "border-top-left-radius", box.Width);
        float trr = ResolveBorderRadius(box.Style, "border-top-right-radius", box.Width);
        float brr = ResolveBorderRadius(box.Style, "border-bottom-right-radius", box.Width);
        float blr = ResolveBorderRadius(box.Style, "border-bottom-left-radius", box.Width);
        bool hasRadius = tlr > 0 || trr > 0 || brr > 0 || blr > 0;

        // Convert radii from px to pt
        float tlrPt = tlr * PdfCoordinates.PxToPt;
        float trrPt = trr * PdfCoordinates.PxToPt;
        float brrPt = brr * PdfCoordinates.PxToPt;
        float blrPt = blr * PdfCoordinates.PxToPt;

        // Anonymous text boxes (no element) inherit parent's style but should not paint
        // their own backgrounds or borders — those belong to the parent element box only.
        bool isAnonymousTextBox = box.Element == null && box.Text != null;

        // Paint box-shadow (before background)
        if (!isAnonymousTextBox)
            PaintBoxShadow(page, box, effectiveX, pageHeightPx, adjustedY);

        // Paint background
        if (!isAnonymousTextBox)
        {
            var bgColor = box.Style.BackgroundColor;
            if (!string.IsNullOrEmpty(bgColor) && bgColor != "transparent")
            {
                var color = ParseColor(bgColor);
                if (color.HasValue)
                {
                    float bgAlpha = (color.Value.A / 255f) * cssOpacity;
                    if (bgAlpha < 1f)
                        page.SetOpacity(bgAlpha);

                    float pdfX = effectiveX * PdfCoordinates.PxToPt;
                    float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
                    float pdfW = box.Width * PdfCoordinates.PxToPt;
                    float pdfH = box.Height * PdfCoordinates.PxToPt;

                    if (hasRadius)
                        page.AddRoundedRectangle(pdfX, pdfY, pdfW, pdfH,
                            color.Value.R / 255f, color.Value.G / 255f, color.Value.B / 255f,
                            tlrPt, trrPt, brrPt, blrPt);
                    else
                        page.AddRectangle(pdfX, pdfY, pdfW, pdfH,
                            color.Value.R / 255f, color.Value.G / 255f, color.Value.B / 255f);
                }
            }

            // Paint background-image
            var bgImage = box.Style.Get("background-image");
            if (!string.IsNullOrEmpty(bgImage) && bgImage != "none" && _currentPdfDoc != null)
            {
                PaintBackgroundImage(page, box, bgImage, effectiveX, pageHeightPx, adjustedY);
            }

            // Paint border (per-side with style support)
            PaintBorders(page, box, effectiveX, pageHeightPt, pageHeightPx, adjustedY, hasRadius, tlrPt, trrPt, brrPt, blrPt);
        }

        // Paint outline (outside border, doesn't affect layout)
        var outlineStyle = box.Style.Get("outline-style");
        if (!string.IsNullOrEmpty(outlineStyle) && outlineStyle != "none")
        {
            float outlineWidth = BlockLayout.ResolveLength(box.Style.Get("outline-width"), 0, 16);
            if (outlineWidth <= 0) outlineWidth = 1;
            var outlineColorStr = box.Style.Get("outline-color");
            Color outlineColor = ParseColor(outlineColorStr ?? "") ?? Color.Black;

            float owPt = outlineWidth * PdfCoordinates.PxToPt;
            float olX = (effectiveX - outlineWidth) * PdfCoordinates.PxToPt;
            float olY = (pageHeightPx - adjustedY - box.Height - outlineWidth) * PdfCoordinates.PxToPt;
            float olW = (box.Width + outlineWidth * 2) * PdfCoordinates.PxToPt;
            float olH = (box.Height + outlineWidth * 2) * PdfCoordinates.PxToPt;
            page.AddStrokeRectangle(olX, olY, olW, olH,
                outlineColor.R / 255f, outlineColor.G / 255f, outlineColor.B / 255f, owPt);
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

            // Apply BiDi reordering for RTL text
            if (Text.BidiAlgorithm.ContainsRTL(paintText))
            {
                var direction = box.Style.Get("direction");
                bool baseRTL = direction == "rtl";
                var (visual, _) = Text.BidiAlgorithm.Reorder(paintText, baseRTL);
                paintText = visual;
            }

            float fontSize = 16;
            var fsStr = box.Style.FontSize;
            if (!string.IsNullOrEmpty(fsStr))
            {
                float resolved = BlockLayout.ResolveLength(fsStr, 0, 16);
                if (resolved > 0) fontSize = resolved;
            }

            // text-overflow: ellipsis — truncate text that overflows container
            var textOverflow = box.Style.Get("text-overflow");
            var ellipsisOverflow = box.Style.Get("overflow");
            if (textOverflow == "ellipsis" && (ellipsisOverflow == "hidden" || ellipsisOverflow == "clip"))
            {
                float availWidth = box.Width - box.PaddingLeft - box.PaddingRight;
                float textWidth = TextMeasurer.MeasureWidth(paintText, fontSize,
                    box.Style.FontFamily, box.Style.FontWeight, box.Style.Get("font-style"));
                if (textWidth > availWidth && paintText.Length > 3)
                {
                    float ellipsisWidth = TextMeasurer.MeasureWidth("...", fontSize,
                        box.Style.FontFamily, box.Style.FontWeight, box.Style.Get("font-style"));
                    float targetWidth = availWidth - ellipsisWidth;
                    // Binary search for truncation point
                    int lo = 1, hi = paintText.Length;
                    while (lo < hi)
                    {
                        int mid = (lo + hi + 1) / 2;
                        float w = TextMeasurer.MeasureWidth(paintText.Substring(0, mid), fontSize,
                            box.Style.FontFamily, box.Style.FontWeight, box.Style.Get("font-style"));
                        if (w <= targetWidth) lo = mid;
                        else hi = mid - 1;
                    }
                    paintText = paintText.Substring(0, lo) + "...";
                }
            }

            string fontName = StandardFontMetrics.ResolvePdfFontName(
                box.Style.FontFamily, box.Style.FontWeight, box.Style.Get("font-style"));

            float pdfFontSize = fontSize * PdfCoordinates.PxToPt;
            float textX = effectiveX + box.PaddingLeft;

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

            // Vertical-align baseline shift (sup/sub/super/sub)
            var verticalAlign = box.Style.Get("vertical-align");
            if (!string.IsNullOrEmpty(verticalAlign))
            {
                if (verticalAlign == "super" || verticalAlign == "sup")
                    pdfY += fontSize * 0.4f * PdfCoordinates.PxToPt; // shift up
                else if (verticalAlign == "sub")
                    pdfY -= fontSize * 0.2f * PdfCoordinates.PxToPt; // shift down
            }

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

            // Text shadow: render shadow text before the main text
            var textShadow = box.Style.Get("text-shadow");
            if (!string.IsNullOrEmpty(textShadow) && textShadow != "none")
            {
                ParseTextShadow(textShadow, fontSize, out float shX, out float shY, out float shR, out float shG, out float shB, out float shA);
                if (shA > 0)
                {
                    float shadowPdfX = pdfX + shX * PdfCoordinates.PxToPt;
                    float shadowPdfY = pdfY - shY * PdfCoordinates.PxToPt; // PDF Y is inverted
                    if (shA < 1f) page.SetOpacity(shA);
                    page.AddText(paintText, shadowPdfX, shadowPdfY, fontName, pdfFontSize, shR, shG, shB, letterSpacing, wordSpacing);
                    if (shA < 1f) page.SetOpacity(1f);
                }
            }

            // Use CIDFont glyph IDs for embedded fonts, or WinAnsi for built-in fonts
            if (_currentPdfDoc != null && _currentPdfDoc.IsEmbeddedFont(fontName))
            {
                var glyphIds = _currentPdfDoc.GetGlyphIds(fontName, paintText);
                if (glyphIds != null && glyphIds.Length > 0)
                {
                    page.AddTextCID(glyphIds, pdfX, pdfY, fontName, pdfFontSize,
                        color?.R / 255f ?? 0, color?.G / 255f ?? 0, color?.B / 255f ?? 0,
                        letterSpacing, wordSpacing);
                }
                else
                {
                    page.AddText(paintText, pdfX, pdfY, fontName, pdfFontSize,
                        color?.R / 255f ?? 0, color?.G / 255f ?? 0, color?.B / 255f ?? 0,
                        letterSpacing, wordSpacing);
                }
            }
            else
            {
                page.AddText(paintText, pdfX, pdfY, fontName, pdfFontSize,
                    color?.R / 255f ?? 0, color?.G / 255f ?? 0, color?.B / 255f ?? 0,
                    letterSpacing, wordSpacing);
            }

            // Text decoration (underline, line-through)
            var textDecoration = box.Style.Get("text-decoration");
            if (!string.IsNullOrEmpty(textDecoration) && textDecoration != "none")
            {
                float lineY;
                // Use actual measured text width for decoration, not the box's content width
                float measuredPx = TextMeasurer.MeasureWidth(paintText, fontSize,
                    box.Style.FontFamily, box.Style.FontWeight, box.Style.Get("font-style"));
                float textWidth = measuredPx * PdfCoordinates.PxToPt;
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
                if (textDecoration.IndexOf("overline", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    lineY = pdfY + fontSize * 0.85f * PdfCoordinates.PxToPt;
                    page.AddLine(pdfX, lineY, pdfX + textWidth, lineY, dr, dg, db, decoLineWidth);
                }
            }
        }

        // Paint image (with object-fit support)
        if (!string.IsNullOrEmpty(box.ImageSource) && box.ImageData != null)
        {
            float pdfX = effectiveX * PdfCoordinates.PxToPt;
            float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
            float pdfW = box.Width * PdfCoordinates.PxToPt;
            float pdfH = box.Height * PdfCoordinates.PxToPt;

            // Apply object-fit
            var objectFit = box.Style.Get("object-fit");
            if (objectFit == "contain" || objectFit == "cover")
            {
                // Get natural image dimensions
                float natW = box.ImageData.Length > 0 ? pdfW : pdfW; // approximation
                float natH = pdfH;
                float scaleX = pdfW / Math.Max(natW, 1);
                float scaleY = pdfH / Math.Max(natH, 1);
                float scale = objectFit == "contain" ? Math.Min(scaleX, scaleY) : Math.Max(scaleX, scaleY);
                float fitW = natW * scale;
                float fitH = natH * scale;
                // Center the image
                pdfX += (pdfW - fitW) / 2;
                pdfY += (pdfH - fitH) / 2;
                pdfW = fitW;
                pdfH = fitH;
            }
            // object-fit: none would use natural size (not scaled)
            // object-fit: fill (default) uses the box dimensions as-is

            string imgName = "Img" + box.ImageSource.GetHashCode().ToString("X8");
            page.AddImage(imgName, pdfX, pdfY, pdfW, pdfH);
        }

        // Paint inline SVG
        if (box.Element?.TagName == "svg")
        {
            var svgElement = Svg.SvgParser.Parse(box.Element);
            if (svgElement != null)
            {
                float pdfX = effectiveX * PdfCoordinates.PxToPt;
                float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
                float pdfW = box.Width * PdfCoordinates.PxToPt;
                float pdfH = box.Height * PdfCoordinates.PxToPt;
                string svgCommands = Svg.SvgRenderer.Render(svgElement, pdfX, pdfY, pdfW, pdfH);
                page.AppendRawContent(svgCommands);
            }
        }

        // Paint links
        if (box.Element?.TagName == "a")
        {
            var href = box.Element.GetAttribute("href");
            if (!string.IsNullOrEmpty(href) && href.StartsWith("http"))
            {
                float pdfX = effectiveX * PdfCoordinates.PxToPt;
                float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
                float pdfW = box.Width * PdfCoordinates.PxToPt;
                float pdfH = box.Height * PdfCoordinates.PxToPt;
                page.AddLink(pdfX, pdfY, pdfW, pdfH, href);
            }
        }

        // Restore clipping state
        if (hasClip)
            page.RestoreState();

        // Restore transform state
        if (hasTransform)
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

    private static float ResolveBorderRadius(Css.ComputedStyle style, string property, float boxWidth)
    {
        // Try specific corner property first, then shorthand
        var value = style.Get(property) ?? style.Get("border-radius");
        if (string.IsNullOrEmpty(value) || value == "0" || value == "0px")
            return 0;

        float resolved = Layout.BlockLayout.ResolveLength(value, boxWidth, 16);
        return Math.Max(0, resolved);
    }

    /// <summary>Paint a background-image: url(...) or gradient behind the element.</summary>
    private static void PaintBackgroundImage(PdfPage page, LayoutBox box, string bgImage,
        float effectiveX, float pageHeightPx, float adjustedY)
    {
        // Handle CSS gradients
        if (bgImage.StartsWith("linear-gradient(", StringComparison.OrdinalIgnoreCase) ||
            bgImage.StartsWith("repeating-linear-gradient(", StringComparison.OrdinalIgnoreCase))
        {
            float pdfX = effectiveX * PdfCoordinates.PxToPt;
            float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
            float pdfW = box.Width * PdfCoordinates.PxToPt;
            float pdfH = box.Height * PdfCoordinates.PxToPt;
            var commands = Pdf.PdfGradient.RenderLinearGradient(bgImage, pdfX, pdfY, pdfW, pdfH);
            if (commands != null) page.AppendRawContent(commands);
            return;
        }
        if (bgImage.StartsWith("radial-gradient(", StringComparison.OrdinalIgnoreCase))
        {
            float pdfX = effectiveX * PdfCoordinates.PxToPt;
            float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
            float pdfW = box.Width * PdfCoordinates.PxToPt;
            float pdfH = box.Height * PdfCoordinates.PxToPt;
            var commands = Pdf.PdfRadialGradient.Render(bgImage, pdfX, pdfY, pdfW, pdfH);
            if (commands != null) page.AppendRawContent(commands);
            return;
        }

        // Extract URL from "url(...)" or "url('...')" or "url("...")"
        string? url = null;
        if (bgImage.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            int start = 4;
            int end = bgImage.Length - 1;
            if (end > start)
            {
                url = bgImage.Substring(start, end - start).Trim();
                // Remove quotes
                if (url.Length >= 2 && ((url[0] == '\'' && url[url.Length - 1] == '\'') ||
                    (url[0] == '"' && url[url.Length - 1] == '"')))
                    url = url.Substring(1, url.Length - 2);
            }
        }

        if (string.IsNullOrEmpty(url)) return;

        // Load image data
        var data = LoadBackgroundImageData(url);
        if (data == null || data.Length == 0) return;

        // Register image with PDF document
        string imgName = "BgImg" + url.GetHashCode().ToString("X8");
        Pdf.PdfImage? pdfImage = null;

        if (data.Length >= 8 && data[0] == 137 && data[1] == 80 && data[2] == 78 && data[3] == 71)
            pdfImage = Pdf.PdfImage.FromPng(imgName, data);
        else if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8)
            pdfImage = Pdf.PdfImage.FromJpeg(imgName, data);
        else if (data.Length >= 4 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46)
            pdfImage = Pdf.PdfImage.FromGif(imgName, data);

        if (pdfImage == null) return;

        _currentPdfDoc?.AddImage(pdfImage);

        // Parse background-size
        float imgW = box.Width;
        float imgH = box.Height;
        var bgSize = box.Style.Get("background-size");
        if (!string.IsNullOrEmpty(bgSize) && bgSize != "auto")
        {
            if (bgSize == "cover")
            {
                float scaleX = box.Width / pdfImage.Width;
                float scaleY = box.Height / pdfImage.Height;
                float scale = Math.Max(scaleX, scaleY);
                imgW = pdfImage.Width * scale;
                imgH = pdfImage.Height * scale;
            }
            else if (bgSize == "contain")
            {
                float scaleX = box.Width / pdfImage.Width;
                float scaleY = box.Height / pdfImage.Height;
                float scale = Math.Min(scaleX, scaleY);
                imgW = pdfImage.Width * scale;
                imgH = pdfImage.Height * scale;
            }
            else
            {
                // Try px values
                var parts = bgSize.Split(SpaceSep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1)
                {
                    float? w = ResolveOptionalLength(parts[0], box.Width);
                    if (w.HasValue) imgW = w.Value;
                }
                if (parts.Length >= 2)
                {
                    float? h = ResolveOptionalLength(parts[1], box.Height);
                    if (h.HasValue) imgH = h.Value;
                }
            }
        }

        // Parse background-position (simplified: px or keywords)
        float bgX = 0, bgY = 0;
        var bgPos = box.Style.Get("background-position");
        if (!string.IsNullOrEmpty(bgPos))
        {
            var parts = bgPos.Split(SpaceSep, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1)
            {
                if (parts[0] == "center") bgX = (box.Width - imgW) / 2;
                else if (parts[0] == "right") bgX = box.Width - imgW;
                else { var v = ResolveOptionalLength(parts[0], box.Width); if (v.HasValue) bgX = v.Value; }
            }
            if (parts.Length >= 2)
            {
                if (parts[1] == "center") bgY = (box.Height - imgH) / 2;
                else if (parts[1] == "bottom") bgY = box.Height - imgH;
                else { var v = ResolveOptionalLength(parts[1], box.Height); if (v.HasValue) bgY = v.Value; }
            }
        }

        // Parse background-repeat
        var bgRepeat = box.Style.Get("background-repeat") ?? "repeat";

        // Calculate PDF coordinates
        float baseX = effectiveX + bgX;
        float baseY = adjustedY + bgY;

        if (bgRepeat == "no-repeat")
        {
            float pdfX = baseX * PdfCoordinates.PxToPt;
            float pdfY = (pageHeightPx - baseY - imgH) * PdfCoordinates.PxToPt;
            float pdfW = imgW * PdfCoordinates.PxToPt;
            float pdfH = imgH * PdfCoordinates.PxToPt;
            page.AddImage(imgName, pdfX, pdfY, pdfW, pdfH);
        }
        else
        {
            // Tile the image
            bool repeatX = bgRepeat == "repeat" || bgRepeat == "repeat-x";
            bool repeatY = bgRepeat == "repeat" || bgRepeat == "repeat-y";

            float startX = repeatX ? 0 : bgX;
            float endX = repeatX ? box.Width : bgX + imgW;
            float startY = repeatY ? 0 : bgY;
            float endY = repeatY ? box.Height : bgY + imgH;

            for (float ty = startY; ty < endY; ty += imgH)
            {
                for (float tx = startX; tx < endX; tx += imgW)
                {
                    float pdfX = (effectiveX + tx) * PdfCoordinates.PxToPt;
                    float pdfY = (pageHeightPx - adjustedY - ty - imgH) * PdfCoordinates.PxToPt;
                    float pdfW = imgW * PdfCoordinates.PxToPt;
                    float pdfH = imgH * PdfCoordinates.PxToPt;
                    page.AddImage(imgName, pdfX, pdfY, pdfW, pdfH);
                }
            }
        }
    }

    private static byte[]? LoadBackgroundImageData(string url)
    {
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int comma = url.IndexOf(',');
            if (comma > 0)
            {
                try { return Convert.FromBase64String(url.Substring(comma + 1)); }
                catch { return null; }
            }
        }
        try { if (System.IO.File.Exists(url)) return System.IO.File.ReadAllBytes(url); }
        catch { }
        return null;
    }

    private static float? ResolveOptionalLength(string value, float containingSize)
    {
        if (string.IsNullOrEmpty(value) || value == "auto") return null;
        if (value.EndsWith("%"))
        {
            if (float.TryParse(value.TrimEnd('%'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float pct))
                return containingSize * pct / 100f;
        }
        return BlockLayout.ResolveLength(value, containingSize, 16);
    }

    /// <summary>Paint all four borders with per-side style, width, and color support.</summary>
    private static void PaintBorders(PdfPage page, LayoutBox box, float effectiveX,
        float pageHeightPt, float pageHeightPx, float adjustedY,
        bool hasRadius, float tlrPt, float trrPt, float brrPt, float blrPt)
    {
        // Get common fallback values
        var fallbackStyle = box.Style.Get("border-style");
        var fallbackWidth = box.Style.Get("border-width");
        var fallbackColor = box.Style.Get("border-color");

        // Per-side values
        var sides = BorderSides;
        string[] sideStyles = new string[4];
        float[] sideWidths = new float[4];
        float[] sideR = new float[4], sideG = new float[4], sideB = new float[4];
        bool anyBorder = false;
        bool allSolid = true;

        for (int s = 0; s < 4; s++)
        {
            string side = sides[s];
            sideStyles[s] = box.Style.Get($"border-{side}-style") ?? fallbackStyle ?? "";
            if (string.IsNullOrEmpty(sideStyles[s]) || sideStyles[s] == "none" || sideStyles[s] == "hidden")
            {
                sideWidths[s] = 0;
                continue;
            }

            anyBorder = true;
            if (sideStyles[s] != "solid") allSolid = false;

            var widthStr = box.Style.Get($"border-{side}-width") ?? fallbackWidth;
            sideWidths[s] = 1;
            if (!string.IsNullOrEmpty(widthStr))
            {
                sideWidths[s] = Layout.BlockLayout.ResolveLength(widthStr, 0, 16);
                // Explicit 0 means no border (e.g. border-collapse zeroed edges)
                if (sideWidths[s] <= 0) { sideWidths[s] = 0; continue; }
            }

            var colorStr = box.Style.Get($"border-{side}-color") ?? fallbackColor;
            if (!string.IsNullOrEmpty(colorStr))
            {
                var bc = ParseColor(colorStr);
                if (bc.HasValue) { sideR[s] = bc.Value.R / 255f; sideG[s] = bc.Value.G / 255f; sideB[s] = bc.Value.B / 255f; }
            }
        }

        if (!anyBorder) return;

        float pdfX = effectiveX * PdfCoordinates.PxToPt;
        float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
        float pdfW = box.Width * PdfCoordinates.PxToPt;
        float pdfH = box.Height * PdfCoordinates.PxToPt;

        // If all sides are solid and same style, use existing rectangle stroke (fast path)
        if (allSolid && hasRadius)
        {
            page.AddStrokeRoundedRectangle(pdfX, pdfY, pdfW, pdfH,
                sideR[0], sideG[0], sideB[0], sideWidths[0] * PdfCoordinates.PxToPt,
                tlrPt, trrPt, brrPt, blrPt);
            return;
        }

        if (allSolid && !hasRadius &&
            sideWidths[0] == sideWidths[1] && sideWidths[1] == sideWidths[2] && sideWidths[2] == sideWidths[3] &&
            sideR[0] == sideR[1] && sideR[1] == sideR[2] && sideR[2] == sideR[3])
        {
            page.AddStrokeRectangle(pdfX, pdfY, pdfW, pdfH,
                sideR[0], sideG[0], sideB[0], sideWidths[0] * PdfCoordinates.PxToPt);
            return;
        }

        // Per-side rendering (handles different styles, widths, colors)
        // Top border: left-top corner to right-top corner
        if (sideWidths[0] > 0)
        {
            float y = pdfY + pdfH; // top edge
            page.AddBorderLine(pdfX, y, pdfX + pdfW, y,
                sideR[0], sideG[0], sideB[0], sideWidths[0] * PdfCoordinates.PxToPt, sideStyles[0]);
        }

        // Right border: right-top to right-bottom
        if (sideWidths[1] > 0)
        {
            float x = pdfX + pdfW; // right edge
            page.AddBorderLine(x, pdfY + pdfH, x, pdfY,
                sideR[1], sideG[1], sideB[1], sideWidths[1] * PdfCoordinates.PxToPt, sideStyles[1]);
        }

        // Bottom border: right-bottom to left-bottom
        if (sideWidths[2] > 0)
        {
            float y = pdfY; // bottom edge
            page.AddBorderLine(pdfX + pdfW, y, pdfX, y,
                sideR[2], sideG[2], sideB[2], sideWidths[2] * PdfCoordinates.PxToPt, sideStyles[2]);
        }

        // Left border: left-bottom to left-top
        if (sideWidths[3] > 0)
        {
            page.AddBorderLine(pdfX, pdfY, pdfX, pdfY + pdfH,
                sideR[3], sideG[3], sideB[3], sideWidths[3] * PdfCoordinates.PxToPt, sideStyles[3]);
        }
    }

    // ===== CSS Transform support =====

    /// <summary>A 2D affine transform matrix [a, b, c, d, e, f].</summary>
    private struct Matrix2D
    {
        public float A, B, C, D, E, F;

        public static Matrix2D Identity => new Matrix2D { A = 1, B = 0, C = 0, D = 1, E = 0, F = 0 };

        public Matrix2D Multiply(Matrix2D o)
        {
            return new Matrix2D
            {
                A = A * o.A + C * o.B,
                B = B * o.A + D * o.B,
                C = A * o.C + C * o.D,
                D = B * o.C + D * o.D,
                E = A * o.E + C * o.F + E,
                F = B * o.E + D * o.F + F
            };
        }
    }

    /// <summary>Parse a CSS transform property value into a combined affine matrix.</summary>
    internal static bool TryParseTransformMatrix(string value, float boxWidth, float boxHeight,
        out float a, out float b, out float c, out float d, out float e, out float f)
    {
        a = 1; b = 0; c = 0; d = 1; e = 0; f = 0;

        if (string.IsNullOrEmpty(value) || value == "none")
            return false;

        var result = Matrix2D.Identity;
        int pos = 0;
        bool hasTransform = false;

        while (pos < value.Length)
        {
            while (pos < value.Length && char.IsWhiteSpace(value[pos])) pos++;
            if (pos >= value.Length) break;

            int nameStart = pos;
            while (pos < value.Length && value[pos] != '(') pos++;
            if (pos >= value.Length) break;

            string funcName = value.Substring(nameStart, pos - nameStart).Trim();
            pos++; // skip '('

            int argsStart = pos;
            int depth = 1;
            while (pos < value.Length && depth > 0)
            {
                if (value[pos] == '(') depth++;
                else if (value[pos] == ')') depth--;
                if (depth > 0) pos++;
            }
            if (pos > value.Length) break;

            string argsStr = value.Substring(argsStart, pos - argsStart);
            if (pos < value.Length) pos++; // skip ')'

            var args = ParseTransformArgs(argsStr);
            var m = EvaluateTransformFunction(funcName, args, boxWidth, boxHeight);
            if (m.HasValue)
            {
                result = result.Multiply(m.Value);
                hasTransform = true;
            }
        }

        if (!hasTransform)
            return false;

        if (Math.Abs(result.A - 1) < 0.0001f && Math.Abs(result.B) < 0.0001f &&
            Math.Abs(result.C) < 0.0001f && Math.Abs(result.D - 1) < 0.0001f &&
            Math.Abs(result.E) < 0.0001f && Math.Abs(result.F) < 0.0001f)
            return false;

        a = result.A; b = result.B; c = result.C; d = result.D; e = result.E; f = result.F;
        return true;
    }

    private static float[] ParseTransformArgs(string argsStr)
    {
        var result = new List<float>();
        var parts = argsStr.Split(CommaSpaceSep, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (string.IsNullOrEmpty(part)) continue;
            result.Add(ParseAngleOrLength(part));
        }
        return result.ToArray();
    }

    private static float ParseAngleOrLength(string value)
    {
        value = value.Trim();

        if (value.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(value.Substring(0, value.Length - 3),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float deg))
                return deg * (float)(Math.PI / 180.0);
            return 0;
        }
        if (value.EndsWith("rad", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(value.Substring(0, value.Length - 3),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float rad))
                return rad;
            return 0;
        }
        if (value.EndsWith("turn", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(value.Substring(0, value.Length - 4),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float turn))
                return turn * (float)(2.0 * Math.PI);
            return 0;
        }
        if (value.EndsWith("grad", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(value.Substring(0, value.Length - 4),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float grad))
                return grad * (float)(Math.PI / 200.0);
            return 0;
        }

        return BlockLayout.ResolveLength(value, 0, 16);
    }

    private static Matrix2D? EvaluateTransformFunction(string name, float[] args, float boxWidth, float boxHeight)
    {
        switch (name)
        {
            case "translate":
            {
                float tx = args.Length > 0 ? args[0] : 0;
                float ty = args.Length > 1 ? args[1] : 0;
                return new Matrix2D { A = 1, B = 0, C = 0, D = 1, E = tx, F = ty };
            }
            case "translateX":
            {
                float tx = args.Length > 0 ? args[0] : 0;
                return new Matrix2D { A = 1, B = 0, C = 0, D = 1, E = tx, F = 0 };
            }
            case "translateY":
            {
                float ty = args.Length > 0 ? args[0] : 0;
                return new Matrix2D { A = 1, B = 0, C = 0, D = 1, E = 0, F = ty };
            }
            case "rotate":
            {
                float angle = args.Length > 0 ? args[0] : 0;
                float cos = (float)Math.Cos(angle);
                float sin = (float)Math.Sin(angle);
                return new Matrix2D { A = cos, B = sin, C = -sin, D = cos, E = 0, F = 0 };
            }
            case "scale":
            {
                float sx = args.Length > 0 ? args[0] : 1;
                float sy = args.Length > 1 ? args[1] : sx;
                return new Matrix2D { A = sx, B = 0, C = 0, D = sy, E = 0, F = 0 };
            }
            case "scaleX":
            {
                float sx = args.Length > 0 ? args[0] : 1;
                return new Matrix2D { A = sx, B = 0, C = 0, D = 1, E = 0, F = 0 };
            }
            case "scaleY":
            {
                float sy = args.Length > 0 ? args[0] : 1;
                return new Matrix2D { A = 1, B = 0, C = 0, D = sy, E = 0, F = 0 };
            }
            case "skew":
            {
                float ax = args.Length > 0 ? args[0] : 0;
                float ay = args.Length > 1 ? args[1] : 0;
                return new Matrix2D { A = 1, B = (float)Math.Tan(ay), C = (float)Math.Tan(ax), D = 1, E = 0, F = 0 };
            }
            case "skewX":
            {
                float ax = args.Length > 0 ? args[0] : 0;
                return new Matrix2D { A = 1, B = 0, C = (float)Math.Tan(ax), D = 1, E = 0, F = 0 };
            }
            case "skewY":
            {
                float ay = args.Length > 0 ? args[0] : 0;
                return new Matrix2D { A = 1, B = (float)Math.Tan(ay), C = 0, D = 1, E = 0, F = 0 };
            }
            case "matrix":
            {
                if (args.Length >= 6)
                    return new Matrix2D { A = args[0], B = args[1], C = args[2], D = args[3], E = args[4], F = args[5] };
                return null;
            }
            default:
                return null;
        }
    }

    private static void ResolveTransformOrigin(LayoutBox box, float pageHeightPx, float adjustedY,
        float effectiveX, out float originXPt, out float originYPt)
    {
        var originStr = box.Style.Get("transform-origin");
        float oxPx, oyPx;

        if (string.IsNullOrEmpty(originStr))
        {
            oxPx = effectiveX + box.Width / 2f;
            oyPx = adjustedY + box.Height / 2f;
        }
        else
        {
            var parts = originStr.Split(SpaceSep, StringSplitOptions.RemoveEmptyEntries);
            float oxLocal = ResolveOriginComponent(parts.Length > 0 ? parts[0] : "50%", box.Width);
            float oyLocal = ResolveOriginComponent(parts.Length > 1 ? parts[1] : "50%", box.Height);
            oxPx = effectiveX + oxLocal;
            oyPx = adjustedY + oyLocal;
        }

        originXPt = oxPx * PdfCoordinates.PxToPt;
        originYPt = (pageHeightPx - oyPx) * PdfCoordinates.PxToPt;
    }

    private static float ResolveOriginComponent(string value, float dimension)
    {
        value = value.Trim();
        switch (value)
        {
            case "left":
            case "top":
                return 0;
            case "center":
                return dimension / 2f;
            case "right":
            case "bottom":
                return dimension;
            default:
                if (value.EndsWith("%"))
                {
                    if (float.TryParse(value.Substring(0, value.Length - 1),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float pct))
                        return dimension * pct / 100f;
                    return dimension / 2f;
                }
                return BlockLayout.ResolveLength(value, dimension, 16);
        }
    }

    /// <summary>
    /// Apply a CSS transform. Returns true if transform was applied (caller must RestoreState).
    /// </summary>
    private static bool ApplyTransform(PdfPage page, LayoutBox box,
        float pageHeightPx, float adjustedY, float effectiveX)
    {
        var transformStr = box.Style.Get("transform");
        if (string.IsNullOrEmpty(transformStr) || transformStr == "none")
            return false;

        if (!TryParseTransformMatrix(transformStr, box.Width, box.Height,
                out float ma, out float mb, out float mc, out float md, out float me, out float mf))
            return false;

        ResolveTransformOrigin(box, pageHeightPx, adjustedY, effectiveX,
            out float oxPt, out float oyPt);

        // Convert CSS translation (px) to PDF points, flip Y for PDF coordinate system
        float ePt = me * PdfCoordinates.PxToPt;
        float fPt = -mf * PdfCoordinates.PxToPt;

        // Flip sin components for PDF coordinate system (Y up vs CSS Y down)
        float pdfA = ma;
        float pdfB = -mb;
        float pdfC = -mc;
        float pdfD = md;

        // Compose: translate(origin) * matrix * translate(-origin)
        float finalE = oxPt + ePt - pdfA * oxPt - pdfC * oyPt;
        float finalF = oyPt + fPt - pdfB * oxPt - pdfD * oyPt;

        page.SaveState();
        page.ConcatMatrix(pdfA, pdfB, pdfC, pdfD, finalE, finalF);
        return true;
    }

    /// <summary>
    /// Sort boxes by z-index stacking order. Non-positioned boxes stay in document order,
    /// positioned boxes with z-index sort ascending (higher z-index paints later = on top).
    /// </summary>
    private static void SortByZIndex(List<LayoutBox> boxes)
    {
        // Must use stable sort to preserve document order within same z-index.
        // List.Sort() is unstable and scrambles equal elements, causing backgrounds
        // to paint after text (hiding it). Use OrderBy which is stable.
        var sorted = boxes.OrderBy(b => GetZIndex(b)).ToList();
        boxes.Clear();
        boxes.AddRange(sorted);
    }

    private static int GetZIndex(LayoutBox box)
    {
        var zStr = box.Style.Get("z-index");
        if (!string.IsNullOrEmpty(zStr) && zStr != "auto" &&
            int.TryParse(zStr, out int z))
            return z;
        return 0; // auto = 0
    }

    private static Color? ParseColor(string value)
    {
        return Color.TryParse(value);
    }

    /// <summary>Parse text-shadow: offsetX offsetY [blur] [color]</summary>
    private static void ParseTextShadow(string shadow, float fontSize,
        out float x, out float y, out float r, out float g, out float b, out float a)
    {
        x = y = 0; r = g = b = 0; a = 0.5f;
        if (string.IsNullOrEmpty(shadow) || shadow == "none") return;

        var parts = shadow.Trim().Split(SpaceSep, StringSplitOptions.RemoveEmptyEntries);
        int numIdx = 0;
        string? colorStr = null;

        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            // Try as a length value
            if (p.EndsWith("px") || p.EndsWith("em") || p.EndsWith("rem") ||
                (p.Length > 0 && (char.IsDigit(p[0]) || p[0] == '-' || p[0] == '.')))
            {
                float val = Layout.BlockLayout.ResolveLength(p, 0, fontSize);
                if (numIdx == 0) x = val;
                else if (numIdx == 1) y = val;
                // numIdx == 2 would be blur radius (ignored for now)
                numIdx++;
            }
            else
            {
                // Accumulate color tokens (might be "rgba(0, 0, 0, 0.3)" split across parts)
                colorStr = colorStr == null ? p : colorStr + " " + p;
            }
        }

        if (!string.IsNullOrEmpty(colorStr))
        {
            var c = ParseColor(colorStr);
            if (c.HasValue)
            {
                r = c.Value.R / 255f;
                g = c.Value.G / 255f;
                b = c.Value.B / 255f;
                a = c.Value.A / 255f;
            }
        }

        if (a <= 0 && x == 0 && y == 0) a = 0; // no shadow
        else if (a <= 0) a = 0.5f; // default opacity
    }

    /// <summary>Paint box-shadow behind an element.</summary>
    private static void PaintBoxShadow(PdfPage page, LayoutBox box, float effectiveX,
        float pageHeightPx, float adjustedY)
    {
        var shadow = box.Style.Get("box-shadow");
        if (string.IsNullOrEmpty(shadow) || shadow == "none") return;

        float fontSize = 16;
        var fsStr = box.Style.FontSize;
        if (!string.IsNullOrEmpty(fsStr))
        {
            float resolved = Layout.BlockLayout.ResolveLength(fsStr, 0, 16);
            if (resolved > 0) fontSize = resolved;
        }

        // Parse: offsetX offsetY [blur] [spread] [color]
        var parts = shadow.Trim().Split(SpaceSep, StringSplitOptions.RemoveEmptyEntries);
        float sx = 0, sy = 0, blur = 0, spread = 0;
        string? colorStr = null;
        int numIdx = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p.EndsWith("px") || p.EndsWith("em") ||
                (p.Length > 0 && (char.IsDigit(p[0]) || p[0] == '-' || p[0] == '.')))
            {
                float val = Layout.BlockLayout.ResolveLength(p, 0, fontSize);
                if (numIdx == 0) sx = val;
                else if (numIdx == 1) sy = val;
                else if (numIdx == 2) blur = val;
                else if (numIdx == 3) spread = val;
                numIdx++;
            }
            else
            {
                colorStr = colorStr == null ? p : colorStr + " " + p;
            }
        }

        float sr = 0, sg = 0, sb = 0;
        float sa = 0.3f;
        if (!string.IsNullOrEmpty(colorStr))
        {
            var c = ParseColor(colorStr);
            if (c.HasValue)
            {
                sr = c.Value.R / 255f;
                sg = c.Value.G / 255f;
                sb = c.Value.B / 255f;
                sa = c.Value.A / 255f;
            }
        }

        float pdfX = (effectiveX + sx - spread) * PdfCoordinates.PxToPt;
        float pdfY = (pageHeightPx - adjustedY - box.Height - sy - spread) * PdfCoordinates.PxToPt;
        float pdfW = (box.Width + spread * 2) * PdfCoordinates.PxToPt;
        float pdfH = (box.Height + spread * 2) * PdfCoordinates.PxToPt;

        if (sa < 1f) page.SetOpacity(sa);
        page.AddRectangle(pdfX, pdfY, pdfW, pdfH, sr, sg, sb);
        if (sa < 1f) page.SetOpacity(1f);
    }
}
