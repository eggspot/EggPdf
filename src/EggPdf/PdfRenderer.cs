using System;
using System.Collections.Generic;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Core;
using EggPdf.Html.Dom;
using EggPdf.Layout;
using EggPdf.Pdf;
using EggPdf.Fragmentation;
using EggPdf.Paint;

namespace EggPdf;

/// <summary>
/// Renders layout boxes to PDF pages. Handles pagination (splitting content
/// across multiple pages when it exceeds page height).
/// </summary>
internal static class PdfRenderer
{
    /// <summary>Page margin offsets in CSS pixels, applied when painting boxes.</summary>
    [ThreadStatic]
    private static float _marginTopPx;
    [ThreadStatic]
    private static float _marginBottomPx;

    public static void Render(LayoutBox layoutRoot, PdfDocument pdfDoc,
        float pageWidthPt, float pageHeightPt, float pageHeightPx,
        float marginLeftPx = 0, float marginTopPx = 0, float marginBottomPx = 0,
        List<LayoutBox>? marginBoxes = null)
    {
        BoxPainter.MarginLeftPx = marginLeftPx;
        _marginTopPx = marginTopPx;
        _marginBottomPx = marginBottomPx;
        BoxPainter.CurrentPdfDoc = pdfDoc;

        try
        {
            RenderCore(layoutRoot, pdfDoc, pageWidthPt, pageHeightPt, pageHeightPx, marginBoxes);
        }
        finally
        {
            BoxPainter.MarginLeftPx = 0;
            _marginTopPx = 0;
            _marginBottomPx = 0;
            BoxPainter.CurrentPdfDoc = null;
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
        float pageWidthPt, float pageHeightPt, float pageHeightPx,
        List<LayoutBox>? marginBoxes = null)
    {
        // Collect all leaf boxes (boxes with text or background)
        var allPaintableBoxes = new List<LayoutBox>();
        PageFragmenter.CollectPaintableBoxes(layoutRoot, allPaintableBoxes);

        // position:fixed content repeats identically on every physical page: its containing
        // block is the page origin (0,0), not the document flow (see BlockLayout's
        // LayoutAbsoluteChildren), so its Y/X are already page-local rather than document-
        // absolute. Split these out from normal flow boxes so Y-band pagination below doesn't
        // assign them to a single page — they're instead repainted once per page further down.
        var allBoxes = new List<LayoutBox>();
        var fixedBoxes = new List<LayoutBox>();
        var fixedBoxSet = new HashSet<LayoutBox>();
        PageFragmenter.CollectFixedPositionedBoxes(layoutRoot, false, fixedBoxSet);
        for (int fi = 0; fi < allPaintableBoxes.Count; fi++)
        {
            if (fixedBoxSet.Contains(allPaintableBoxes[fi]))
                fixedBoxes.Add(allPaintableBoxes[fi]);
            else
                allBoxes.Add(allPaintableBoxes[fi]);
        }

        // @page margin box content (@top-center, @bottom-right, etc.) repeats
        // per physical page exactly like position:fixed content, including
        // counter(page)/counter(pages) sentinel substitution.
        if (marginBoxes != null && marginBoxes.Count > 0)
            fixedBoxes.AddRange(marginBoxes);

        // Sort by z-index stacking order: non-positioned first (doc order),
        // then positioned elements sorted by z-index ascending (higher = painted later = on top)
        BoxPainter.SortByZIndex(allBoxes);
        BoxPainter.SortByZIndex(fixedBoxes);

        // Also collect heading boxes for bookmarks
        var headings = new List<(string title, int level, float yPx)>();
        PageFragmenter.CollectHeadings(layoutRoot, headings);

        if (allBoxes.Count == 0)
        {
            var blankPage = pdfDoc.AddPage(pageWidthPt, pageHeightPt);
            blankPage.AddRectangle(0, 0, pageWidthPt, pageHeightPt, 1f, 1f, 1f);
            BoxPainter.PaintFixedBoxes(blankPage, fixedBoxes, pageHeightPt, pageHeightPx, pageIndex: 1, totalPages: 1);
            return;
        }

        // Collect forced page break Y positions
        var pageBreakYs = new List<float>();
        PageFragmenter.CollectPageBreaks(layoutRoot, pageBreakYs);
        pageBreakYs.Sort();

        // Collect boxes that must not be split across pages (break-inside: avoid).
        // These may not be in allBoxes (no paint) but still need to be kept whole.
        var avoidBreakBoxes = new List<(float y, float height)>();
        PageFragmenter.CollectBreakInsideAvoid(layoutRoot, avoidBreakBoxes);

        // Collect text blocks with explicit orphans/widows constraints.
        var orphanWidowBlocks = new List<PageFragmenter.OrphanWidowBlock>();
        PageFragmenter.CollectOrphansWidowsBlocks(layoutRoot, orphanWidowBlocks);

        // Determine total content height
        float maxY = 0;
        for (int i = 0; i < allBoxes.Count; i++)
        {
            float bottom = allBoxes[i].Y + allBoxes[i].Height;
            if (bottom > maxY) maxY = bottom;
        }

        // Content area height for pagination (page height minus vertical margins).
        // Both margins must be subtracted individually — asymmetric @page margins
        // (e.g. a tall margin-top with a small margin-bottom) are common, and
        // doubling one of them silently mis-sizes every page's content band.
        float paginationHeight = pageHeightPx - _marginTopPx - _marginBottomPx;
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

        // Real forced-break boundaries (page-break-after/before). A section bounded by one
        // of these can itself be taller than one physical page — a long table or flowing
        // text block inside a page-break-bounded <section> is a common case — so content
        // must not be allowed to cross a forced break onto the same physical page below,
        // but (unlike the document's natural end, maxY) a forced break IS a real constraint
        // on how tall a single page's band can be, not just "where content happens to stop".
        // Treating a whole oversized section as a single physical page (the previous
        // behavior) doesn't just mis-paginate: content past the first page's-worth of
        // that section gets painted at Y coordinates beyond the physical page canvas —
        // present in the PDF's text objects but invisible in any viewer, which is exactly
        // the "content silently vanishes past a certain point" table bug.
        var hardBoundaries = new List<float>();
        foreach (float breakY in pageBreakYs)
            if (breakY > 0 && breakY < maxY) hardBoundaries.Add(breakY);
        hardBoundaries.Sort();

        // pageCapacityBottom tracks, per page, the true usable content-bottom edge (i.e.
        // where this page's content area actually ends — a forced break, a full page's
        // height, or wherever the next smart-break constraint lands) as opposed to
        // pageBounds' bottom, which for the trailing page is clamped down to wherever the
        // content happens to end (maxY). The two coincide except on the last page when its
        // content doesn't fill the whole page — maxY must NOT cap naiveBottom below, or a
        // page's true remaining capacity collapses to wherever content happens to stop.
        var pageBounds = new List<(float top, float bottom)>();
        var pageCapacityBottom = new List<float>();
        float currentTop = 0;
        int hardBoundaryIndex = 0;

        // Fill pages using content area height, with smart page breaking that avoids
        // cutting through content boxes OR placing content within the body's bottom
        // margin zone (which would leave no visual padding at the page bottom) — bounded
        // so a page's band never crosses into the next forced-break section.
        while (currentTop < maxY)
        {
            while (hardBoundaryIndex < hardBoundaries.Count && hardBoundaries[hardBoundaryIndex] <= currentTop)
                hardBoundaryIndex++;
            float nextHardBoundary = hardBoundaryIndex < hardBoundaries.Count
                ? hardBoundaries[hardBoundaryIndex] : float.MaxValue;

            float naiveBottom = Math.Min(currentTop + paginationHeight, nextHardBoundary);
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

            // break-inside: avoid — move page break before any avoid box that would be split
            for (int ai = 0; ai < avoidBreakBoxes.Count; ai++)
            {
                float bTop    = avoidBreakBoxes[ai].y;
                float bHeight = avoidBreakBoxes[ai].height;
                float bBottom = bTop + bHeight;
                if (bTop > currentTop && bTop < naiveBottom && bHeight <= paginationHeight)
                {
                    if (bBottom > naiveBottom && bTop < smartBottom)
                        smartBottom = bTop;
                }
            }

            // orphans / widows check for text blocks
            for (int oi = 0; oi < orphanWidowBlocks.Count; oi++)
            {
                float bTop    = orphanWidowBlocks[oi].Y;
                float bBottom = bTop + orphanWidowBlocks[oi].Height;
                var   lineYs  = orphanWidowBlocks[oi].LineYs;

                // Only check blocks that straddle this page boundary
                if (bTop < currentTop || bTop >= naiveBottom || bBottom <= naiveBottom)
                    continue;
                if (lineYs.Length < 2)
                    continue; // nothing to balance

                // Count lines on current page (orphans) vs next page (widows)
                int orphanCount = 0;
                for (int li = 0; li < lineYs.Length; li++)
                {
                    if (lineYs[li] < naiveBottom)
                        orphanCount++;
                }
                int widowCount = lineYs.Length - orphanCount;

                int minOrphans = orphanWidowBlocks[oi].OrphansValue;
                int minWidows  = orphanWidowBlocks[oi].WidowsValue;

                if (orphanCount > 0 && orphanCount < minOrphans)
                {
                    // Too few lines remain on current page: push break to before the block
                    if (bTop > currentTop && bTop < smartBottom)
                        smartBottom = bTop;
                }
                else if (widowCount > 0 && widowCount < minWidows && lineYs.Length > minWidows)
                {
                    // Too few lines go to next page: move break back so minWidows lines go over
                    int lastOnCurrentIdx = lineYs.Length - minWidows - 1;
                    if (lastOnCurrentIdx >= 0)
                    {
                        float newBreakY = lineYs[lastOnCurrentIdx];
                        if (newBreakY > currentTop && newBreakY < smartBottom)
                            smartBottom = newBreakY;
                    }
                    else
                    {
                        // Can't satisfy widows without moving whole block: push block to next page
                        if (bTop > currentTop && bTop < smartBottom)
                            smartBottom = bTop;
                    }
                }
            }

            // Guard against degenerate case where smartBottom snapped to currentTop
            if (smartBottom <= currentTop)
                smartBottom = naiveBottom;

            float bottom = Math.Min(smartBottom, maxY);
            pageBounds.Add((currentTop, bottom));
            pageCapacityBottom.Add(smartBottom);
            currentTop = bottom;
        }

        if (pageBounds.Count == 0)
        {
            pageBounds.Add((0, maxY));
            pageCapacityBottom.Add(maxY);
        }

        // -eggpdf-pin-bottom: page — visually pin a box (and everything after it in
        // document order on the same page) to that page's bottom content edge, but only
        // when the marked box itself fits entirely within a single page. This is a paint-time
        // offset only (box.Y in the tree is never mutated), so it can't affect pagination itself
        // or how many pages dynamic/flowing content produces above it.
        var pinBottomBoxes = new List<LayoutBox>();
        PageFragmenter.CollectPinBottomBoxes(layoutRoot, pinBottomBoxes);
        var pageShiftThresholdY = new float[pageBounds.Count];
        var pageShiftDelta = new float[pageBounds.Count];
        if (pinBottomBoxes.Count > 0)
        {
            for (int pi = 0; pi < pageBounds.Count; pi++)
                pageShiftThresholdY[pi] = float.MaxValue;

            foreach (var markBox in pinBottomBoxes)
            {
                for (int pi = 0; pi < pageBounds.Count; pi++)
                {
                    var (pTop, pBottom) = pageBounds[pi];
                    if (markBox.Y < pTop || markBox.Y >= pBottom) continue;
                    if (markBox.Y + markBox.Height > pBottom) break; // doesn't fit this one page: leave in natural flow

                    float pageBottomCapacity = pageCapacityBottom[pi];
                    float thresholdY = markBox.Y;

                    // Extent of everything from the marked box through the end of the page,
                    // in its original (unshifted) position.
                    float groupBottom = markBox.Y + markBox.Height;
                    foreach (var b in allBoxes)
                    {
                        if (b.Y >= thresholdY && b.Y < pBottom)
                        {
                            float bBottom = b.Y + b.Height;
                            if (bBottom > groupBottom) groupBottom = bBottom;
                        }
                    }

                    float delta = pageBottomCapacity - groupBottom;
                    if (delta > 0 && thresholdY < pageShiftThresholdY[pi])
                    {
                        pageShiftThresholdY[pi] = thresholdY;
                        pageShiftDelta[pi] = delta;
                    }
                    break;
                }
            }
        }

        // Repeat a <thead> at the top of every page a table's body continues onto.
        // Independent of the pin-bottom shift above (applied additively): every box
        // on a continuation page shifts down by the thead's height to make room,
        // and the thead's own paintable boxes (cells/text/borders) are repainted
        // there with a freshly computed page-local Y -- they still also paint once,
        // normally, via allBoxes on the page they actually start on.
        var tableHeaders = new List<PageFragmenter.TableHeaderInfo>();
        PageFragmenter.CollectRepeatingTableHeaders(layoutRoot, tableHeaders);
        var theadShiftDelta = new float[pageBounds.Count];
        var theadRepeatsByPage = new Dictionary<int, List<(LayoutBox box, float adjustedY)>>();
        foreach (var th in tableHeaders)
        {
            int theadPage = -1;
            for (int pi = 0; pi < pageBounds.Count; pi++)
            {
                var (pTop, pBottom) = pageBounds[pi];
                if (th.TheadBox.Y >= pTop && th.TheadBox.Y < pBottom) { theadPage = pi; break; }
            }
            if (theadPage < 0) continue;

            List<(LayoutBox box, float adjustedY)>? theadPaintables = null;
            for (int pi = theadPage + 1; pi < pageBounds.Count; pi++)
            {
                var (pTop, _) = pageBounds[pi];
                if (pTop >= th.TableBottom) break; // table already ended before this page starts

                if (theadPaintables == null)
                {
                    var raw = new List<LayoutBox>();
                    PageFragmenter.CollectPaintableBoxes(th.TheadBox, raw);
                    theadPaintables = new List<(LayoutBox, float)>(raw.Count);
                    foreach (var b in raw)
                        theadPaintables.Add((b, b.Y - th.TheadBox.Y + _marginTopPx));
                }
                if (!theadRepeatsByPage.TryGetValue(pi, out var list))
                    theadRepeatsByPage[pi] = list = new List<(LayoutBox, float)>();
                list.AddRange(theadPaintables);
                theadShiftDelta[pi] += th.TheadBox.Height;
            }
        }

        // Render each page
        int renderPageIndex = 0;
        foreach (var (pageTopPx, pageBottomPx) in pageBounds)
        {
            var page = pdfDoc.AddPage(pageWidthPt, pageHeightPt);

            // Paint white page canvas background (matches browser default canvas color).
            // Without this, PDF viewers render the transparent page as off-white, causing
            // visible differences against explicitly white-background elements in browsers.
            page.AddRectangle(0, 0, pageWidthPt, pageHeightPt, 1f, 1f, 1f);

            float shiftThreshold = pageShiftThresholdY[renderPageIndex];
            float shiftDelta = pageShiftDelta[renderPageIndex];
            float theadDelta = theadShiftDelta[renderPageIndex];

            // Paint boxes that fall on this page
            foreach (var box in allBoxes)
            {
                float boxTop = box.Y;
                float boxBottom = box.Y + box.Height;

                // Text boxes are assigned to exactly one page: the page where their top falls.
                // This prevents text from appearing duplicated when a text line straddles a page boundary.
                // Non-text boxes (backgrounds, borders) use overlap check so they cover their full area.
                // On the first page, a box can legitimately sit slightly above Y=0 (e.g. a large
                // inline run's vertical-align: baseline shift pulling it just above its line's
                // nominal top) -- there's no earlier page it could belong to instead, so excluding
                // it here would silently drop it rather than just paint it near the top edge.
                float bandTop = renderPageIndex == 0 ? float.NegativeInfinity : pageTopPx;
                bool skip;
                if (!string.IsNullOrEmpty(box.Text))
                    skip = boxTop < bandTop || boxTop >= pageBottomPx;
                else
                    skip = boxBottom <= bandTop || boxTop >= pageBottomPx;

                if (skip) continue;

                float effectiveY = box.Y;
                if (shiftDelta > 0 && box.Y >= shiftThreshold)
                    effectiveY += shiftDelta;
                if (theadDelta > 0)
                    effectiveY += theadDelta;

                // Adjust Y coordinate relative to this page, offset by top margin
                float adjustedY = effectiveY - pageTopPx + _marginTopPx;

                // A box taller than one physical page (e.g. a bordered/backgrounded <section>
                // that now correctly spans multiple pages — see the pagination fix above) only
                // has PART of its height visible on any single page. PaintBox draws background/
                // border/shadow rectangles from box.Height unconditionally; passing the box's
                // full height here would paint those rectangles at the wrong position and size
                // on every page but the one where the box begins — visible as a mispositioned
                // sliver of color instead of a correctly filled page. Clamp to the portion of
                // the box actually visible on this page before painting, then restore.
                float visibleTop = Math.Max(effectiveY, pageTopPx);
                float visibleBottom = Math.Min(effectiveY + box.Height, pageBottomPx);
                float clampedHeight = Math.Max(0f, visibleBottom - visibleTop);

                if (clampedHeight < box.Height)
                {
                    float clampedAdjustedY = visibleTop - pageTopPx + _marginTopPx;
                    float originalHeight = box.Height;
                    box.Height = clampedHeight;
                    BoxPainter.PaintBox(page, box, pageHeightPt, pageHeightPx, clampedAdjustedY);
                    box.Height = originalHeight;
                }
                else
                {
                    BoxPainter.PaintBox(page, box, pageHeightPt, pageHeightPx, adjustedY);
                }
            }

            if (theadRepeatsByPage.TryGetValue(renderPageIndex, out var repeats))
            {
                foreach (var (rbox, radjY) in repeats)
                    BoxPainter.PaintBox(page, rbox, pageHeightPt, pageHeightPx, radjY);
            }

            BoxPainter.PaintFixedBoxes(page, fixedBoxes, pageHeightPt, pageHeightPx,
                pageIndex: renderPageIndex + 1, totalPages: pageBounds.Count);

            renderPageIndex++;
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
}
