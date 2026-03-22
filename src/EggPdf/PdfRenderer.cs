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
    /// <summary>Page margin offsets in CSS pixels, applied when painting boxes.</summary>
    [ThreadStatic]
    private static float _marginLeftPx;
    [ThreadStatic]
    private static float _marginTopPx;

    public static void Render(LayoutBox layoutRoot, PdfDocument pdfDoc,
        float pageWidthPt, float pageHeightPt, float pageHeightPx,
        float marginLeftPx = 0, float marginTopPx = 0)
    {
        _marginLeftPx = marginLeftPx;
        _marginTopPx = marginTopPx;

        try
        {
            RenderCore(layoutRoot, pdfDoc, pageWidthPt, pageHeightPt, pageHeightPx);
        }
        finally
        {
            _marginLeftPx = 0;
            _marginTopPx = 0;
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

        // Content area height for pagination (page height minus vertical margins)
        float paginationHeight = pageHeightPx - _marginTopPx * 2;
        if (paginationHeight <= 0) paginationHeight = pageHeightPx;

        // Fill remaining pages using content area height
        while (currentTop < maxY)
        {
            float bottom = Math.Min(currentTop + paginationHeight, maxY);
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

        // Paint background
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

        // Paint border (per-side with style support)
        PaintBorders(page, box, effectiveX, pageHeightPt, pageHeightPx, adjustedY, hasRadius, tlrPt, trrPt, brrPt, blrPt);

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
            float pdfX = effectiveX * PdfCoordinates.PxToPt;
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
        string[] sides = { "top", "right", "bottom", "left" };
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
                sideWidths[s] = Layout.BlockLayout.ResolveLength(widthStr, 0, 16);
            if (sideWidths[s] <= 0) sideWidths[s] = 1;

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
        var parts = argsStr.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
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
            var parts = originStr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
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

    private static Color? ParseColor(string value)
    {
        return Color.TryParse(value);
    }
}
