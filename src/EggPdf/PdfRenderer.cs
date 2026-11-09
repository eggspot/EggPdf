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

internal static class PdfRenderer
{
    [ThreadStatic]
    private static float _marginTopPx;
    [ThreadStatic]
    private static float _marginBottomPx;

    public static void Render(LayoutBox layoutRoot, PdfDocument pdfDoc,
        float pageWidthPt, float pageHeightPt, float pageHeightPx,
        float marginLeftPx = 0, float marginTopPx = 0, float marginBottomPx = 0,
        List<LayoutBox>? marginBoxes = null, int pageNumberOffset = 0, int? totalPagesOverride = null)
    {
        BoxPainter.MarginLeftPx = marginLeftPx;
        _marginTopPx = marginTopPx;
        _marginBottomPx = marginBottomPx;
        BoxPainter.CurrentPdfDoc = pdfDoc;

        try
        {
            RenderCore(layoutRoot, pdfDoc, pageWidthPt, pageHeightPt, pageHeightPx, marginBoxes,
                pageNumberOffset, totalPagesOverride);
        }
        finally
        {
            BoxPainter.MarginLeftPx = 0;
            _marginTopPx = 0;
            _marginBottomPx = 0;
            BoxPainter.CurrentPdfDoc = null;
        }
    }

    private static void RenderCore(LayoutBox layoutRoot, PdfDocument pdfDoc,
        float pageWidthPt, float pageHeightPt, float pageHeightPx,
        List<LayoutBox>? marginBoxes = null, int pageNumberOffset = 0, int? totalPagesOverride = null)
    {
        var allPaintableBoxes = new List<LayoutBox>();
        PageFragmenter.PropagateAncestorOpacity(layoutRoot);
        PageFragmenter.CollectPaintableBoxes(layoutRoot, allPaintableBoxes);

        Dictionary<LayoutBox, LayoutBox>? ancestorMap = null;
        Dictionary<LayoutBox, List<LayoutBox>>? clipAncestorCache = null;
        if (TreeHasPaintClip(layoutRoot))
        {
            ancestorMap = new Dictionary<LayoutBox, LayoutBox>();
            BuildAncestorMap(layoutRoot, null, ancestorMap);
            clipAncestorCache = new Dictionary<LayoutBox, List<LayoutBox>>();
        }

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

        if (marginBoxes != null && marginBoxes.Count > 0)
            fixedBoxes.AddRange(marginBoxes);

        BoxPainter.SortByZIndex(allBoxes);
        BoxPainter.SortByZIndex(fixedBoxes);

        var headings = new List<(string title, int level, float yPx)>();
        PageFragmenter.CollectHeadings(layoutRoot, headings);

        if (allBoxes.Count == 0)
        {
            var blankPage = pdfDoc.AddPage(pageWidthPt, pageHeightPt);
            blankPage.AddRectangle(0, 0, pageWidthPt, pageHeightPt, 1f, 1f, 1f);
            BoxPainter.PaintFixedBoxes(blankPage, fixedBoxes, pageHeightPt, pageHeightPx, pageNumberOffset + 1, totalPagesOverride ?? 1,
                _marginTopPx, layoutRoot.Width, pageHeightPx - _marginTopPx - _marginBottomPx);
            return;
        }

        var pageBreakYs = new List<float>();
        PageFragmenter.CollectPageBreaks(layoutRoot, pageBreakYs);
        pageBreakYs.Sort();

        var avoidBreakBoxes = new List<(float y, float height)>();
        PageFragmenter.CollectBreakInsideAvoid(layoutRoot, avoidBreakBoxes);

        var orphanWidowBlocks = new List<PageFragmenter.OrphanWidowBlock>();
        PageFragmenter.CollectOrphansWidowsBlocks(layoutRoot, orphanWidowBlocks);

        float maxY = 0;
        for (int i = 0; i < allBoxes.Count; i++)
        {
            float bottom = allBoxes[i].Y + allBoxes[i].Height;
            if (bottom > maxY) maxY = bottom;
        }

        float paginationHeight = pageHeightPx - _marginTopPx - _marginBottomPx;
        if (paginationHeight <= 0) paginationHeight = pageHeightPx;

        float bodyMarginBottomPx = 0;
        foreach (var child in layoutRoot.Children)
        {
            if (child is LayoutBox lb && lb.Element?.TagName == "body")
            {
                bodyMarginBottomPx = lb.MarginBottom;
                break;
            }
        }
        if (bodyMarginBottomPx > paginationHeight / 2)
            bodyMarginBottomPx = 0;

        var hardBoundaries = new List<float>();
        foreach (float breakY in pageBreakYs)
            if (breakY > 0 && breakY < maxY) hardBoundaries.Add(breakY);
        hardBoundaries.Sort();

        var pageBounds = new List<(float top, float bottom)>();
        var pageCapacityBottom = new List<float>();
        float currentTop = 0;
        int hardBoundaryIndex = 0;

        var tableHeaders = new List<PageFragmenter.TableHeaderInfo>();
        PageFragmenter.CollectRepeatingTableHeaders(layoutRoot, tableHeaders);
        var breakCandidates = new List<(float Top, float Bottom)>(allBoxes.Count);
        foreach (var box in allBoxes)
        {
            if (box.Height <= paginationHeight && PageFragmenter.IsUnsplittable(box))
                breakCandidates.Add((box.Y, box.Y + box.Height));
        }
        breakCandidates.Sort((a, b) => a.Top.CompareTo(b.Top));

        while (currentTop < maxY)
        {
            while (hardBoundaryIndex < hardBoundaries.Count && hardBoundaries[hardBoundaryIndex] <= currentTop)
                hardBoundaryIndex++;
            float nextHardBoundary = hardBoundaryIndex < hardBoundaries.Count
                ? hardBoundaries[hardBoundaryIndex] : float.MaxValue;

            float theadReserve = 0f;
            foreach (var header in tableHeaders)
            {
                if (header.TheadBox.Y < currentTop && header.TableBottom > currentTop && header.TheadBox.Height < paginationHeight / 2f)
                    theadReserve += header.TheadBox.Height;
            }
            float naiveBottom = Math.Min(currentTop + paginationHeight - theadReserve, nextHardBoundary);
            float effectiveBottom = naiveBottom - bodyMarginBottomPx;
            float smartBottom = naiveBottom;

            for (int ci = FirstCandidateAfter(breakCandidates, currentTop); ci < breakCandidates.Count; ci++)
            {
                var (bTop, bBottom) = breakCandidates[ci];
                if (bTop >= naiveBottom) break;
                bool straddles = bBottom > naiveBottom;
                bool inMarginZone = bTop >= effectiveBottom;
                if ((straddles || inMarginZone) && bTop < smartBottom)
                    smartBottom = bTop;
            }

            foreach (var header in tableHeaders)
            {
                float theadTop = header.TheadBox.Y;
                if (theadTop > currentTop && theadTop < naiveBottom && header.FirstBodyRowBottom > naiveBottom &&
                    header.FirstBodyRowBottom - theadTop <= paginationHeight && theadTop < smartBottom)
                    smartBottom = theadTop;
            }

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

            for (int oi = 0; oi < orphanWidowBlocks.Count; oi++)
            {
                float bTop    = orphanWidowBlocks[oi].Y;
                float bBottom = bTop + orphanWidowBlocks[oi].Height;
                var   lineYs  = orphanWidowBlocks[oi].LineYs;

                if (bTop < currentTop || bTop >= naiveBottom || bBottom <= naiveBottom)
                    continue;
                if (lineYs.Length < 2)
                    continue;

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
                    if (bTop > currentTop && bTop < smartBottom)
                        smartBottom = bTop;
                }
                else if (widowCount > 0 && widowCount < minWidows && lineYs.Length > minWidows)
                {
                    int lastOnCurrentIdx = lineYs.Length - minWidows - 1;
                    if (lastOnCurrentIdx >= 0)
                    {
                        float newBreakY = lineYs[lastOnCurrentIdx];
                        if (newBreakY > currentTop && newBreakY < smartBottom)
                            smartBottom = newBreakY;
                    }
                    else
                    {
                        if (bTop > currentTop && bTop < smartBottom)
                            smartBottom = bTop;
                    }
                }
            }

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
                    if (markBox.Y + markBox.Height > pBottom) break;

                    float pageBottomCapacity = pageCapacityBottom[pi];
                    float thresholdY = markBox.Y;

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
                if (pTop >= th.TableBottom) break;

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

        var boxesByPage = AssignBoxesToPages(allBoxes, pageBounds);

        int renderPageIndex = 0;
        foreach (var (pageTopPx, pageBottomPx) in pageBounds)
        {
            var page = pdfDoc.AddPage(pageWidthPt, pageHeightPt);

            page.AddRectangle(0, 0, pageWidthPt, pageHeightPt, 1f, 1f, 1f);

            float shiftThreshold = pageShiftThresholdY[renderPageIndex];
            float shiftDelta = pageShiftDelta[renderPageIndex];
            float theadDelta = theadShiftDelta[renderPageIndex];

            void PaintWithAncestorClips(LayoutBox target, float adjY, Action paint)
            {
                if (ancestorMap == null)
                {
                    paint();
                    return;
                }

                if (!clipAncestorCache!.TryGetValue(target, out var clipAncestors))
                    clipAncestorCache[target] = clipAncestors = GetClipAncestorsOutermostFirst(target, ancestorMap);

                if (clipAncestors.Count == 0)
                {
                    paint();
                    return;
                }

                page.SaveState();
                foreach (var ancestor in clipAncestors)
                {
                    float aEffectiveY = ancestor.Y;
                    if (shiftDelta > 0 && ancestor.Y >= shiftThreshold) aEffectiveY += shiftDelta;
                    if (theadDelta > 0) aEffectiveY += theadDelta;

                    float aVisibleTop = Math.Max(aEffectiveY, pageTopPx);
                    float aVisibleBottom = Math.Min(aEffectiveY + ancestor.Height, pageBottomPx);
                    float aClampedHeight = Math.Max(0f, aVisibleBottom - aVisibleTop);
                    if (aClampedHeight <= 0) continue;

                    float aAdjustedY = aVisibleTop - pageTopPx + _marginTopPx;
                    float aEffectiveX = ancestor.X + BoxPainter.MarginLeftPx;
                    float clipXpt = aEffectiveX * PdfCoordinates.PxToPt;
                    float clipYpt = (pageHeightPx - aAdjustedY - aClampedHeight) * PdfCoordinates.PxToPt;
                    float clipWpt = ancestor.Width * PdfCoordinates.PxToPt;
                    float clipHpt = aClampedHeight * PdfCoordinates.PxToPt;
                    bool wholeOnThisPage = aEffectiveY >= pageTopPx &&
                                           (aEffectiveY + ancestor.Height <= pageBottomPx || renderPageIndex == pageBounds.Count - 1);
                    if (wholeOnThisPage)
                        BoxPainter.ClipToBox(page, ancestor, clipXpt,
                            (pageHeightPx - aAdjustedY - ancestor.Height) * PdfCoordinates.PxToPt, clipWpt, ancestor.Height * PdfCoordinates.PxToPt);
                    else
                        page.AddClipRect(clipXpt, clipYpt, clipWpt, clipHpt);
                }
                paint();
                page.RestoreState();
            }

            foreach (var box in boxesByPage[renderPageIndex])
            {
                float effectiveY = box.Y;
                if (shiftDelta > 0 && box.Y >= shiftThreshold)
                    effectiveY += shiftDelta;
                if (theadDelta > 0)
                    effectiveY += theadDelta;

                float adjustedY = effectiveY - pageTopPx + _marginTopPx;

                bool crossesPageEdge = string.IsNullOrEmpty(box.Text) &&
                                       (effectiveY < pageTopPx && renderPageIndex > 0 || effectiveY + box.Height > pageBottomPx);
                if (crossesPageEdge)
                {
                    float bandHeightPx = pageBottomPx - pageTopPx + theadDelta;
                    page.SaveState();
                    page.AddClipRect(0f, (pageHeightPx - _marginTopPx - bandHeightPx) * PdfCoordinates.PxToPt,
                        pageWidthPt, bandHeightPx * PdfCoordinates.PxToPt);
                    PaintWithAncestorClips(box, adjustedY,
                        () => BoxPainter.PaintBox(page, box, pageHeightPt, pageHeightPx, adjustedY));
                    page.RestoreState();
                }
                else
                {
                    PaintWithAncestorClips(box, adjustedY,
                        () => BoxPainter.PaintBox(page, box, pageHeightPt, pageHeightPx, adjustedY));
                }
            }

            if (theadRepeatsByPage.TryGetValue(renderPageIndex, out var repeats))
            {
                foreach (var (rbox, radjY) in repeats)
                    PaintWithAncestorClips(rbox, radjY,
                        () => BoxPainter.PaintBox(page, rbox, pageHeightPt, pageHeightPx, radjY));
            }

            BoxPainter.PaintFixedBoxes(page, fixedBoxes, pageHeightPt, pageHeightPx,
                pageNumberOffset + renderPageIndex + 1, totalPagesOverride ?? pageBounds.Count,
                _marginTopPx, layoutRoot.Width, pageHeightPx - _marginTopPx - _marginBottomPx);

            renderPageIndex++;
        }

        if (headings.Count > 0)
        {
            var bookmarks = new List<PdfBookmark>();
            foreach (var (title, level, yPx) in headings)
            {
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
                    if (i == pageBounds.Count - 1)
                    {
                        pageIndex = i;
                        localYPx = yPx - pageBounds[i].top;
                    }
                }

                float topPt = (pageHeightPx - localYPx - _marginTopPx) * PdfCoordinates.PxToPt;

                bookmarks.Add(new PdfBookmark
                {
                    Title = title,
                    Level = level,
                    PageIndex = pageIndex + pageNumberOffset,
                    TopPt = topPt
                });
            }
            pdfDoc.AppendBookmarks(bookmarks);
        }
    }

    private static int FirstCandidateAfter(List<(float Top, float Bottom)> candidates, float y)
    {
        int low = 0, high = candidates.Count;
        while (low < high)
        {
            int mid = (low + high) >> 1;
            if (candidates[mid].Top > y) high = mid;
            else low = mid + 1;
        }
        return low;
    }

    private static List<LayoutBox>[] AssignBoxesToPages(List<LayoutBox> boxes, List<(float top, float bottom)> pageBounds)
    {
        var byPage = new List<LayoutBox>[pageBounds.Count];
        for (int i = 0; i < byPage.Length; i++) byPage[i] = new List<LayoutBox>();
        foreach (var box in boxes)
        {
            float top = box.Y, bottom = box.Y + box.Height;
            int page = PageContaining(pageBounds, top);
            if (!string.IsNullOrEmpty(box.Text))
            {
                if (top < pageBounds[page].bottom) byPage[page].Add(box);
                continue;
            }
            if (top >= pageBounds[page].bottom) continue;
            byPage[page].Add(box);
            for (int p = page + 1; p < pageBounds.Count && bottom > pageBounds[p].top; p++)
                byPage[p].Add(box);
        }
        return byPage;
    }

    private static int PageContaining(List<(float top, float bottom)> pageBounds, float y)
    {
        int low = 0, high = pageBounds.Count - 1;
        while (low < high)
        {
            int mid = (low + high + 1) >> 1;
            if (pageBounds[mid].top <= y) low = mid;
            else high = mid - 1;
        }
        return low;
    }

    private static bool TreeHasPaintClip(LayoutBox box)
    {
        if (HasPaintClip(box.Style)) return true;
        foreach (var child in box.Children)
            if (TreeHasPaintClip(child)) return true;
        return false;
    }

    private static void BuildAncestorMap(LayoutBox box, LayoutBox? parent, Dictionary<LayoutBox, LayoutBox> map)
    {
        if (parent != null) map[box] = parent;
        foreach (var child in box.Children)
            BuildAncestorMap(child, box, map);
    }

    private static bool HasPaintClip(ComputedStyle style)
    {
        var overflow = style.Get("overflow");
        if (overflow == "hidden" || overflow == "clip") return true;

        var contain = style.Get("contain");
        return !string.IsNullOrEmpty(contain) &&
            (contain!.IndexOf("paint", StringComparison.OrdinalIgnoreCase) >= 0 ||
             contain.IndexOf("strict", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static List<LayoutBox> GetClipAncestorsOutermostFirst(LayoutBox box, Dictionary<LayoutBox, LayoutBox> ancestorMap)
    {
        var result = new List<LayoutBox>();
        var current = box;
        while (ancestorMap.TryGetValue(current, out var parent))
        {
            if (HasPaintClip(parent.Style)) result.Add(parent);
            current = parent;
        }
        result.Reverse();
        return result;
    }
}
