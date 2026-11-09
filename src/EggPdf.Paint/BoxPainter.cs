using System;
using System.Collections.Generic;
using System.Globalization;
using EggPdf.Core;
using EggPdf.Css;
using EggPdf.Layout;
using EggPdf.Pdf;
using EggPdf.Svg;
using EggPdf.Text;
using EggPdf.Text.Fallback;
using EggPdf.Text.OpenType;
using EggPdf.Text.TrueType;

namespace EggPdf.Paint;

/// <summary>
/// Paints a laid-out box tree onto a PdfPage: text, backgrounds, borders,
/// shadows, transforms, form-control visuals, and every other per-box
/// visual effect. Split out of PdfRenderer.cs, which mixed this with
/// pagination logic (now in EggPdf.Fragmentation) in one file.
/// </summary>
public static partial class BoxPainter
{
    private const ushort NotdefGlyph = 0;
    private const float SuperscriptRaiseEm = 0.4f;
    private const float SubscriptLowerEm = 0.2f;
    private const float DecorationThicknessEm = 0.05f;
    private const float MinDecorationThicknessPx = 0.5f;
    private const float UnderlineDropEm = 0.15f;
    private const float LineThroughRiseEm = 0.3f;
    private const float OverlineRiseEm = 0.85f;
    private const float EmphasisMarkScale = 0.5f;
    private const float EmphasisOverRiseEm = 0.8f;
    private const float EmphasisUnderDropEm = 0.3f;
    private const float IndeterminateProgressFill = 0.4f;
    private static readonly (float R, float G, float B) DefaultAccentRgb = (0.26f, 0.55f, 0.96f);
    private static readonly (float R, float G, float B) MeterOptimumRgb = (0.2f, 0.7f, 0.2f);
    private static readonly (float R, float G, float B) MeterSuboptimumRgb = (0.9f, 0.8f, 0.1f);
    private static readonly (float R, float G, float B) MeterEvenLessGoodRgb = (0.8f, 0.2f, 0.2f);

    private static (float R, float G, float B) ToRgb(Color color) => (color.R / 255f, color.G / 255f, color.B / 255f);

    private static readonly char[] SpaceSep = { ' ' };
    private static readonly char[] CommaSpaceSep = { ',', ' ' };

    private static readonly string[] BorderStyleKeys = { "border-top-style", "border-right-style", "border-bottom-style", "border-left-style" };
    private static readonly string[] BorderWidthKeys = { "border-top-width", "border-right-width", "border-bottom-width", "border-left-width" };
    private static readonly string[] BorderColorKeys = { "border-top-color", "border-right-color", "border-bottom-color", "border-left-color" };

    /// <summary>
    /// Render context set by PdfRenderer.Render before painting and cleared
    /// after: the page-margin left offset and the document boxes are being
    /// painted into. Thread-static to match PdfRenderer's own render-scoped
    /// fields, which these were split out of.
    /// </summary>
    [ThreadStatic]
    public static float MarginLeftPx;
    [ThreadStatic]
    public static PdfDocument? CurrentPdfDoc;

    /// <summary>
    /// PDF/UA-1 tagging: maps every LayoutBox in the current render to the structure element its
    /// painted content should be attributed to (built by StructureTreeBuilder before painting
    /// starts). Null when the render isn't tagged. Thread-static for the same reason as the
    /// fields above.
    /// </summary>
    [ThreadStatic]
    public static System.Collections.Generic.Dictionary<EggPdf.Layout.LayoutBox, EggPdf.Pdf.PdfStructureElement>? StructureMap;

    /// <summary>
    /// Loads CSS background-image URLs under the current render's resource policy (resolver, local-file
    /// access). Set by the renderer for the duration of a render; when null, only data: URIs and local
    /// files are read. Thread-static for the same reason as the fields above.
    /// </summary>
    [ThreadStatic]
    public static Func<string, byte[]?>? ImageLoader;

    /// <summary>
    /// Repaint every position:fixed box onto the given page. A box placed against the sheet (margin boxes,
    /// the fluent header/footer/watermark) uses its page-local X/Y directly; an author fixed box
    /// (<see cref="LayoutBox.FixedToPageArea"/>) is offset into the page area by the page margins and clipped
    /// to it. Any counter(page)/counter(pages) sentinel left in a box's text by
    /// <see cref="EggPdf.Layout.CssCounterContext.ResolveContent"/> is substituted with this
    /// page's real number before painting, then restored so the next page starts clean.
    /// </summary>
    public static void PaintFixedBoxes(PdfPage page, List<LayoutBox> fixedBoxes,
        float pageHeightPt, float pageHeightPx, int pageIndex, int totalPages) =>
        PaintFixedBoxes(page, fixedBoxes, pageHeightPt, pageHeightPx, pageIndex, totalPages, 0f, 0f, 0f);

    /// <summary>
    /// <see cref="PaintFixedBoxes(PdfPage, List{LayoutBox}, float, float, int, int)"/> with the page area:
    /// its top margin and its width and height in CSS pixels, used for author fixed boxes.
    /// </summary>
    public static void PaintFixedBoxes(PdfPage page, List<LayoutBox> fixedBoxes,
        float pageHeightPt, float pageHeightPx, int pageIndex, int totalPages,
        float pageAreaTopPx, float pageAreaWidthPx, float pageAreaHeightPx)
    {
        var effectiveBoxes = SelectEffectiveMarginBoxes(fixedBoxes, pageIndex);
        for (int i = 0; i < effectiveBoxes.Count; i++)
        {
            var box = effectiveBoxes[i];
            if (box.Text == null && box.Style.Get("-eggpdf-margin-box-position") != null)
                continue;
            string? originalText = box.Text;
            bool textSubstituted = false;

            if (!string.IsNullOrEmpty(originalText) &&
                (originalText.IndexOf(CssCounterContext.PageCounterSentinel, StringComparison.Ordinal) >= 0 ||
                 originalText.IndexOf(CssCounterContext.PagesCounterSentinel, StringComparison.Ordinal) >= 0))
            {
                box.Text = originalText
                    .Replace(CssCounterContext.PageCounterSentinel, pageIndex.ToString(CultureInfo.InvariantCulture))
                    .Replace(CssCounterContext.PagesCounterSentinel, totalPages.ToString(CultureInfo.InvariantCulture));
                textSubstituted = true;
            }

            if (box.FixedToPageArea)
            {
                page.SaveState();
                if (pageAreaWidthPx > 0f && pageAreaHeightPx > 0f)
                    page.AddClipRect(MarginLeftPx * PdfCoordinates.PxToPt, (pageHeightPx - pageAreaTopPx - pageAreaHeightPx) * PdfCoordinates.PxToPt,
                        pageAreaWidthPx * PdfCoordinates.PxToPt, pageAreaHeightPx * PdfCoordinates.PxToPt);
                PaintBox(page, box, pageHeightPt, pageHeightPx, adjustedY: box.Y + pageAreaTopPx);
                page.RestoreState();
            }
            else
            {
                float originalX = box.X;
                box.X = originalX - MarginLeftPx;
                PaintBox(page, box, pageHeightPt, pageHeightPx, adjustedY: box.Y);
                box.X = originalX;
            }
            if (textSubstituted)
                box.Text = originalText;
        }
    }

    private static List<LayoutBox> SelectEffectiveMarginBoxes(List<LayoutBox> fixedBoxes, int pageIndex)
    {
        bool anyTagged = false;
        for (int i = 0; i < fixedBoxes.Count; i++)
        {
            if (fixedBoxes[i].Style.Get("-eggpdf-margin-box-position") != null) { anyTagged = true; break; }
        }
        if (!anyTagged)
            return fixedBoxes;

        string specific = pageIndex == 1 ? "first" : (pageIndex % 2 == 0 ? "left" : "right");
        var specificByPosition = new Dictionary<string, LayoutBox>(StringComparer.OrdinalIgnoreCase);
        var allByPosition = new Dictionary<string, LayoutBox>(StringComparer.OrdinalIgnoreCase);
        var result = new List<LayoutBox>(fixedBoxes.Count);

        foreach (var box in fixedBoxes)
        {
            var pos = box.Style.Get("-eggpdf-margin-box-position");
            if (pos == null) { result.Add(box); continue; }

            var applicability = box.Style.Get("-eggpdf-page-applicability") ?? "all";
            if (string.Equals(applicability, specific, StringComparison.OrdinalIgnoreCase))
                specificByPosition[pos] = box;
            else if (string.Equals(applicability, "all", StringComparison.OrdinalIgnoreCase))
                allByPosition[pos] = box;
        }

        foreach (var kv in allByPosition)
            result.Add(specificByPosition.TryGetValue(kv.Key, out var over) ? over : kv.Value);
        foreach (var kv in specificByPosition)
            if (!allByPosition.ContainsKey(kv.Key))
                result.Add(kv.Value);

        return result;
    }

    /// <summary>The PDF image resource name used for an image source (URL, path or data URI); the same in every run.</summary>
    public static string ImageResourceName(string imageSource) => "Img" + StableHash.Hex(imageSource);

    private static string AnchorIdFromHref(string href)
    {
        var fragment = href.Substring(1);
        try { return Uri.UnescapeDataString(fragment); }
        catch (UriFormatException) { return fragment; }
    }

    private static readonly string[] ExternalLinkSchemes = { "http://", "https://", "mailto:", "tel:" };

    private static bool IsExternalLinkTarget(string href)
    {
        foreach (var scheme in ExternalLinkSchemes)
        {
            if (href.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static void PaintBox(PdfPage page, LayoutBox box,
        float pageHeightPt, float pageHeightPx, float adjustedY)
    {
        var anchorId = box.Element?.GetAttribute("id");
        if (!string.IsNullOrEmpty(anchorId) && CurrentPdfDoc != null)
            CurrentPdfDoc.RegisterAnchor(anchorId!, page.PageIndex, (pageHeightPx - adjustedY) * PdfCoordinates.PxToPt);

        var visibility = box.Style.Get("visibility");
        if (visibility == "hidden" || visibility == "collapse")
            return;

        if (StructureMap != null && StructureMap.TryGetValue(box, out var structElem))
        {
            int mcid = page.BeginMarkedContent(structElem.Type);
            try
            {
                PaintBoxWithOpacity(page, box, pageHeightPt, pageHeightPx, adjustedY);
            }
            finally
            {
                page.EndMarkedContent();
            }
            structElem.AddContentRef(page.PageIndex, mcid);
        }
        else
        {
            PaintBoxWithOpacity(page, box, pageHeightPt, pageHeightPx, adjustedY);
        }
    }

    private static void PaintBoxCore(PdfPage page, LayoutBox box,
        float pageHeightPt, float pageHeightPx, float adjustedY)
    {
        float effectiveX = box.X + MarginLeftPx;

        bool hasTransform = ApplyTransform(page, box, pageHeightPx, adjustedY, effectiveX);

        bool hasWritingModeTransform = ApplyWritingModeTransform(page, box, pageHeightPx, adjustedY, effectiveX);

        bool hasClip = ClipsDescendantPaint(box.Style);
        if (hasClip)
        {
            float clipX = effectiveX * PdfCoordinates.PxToPt;
            float clipY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
            float clipW = box.Width * PdfCoordinates.PxToPt;
            float clipH = box.Height * PdfCoordinates.PxToPt;
            page.SaveState();
            ClipToBox(page, box, clipX, clipY, clipW, clipH);
        }

        var clipPathStr = box.Style.Get("clip-path");
        bool hasCssClipPath = false;
        if (!string.IsNullOrEmpty(clipPathStr) && clipPathStr != "none")
        {
            float cpX = effectiveX * PdfCoordinates.PxToPt;
            float cpY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
            float cpW = box.Width * PdfCoordinates.PxToPt;
            float cpH = box.Height * PdfCoordinates.PxToPt;
            var clipCommands = PdfClipPath.GenerateClipPath(clipPathStr, cpX, cpY, cpW, cpH);
            if (clipCommands != null)
            {
                page.SaveState();
                page.AppendRawContent(clipCommands);
                hasCssClipPath = true;
            }
        }

        var mixBlendMode = box.Style.Get("mix-blend-mode");
        bool hasBlendMode = !string.IsNullOrEmpty(mixBlendMode) && mixBlendMode != "normal";
        if (hasBlendMode)
        {
            page.SaveState();
            page.SetBlendMode(mixBlendMode!);
        }

        var filterStr = box.Style.Get("filter");
        var filterParams = !string.IsNullOrEmpty(filterStr) && filterStr != "none"
            ? EggPdf.Pdf.PdfFilterEffects.Parse(filterStr) : null;

        float tlr = ResolveBorderRadius(box.Style, "border-top-left-radius", box.Width);
        float trr = ResolveBorderRadius(box.Style, "border-top-right-radius", box.Width);
        float brr = ResolveBorderRadius(box.Style, "border-bottom-right-radius", box.Width);
        float blr = ResolveBorderRadius(box.Style, "border-bottom-left-radius", box.Width);
        bool hasRadius = tlr > 0 || trr > 0 || brr > 0 || blr > 0;

        float tlrPt = tlr * PdfCoordinates.PxToPt;
        float trrPt = trr * PdfCoordinates.PxToPt;
        float brrPt = brr * PdfCoordinates.PxToPt;
        float blrPt = blr * PdfCoordinates.PxToPt;

        bool isAnonymousTextBox = box.Element == null && box.Text != null;

        bool suppressEmptyCell = false;
        if (!isAnonymousTextBox)
        {
            var tagName = box.Element?.TagName;
            if ((tagName == "td" || tagName == "th") && box.Style.Get("empty-cells") == "hide")
            {
                bool hasContent = false;
                foreach (var child in box.Children)
                {
                    if (!string.IsNullOrEmpty(child.Text) || !string.IsNullOrEmpty(child.ImageSource))
                    { hasContent = true; break; }
                    foreach (var gc in child.Children)
                    {
                        if (!string.IsNullOrEmpty(gc.Text) || !string.IsNullOrEmpty(gc.ImageSource))
                        { hasContent = true; break; }
                    }
                    if (hasContent) break;
                }
                if (!hasContent) suppressEmptyCell = true;
            }
        }

        if (!isAnonymousTextBox && !suppressEmptyCell)
            PaintBoxShadow(page, box, effectiveX, pageHeightPx, adjustedY);

        if (!isAnonymousTextBox && !suppressEmptyCell)
        {
            var bgBlendMode = box.Style.Get("background-blend-mode");
            bool hasBgBlend = !string.IsNullOrEmpty(bgBlendMode) && bgBlendMode != "normal";
            if (hasBgBlend)
            {
                page.SaveState();
                page.SetBlendMode(bgBlendMode!);
            }

            var bgColor = box.Style.BackgroundColor;
            if (!string.IsNullOrEmpty(bgColor) && bgColor != "transparent")
            {
                var color = ParseColor(bgColor);
                if (color.HasValue)
                {
                    float bgAlpha = color.Value.A / 255f;
                    if (bgAlpha < 1f)
                        SetPaintOpacity(page, bgAlpha);

                    float bgR = color.Value.R / 255f, bgG = color.Value.G / 255f, bgB = color.Value.B / 255f;
                    if (filterParams != null && filterParams.HasEffect)
                        (bgR, bgG, bgB) = EggPdf.Pdf.PdfFilterEffects.ApplyColorFilter(bgR, bgG, bgB, filterParams);

                    float pdfX = effectiveX * PdfCoordinates.PxToPt;
                    float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
                    float pdfW = box.Width * PdfCoordinates.PxToPt;
                    float pdfH = box.Height * PdfCoordinates.PxToPt;

                    if (hasRadius)
                        page.AddRoundedRectangle(pdfX, pdfY, pdfW, pdfH, bgR, bgG, bgB,
                            tlrPt, trrPt, brrPt, blrPt);
                    else
                        page.AddRectangle(pdfX, pdfY, pdfW, pdfH, bgR, bgG, bgB);

                    if (bgAlpha < 1f)
                        SetPaintOpacity(page, 1f);
                }
            }

            var bgImage = box.Style.Get("background-image");
            if (!string.IsNullOrEmpty(bgImage) && bgImage != "none" && CurrentPdfDoc != null)
            {
                var layers = SplitTopLevel(bgImage, ',');
                for (int li = layers.Count - 1; li >= 0; li--)
                {
                    var layer = layers[li].Trim();
                    if (!string.IsNullOrEmpty(layer) && layer != "none")
                        PaintBackgroundImage(page, box, layer, effectiveX, pageHeightPx, adjustedY);
                }
            }

            if (hasBgBlend)
                page.RestoreState();

            PaintBorders(page, box, effectiveX, pageHeightPt, pageHeightPx, adjustedY, hasRadius, tlrPt, trrPt, brrPt, blrPt);
        }

        var elemTagName = box.Element?.TagName;
        if (elemTagName == "progress" || elemTagName == "meter")
        {
            PaintProgressOrMeter(page, box, elemTagName, effectiveX, pageHeightPx, adjustedY, box.Style.Get("accent-color"));
        }
        else if (elemTagName == "input")
        {
            var inputTypeAttr = (box.Element?.GetAttribute("type") ?? "text").ToLowerInvariant();
            if (inputTypeAttr == "checkbox" || inputTypeAttr == "radio")
            {
                var appearanceVal = box.Style.Get("appearance") ?? box.Style.Get("-webkit-appearance");
                if (appearanceVal != "none")
                    PaintCheckboxOrRadio(page, box, inputTypeAttr, effectiveX, pageHeightPx, adjustedY,
                        box.Style.Get("accent-color"), hasRadius, tlrPt, trrPt, brrPt, blrPt);
            }
        }
        else if (elemTagName == "select")
        {
            var appearanceVal = box.Style.Get("appearance") ?? box.Style.Get("-webkit-appearance");
            if (appearanceVal != "none")
                PaintSelectDropdownArrow(page, box, effectiveX, pageHeightPx, adjustedY);
        }

        var outlineStyle = box.Style.Get("outline-style");
        if (!string.IsNullOrEmpty(outlineStyle) && outlineStyle != "none")
        {
            float outlineWidth = BlockLayout.ResolveLength(box.Style.Get("outline-width"), 0, 16);
            if (outlineWidth <= 0) outlineWidth = 1;
            var outlineColorStr = box.Style.Get("outline-color");
            Color outlineColor = ParseColor(outlineColorStr ?? "") ?? Color.Black;

            float outlineOffset = BlockLayout.ResolveLength(box.Style.Get("outline-offset"), 0, 16);
            float owPt = outlineWidth * PdfCoordinates.PxToPt;
            float strokeCentre = outlineOffset + outlineWidth / 2f;
            float olX = (effectiveX - strokeCentre) * PdfCoordinates.PxToPt;
            float olY = (pageHeightPx - adjustedY - box.Height - strokeCentre) * PdfCoordinates.PxToPt;
            float olW = (box.Width + strokeCentre * 2) * PdfCoordinates.PxToPt;
            float olH = (box.Height + strokeCentre * 2) * PdfCoordinates.PxToPt;
            page.AddStrokeRectangle(olX, olY, olW, olH,
                outlineColor.R / 255f, outlineColor.G / 255f, outlineColor.B / 255f, owPt);
        }

        var colCountStr = box.Style.Get("column-count");
        if (!string.IsNullOrEmpty(colCountStr) && colCountStr != "auto" &&
            int.TryParse(colCountStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int colCount) && colCount > 1)
        {
            var ruleShorthand = box.Style.Get("column-rule");
            var ruleWidth = box.Style.Get("column-rule-width");
            var ruleStyle = box.Style.Get("column-rule-style");
            var ruleColorStr = box.Style.Get("column-rule-color");

            if (!string.IsNullOrEmpty(ruleShorthand))
                ParseColumnRuleShorthand(ruleShorthand, ref ruleWidth, ref ruleStyle, ref ruleColorStr);

            bool hasRule = !string.IsNullOrEmpty(ruleStyle) && ruleStyle != "none" &&
                           !string.IsNullOrEmpty(ruleColorStr) && !string.IsNullOrEmpty(ruleWidth);
            if (hasRule)
            {
                float rw = BlockLayout.ResolveLength(ruleWidth!, box.Width, 16);
                var rc = ParseColor(ruleColorStr!);
                if (rw > 0 && rc.HasValue)
                {
                    float rwPt = rw * PdfCoordinates.PxToPt;
                    float topPt = (pageHeightPx - adjustedY) * PdfCoordinates.PxToPt;
                    float bottomPt = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
                    float prevRight = float.MinValue;
                    for (int ci = 0; ci < box.Children.Count; ci++)
                    {
                        var col = box.Children[ci];
                        if (col.Element != null || col.Text != null) continue;
                        if (prevRight > float.MinValue)
                        {
                            float midX = ((prevRight + col.X) / 2f) * PdfCoordinates.PxToPt;
                            page.AddLine(midX, bottomPt, midX, topPt,
                                rc.Value.R / 255f, rc.Value.G / 255f, rc.Value.B / 255f, rwPt);
                        }
                        prevRight = col.X + col.Width;
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(box.Text))
        {
            var textTransform = box.Style.Get("text-transform");
            var paintText = box.Text;
            if (!string.IsNullOrEmpty(textTransform))
            {
                if (textTransform == "uppercase") paintText = paintText.ToUpperInvariant();
                else if (textTransform == "lowercase") paintText = paintText.ToLowerInvariant();
                else if (textTransform == "capitalize") paintText = CapitalizeText(paintText);
            }

            var fontVariant = box.Style.Get("font-variant");
            var fontVariantCaps = box.Style.Get("font-variant-caps");
            bool isSmallCaps = fontVariant == "small-caps" ||
                fontVariantCaps == "small-caps" || fontVariantCaps == "all-small-caps";

            paintText = ArabicShaper.Shape(paintText);

            string logicalPaintText = paintText;

            if (BidiAlgorithm.ContainsRTL(paintText))
            {
                var direction = box.Style.Get("direction");
                bool baseRTL = direction == "rtl";
                var (visual, _) = BidiAlgorithm.Reorder(paintText, baseRTL);
                paintText = visual;
            }

            float fontSize = 16;
            var fsStr = box.Style.FontSize;
            if (!string.IsNullOrEmpty(fsStr))
            {
                float resolved = BlockLayout.ResolveLength(fsStr, 0, 16);
                if (resolved > 0) fontSize = resolved;
            }

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
                box.Style.FontFamily, box.Style.FontWeight, box.Style.Get("font-style"),
                box.Style.Get("font-feature-settings"));

            string? complexSuffix = ComplexTextShaper.FontKeySuffix(logicalPaintText);
            FontData? complexShapingFont = null;
            if (complexSuffix != null && CurrentPdfDoc != null &&
                CurrentPdfDoc.TryGetShapingFont(fontName + complexSuffix, out var shapingFont))
            {
                fontName += complexSuffix;
                complexShapingFont = shapingFont;
            }

            float pdfFontSize = fontSize * PdfCoordinates.PxToPt;
            float textX = effectiveX + box.PaddingLeft;

            var textAlign = LogicalPropertyResolver.PhysicalTextAlign(box.Style);
            float justifyExtraWordSpacing = 0;
            if (!string.IsNullOrEmpty(textAlign) && box.ContentWidth < box.Width)
            {
                float availableWidth = box.Width - box.PaddingLeft - box.PaddingRight;
                float textWidth = box.ContentWidth;
                if (textAlign == "center")
                    textX += (availableWidth - textWidth) / 2;
                else if (textAlign == "right")
                    textX += availableWidth - textWidth;
                else if (textAlign == "justify" && !box.IsInlineFlowItem)
                {
                    if (box.Justification == LineJustification.Justifiable)
                    {
                        int spaceCount = 0;
                        for (int si = 0; si < box.Text!.Length; si++)
                            if (box.Text[si] == ' ') spaceCount++;
                        if (spaceCount > 0)
                        {
                            float gap = (availableWidth - textWidth) * PdfCoordinates.PxToPt;
                            justifyExtraWordSpacing = gap / spaceCount;
                        }
                    }
                    else
                    {
                        var textAlignLast = box.Style.Get("text-align-last");
                        if (textAlignLast == "center")
                            textX += (availableWidth - textWidth) / 2;
                        else if (textAlignLast == "right")
                            textX += availableWidth - textWidth;
                    }
                }
            }

            float pdfX = textX * PdfCoordinates.PxToPt;

            float lineBoxHeight = box.Height > 0 ? box.Height : fontSize * 1.2f;
            float baselineOffset = TextMeasurer.GetBaselineOffset(fontSize, lineBoxHeight,
                box.Style.FontFamily, box.Style.FontWeight, box.Style.Get("font-style"), box.Text);
            float pdfY = (pageHeightPx - adjustedY - box.PaddingTop - baselineOffset) * PdfCoordinates.PxToPt;

            var verticalAlign = box.Style.Get("vertical-align");
            if (!string.IsNullOrEmpty(verticalAlign))
            {
                if (verticalAlign == "super" || verticalAlign == "sup")
                    pdfY += fontSize * SuperscriptRaiseEm * PdfCoordinates.PxToPt;
                else if (verticalAlign == "sub")
                    pdfY -= fontSize * SubscriptLowerEm * PdfCoordinates.PxToPt;
            }

            var textColor = box.Style.Color;
            Color? color = string.IsNullOrEmpty(textColor)
                ? Color.FromRgb(0, 0, 0)
                : ParseColor(textColor) ?? Color.FromRgb(0, 0, 0);
            if (filterParams != null && filterParams.HasEffect && color.HasValue)
            {
                var (fr, fg, fb) = EggPdf.Pdf.PdfFilterEffects.ApplyColorFilter(
                    color.Value.R / 255f, color.Value.G / 255f, color.Value.B / 255f, filterParams);
                color = Color.FromRgba((byte)(fr * 255), (byte)(fg * 255), (byte)(fb * 255), color.Value.A);
            }

            float letterSpacing = 0, wordSpacing = 0;
            var lsStr = box.Style.Get("letter-spacing");
            if (!string.IsNullOrEmpty(lsStr) && lsStr != "normal")
                letterSpacing = BlockLayout.ResolveLength(lsStr, 0, fontSize) * PdfCoordinates.PxToPt;
            var wsStr = box.Style.Get("word-spacing");
            if (!string.IsNullOrEmpty(wsStr) && wsStr != "normal")
                wordSpacing = BlockLayout.ResolveLength(wsStr, 0, fontSize) * PdfCoordinates.PxToPt;
            wordSpacing += justifyExtraWordSpacing;

            bool hasCombineUpright = EggPdf.Layout.WritingModeLayout.ResolveCombineUpright(
                box.Style.Get("text-combine-upright"));
            if (hasCombineUpright)
            {
                float measuredW = TextMeasurer.MeasureWidth(paintText, fontSize,
                    box.Style.FontFamily, box.Style.FontWeight, box.Style.Get("font-style"))
                    * PdfCoordinates.PxToPt;
                float emPt = fontSize * PdfCoordinates.PxToPt;
                if (measuredW > emPt && emPt > 0f)
                {
                    float sx = emPt / measuredW;
                    float tx = pdfX * (1f - sx);
                    page.SaveState();
                    page.ConcatMatrix(sx, 0f, 0f, 1f, tx, 0f);
                }
                else
                {
                    hasCombineUpright = false;
                }
            }

            var textShadow = box.Style.Get("text-shadow");
            if (!string.IsNullOrEmpty(textShadow) && textShadow != "none")
            {
                ParseTextShadow(textShadow, fontSize, color, out float shX, out float shY, out float shR, out float shG, out float shB, out float shA);
                if (shA > 0)
                {
                    float shadowPdfX = pdfX + shX * PdfCoordinates.PxToPt;
                    float shadowPdfY = pdfY - shY * PdfCoordinates.PxToPt;
                    if (shA < 1f) SetPaintOpacity(page, shA);
                    page.AddText(paintText, shadowPdfX, shadowPdfY, fontName, pdfFontSize, shR, shG, shB, letterSpacing, wordSpacing);
                    if (shA < 1f) SetPaintOpacity(page, 1f);
                }
            }

            float textAlpha = color?.A / 255f ?? 1f;
            if (textAlpha < 1f)
                SetPaintOpacity(page, textAlpha);

            if (complexShapingFont != null && CurrentPdfDoc != null)
            {
                PaintShapedRun(page, CurrentPdfDoc, fontName, complexShapingFont, logicalPaintText,
                    box.Style.Get("direction") == "rtl", pdfX, pdfY, pdfFontSize,
                    color?.R / 255f ?? 0, color?.G / 255f ?? 0, color?.B / 255f ?? 0, letterSpacing);
            }
            else if (CurrentPdfDoc != null && CurrentPdfDoc.IsEmbeddedFont(fontName) && CurrentPdfDoc.ContainsColorGlyphs(fontName, paintText))
            {
                PaintColorGlyphRun(page, paintText, pdfX, pdfY, fontName, pdfFontSize, letterSpacing,
                    color?.R / 255f ?? 0, color?.G / 255f ?? 0, color?.B / 255f ?? 0);
            }
            else if (isSmallCaps)
            {
                PaintSmallCaps(page, paintText, fontVariantCaps == "all-small-caps", pdfX, pdfY, fontName, fontSize,
                    box.Style, color?.R / 255f ?? 0, color?.G / 255f ?? 0, color?.B / 255f ?? 0, letterSpacing, wordSpacing);
            }
            else
            {
                EmitTextRun(page, paintText, pdfX, pdfY, fontName, pdfFontSize,
                    color?.R / 255f ?? 0, color?.G / 255f ?? 0, color?.B / 255f ?? 0, letterSpacing, wordSpacing);
            }

            if (textAlpha < 1f)
                SetPaintOpacity(page, 1f);

            var textDecorationLine = box.Style.Get("text-decoration-line")
                                  ?? box.Style.Get("text-decoration")
                                  ?? "";
            if (!string.IsNullOrEmpty(textDecorationLine) && textDecorationLine != "none")
            {
                float lineY;
                float measuredPx = TextMeasurer.MeasureWidth(paintText, fontSize,
                    box.Style.FontFamily, box.Style.FontWeight, box.Style.Get("font-style"));
                float textWidth = measuredPx * PdfCoordinates.PxToPt;

                float decoLineWidth = Math.Max(fontSize * DecorationThicknessEm, MinDecorationThicknessPx) * PdfCoordinates.PxToPt;
                var thicknessVal = box.Style.Get("text-decoration-thickness");
                if (!string.IsNullOrEmpty(thicknessVal) && thicknessVal != "auto" && thicknessVal != "from-font")
                {
                    float resolved = BlockLayout.ResolveLength(thicknessVal, fontSize, fontSize);
                    if (resolved > 0) decoLineWidth = resolved * PdfCoordinates.PxToPt;
                }

                float dr = 0, dg = 0, db = 0;
                var decoColorStr = box.Style.Get("text-decoration-color");
                Color? decoColor = !IsUnsetOrCurrentColor(decoColorStr)
                    ? Color.TryParse(decoColorStr) : color;
                if (decoColor.HasValue)
                {
                    dr = decoColor.Value.R / 255f;
                    dg = decoColor.Value.G / 255f;
                    db = decoColor.Value.B / 255f;
                }

                var decoStyle = box.Style.Get("text-decoration-style") ?? "solid";

                if (textDecorationLine.IndexOf("underline", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    float underlineOffsetPx = 0f;
                    var underlineOffsetVal = box.Style.Get("text-underline-offset");
                    if (!string.IsNullOrEmpty(underlineOffsetVal) && underlineOffsetVal != "auto")
                        underlineOffsetPx = BlockLayout.ResolveLength(underlineOffsetVal, fontSize, fontSize);
                    lineY = pdfY - (fontSize * UnderlineDropEm + underlineOffsetPx) * PdfCoordinates.PxToPt;
                    page.AddDecorationLine(pdfX, lineY, pdfX + textWidth, lineY, dr, dg, db, decoLineWidth, decoStyle);
                }
                if (textDecorationLine.IndexOf("line-through", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    lineY = pdfY + fontSize * LineThroughRiseEm * PdfCoordinates.PxToPt;
                    page.AddDecorationLine(pdfX, lineY, pdfX + textWidth, lineY, dr, dg, db, decoLineWidth, decoStyle);
                }
                if (textDecorationLine.IndexOf("overline", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    lineY = pdfY + fontSize * OverlineRiseEm * PdfCoordinates.PxToPt;
                    page.AddDecorationLine(pdfX, lineY, pdfX + textWidth, lineY, dr, dg, db, decoLineWidth, decoStyle);
                }
            }

            PaintTextEmphasis(page, box, paintText, pdfX, pdfY, fontSize, fontName, color);

            if (hasCombineUpright)
                page.RestoreState();
        }

        if (!string.IsNullOrEmpty(box.ImageSource) && box.ImageData != null && CurrentPdfDoc?.GetImage(ImageResourceName(box.ImageSource!)) != null)
        {
            var (contentX, contentTop, contentWidth, contentHeight) = ContentRect(box, effectiveX, adjustedY);
            float pdfX = contentX * PdfCoordinates.PxToPt;
            float pdfY = (pageHeightPx - contentTop - contentHeight) * PdfCoordinates.PxToPt;
            float pdfW = contentWidth * PdfCoordinates.PxToPt;
            float pdfH = contentHeight * PdfCoordinates.PxToPt;

            var objectFit = box.Style.Get("object-fit") ?? "fill";
            var objectPosition = box.Style.Get("object-position") ?? "50% 50%";

            float posX = 0.5f, posY = 0.5f;
            var posParts = objectPosition.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (posParts.Length >= 1) posX = ParseObjectPositionFraction(posParts[0], false);
            if (posParts.Length >= 2) posY = ParseObjectPositionFraction(posParts[1], true);

            if (objectFit == "contain" || objectFit == "cover" || objectFit == "scale-down" || objectFit == "none")
            {
                float natW = pdfW, natH = pdfH;
                if (ImageIntrinsicSize.TryRead(box.ImageData!, out int storedWidth, out int storedHeight, out int orientation))
                {
                    bool swap = !IgnoresExifOrientation(box) && ImageIntrinsicSize.SwapsWidthAndHeight(orientation);
                    natW = (swap ? storedHeight : storedWidth) * PdfCoordinates.PxToPt;
                    natH = (swap ? storedWidth : storedHeight) * PdfCoordinates.PxToPt;
                }

                float fitW, fitH;
                if (objectFit == "none")
                {
                    fitW = natW; fitH = natH;
                }
                else if (objectFit == "scale-down")
                {
                    float scaleX2 = pdfW / Math.Max(natW, 1);
                    float scaleY2 = pdfH / Math.Max(natH, 1);
                    float scale2 = Math.Min(Math.Min(scaleX2, scaleY2), 1f);
                    fitW = natW * scale2; fitH = natH * scale2;
                }
                else
                {
                    float scaleX = pdfW / Math.Max(natW, 1);
                    float scaleY = pdfH / Math.Max(natH, 1);
                    float scale = objectFit == "contain" ? Math.Min(scaleX, scaleY) : Math.Max(scaleX, scaleY);
                    fitW = natW * scale; fitH = natH * scale;
                }

                pdfX += (pdfW - fitW) * posX;
                pdfY += (pdfH - fitH) * (1f - posY);
                pdfW = fitW;
                pdfH = fitH;
            }

            string imgName = ImageResourceName(box.ImageSource!);
            page.SaveState();
            ClipToBox(page, box, effectiveX * PdfCoordinates.PxToPt, (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt,
                box.Width * PdfCoordinates.PxToPt, box.Height * PdfCoordinates.PxToPt);
            page.AddImage(imgName, pdfX, pdfY, pdfW, pdfH, DisplayOrientation(box, CurrentPdfDoc!.GetImage(imgName)!));
            page.RestoreState();
        }

        var svgRoot = box.Element?.TagName == "svg" ? box.Element : box.ImageSvgRoot;
        if (svgRoot != null)
        {
            var svgElement = svgRoot == box.Element ? SvgParser.Parse(svgRoot, box.Style.Get) : SvgParser.Parse(svgRoot);
            if (svgElement != null)
            {
                var (contentX, contentTop, contentWidth, contentHeight) = ContentRect(box, effectiveX, adjustedY);
                float pdfX = contentX * PdfCoordinates.PxToPt;
                float pdfY = (pageHeightPx - contentTop - contentHeight) * PdfCoordinates.PxToPt;
                float pdfW = contentWidth * PdfCoordinates.PxToPt;
                float pdfH = contentHeight * PdfCoordinates.PxToPt;
                var svgResult = SvgRenderer.RenderWithResources(svgElement, pdfX, pdfY, pdfW, pdfH, CurrentPdfDoc,
                    StandardFontMetrics.MeasureWidth);
                page.AppendRawContent(svgResult.Commands);
                foreach (var imgName in svgResult.UsedImages)
                    page.RegisterUsedImage(imgName);
                foreach (var fontName in svgResult.UsedFonts)
                    page.RegisterUsedFont(fontName);
                foreach (var stateName in svgResult.UsedExtGStates)
                    page.RegisterUsedExtGState(stateName);
            }
        }

        var linkElement = box.InlineSpan?.Element ?? box.Element;
        if (linkElement?.TagName == "a")
        {
            var href = linkElement.GetAttribute("href")?.Trim();
            bool isInternalLink = !string.IsNullOrEmpty(href) && href![0] == '#';
            if (!string.IsNullOrEmpty(href) && (IsExternalLinkTarget(href!) || isInternalLink))
            {
                var span = box.InlineSpan;
                if (span != null)
                {
                    if (span.PaintTag is not PdfLinkAnnotation)
                    {
                        float spanEffectiveX = effectiveX + (span.X - box.X);
                        float spanAdjustedY = adjustedY + (span.Y - box.Y);
                        float pdfX = spanEffectiveX * PdfCoordinates.PxToPt;
                        float pdfY = (pageHeightPx - spanAdjustedY - span.Height) * PdfCoordinates.PxToPt;
                        float pdfW = span.Width * PdfCoordinates.PxToPt;
                        float pdfH = span.Height * PdfCoordinates.PxToPt;
                        span.PaintTag = isInternalLink
                            ? page.AddInternalLink(pdfX, pdfY, pdfW, pdfH, AnchorIdFromHref(href!))
                            : page.AddLink(pdfX, pdfY, pdfW, pdfH, href);
                    }
                    if (span.PaintTag is PdfLinkAnnotation spanLink && StructureMap != null &&
                        StructureMap.TryGetValue(box, out var spanLinkElem))
                        spanLink.TaggedElement = spanLinkElem;
                }
                else
                {
                    float pdfX = effectiveX * PdfCoordinates.PxToPt;
                    float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
                    float pdfW = box.Width * PdfCoordinates.PxToPt;
                    float pdfH = box.Height * PdfCoordinates.PxToPt;
                    var link = isInternalLink
                        ? page.AddInternalLink(pdfX, pdfY, pdfW, pdfH, AnchorIdFromHref(href!))
                        : page.AddLink(pdfX, pdfY, pdfW, pdfH, href);
                    if (StructureMap != null && StructureMap.TryGetValue(box, out var linkElem))
                        link.TaggedElement = linkElem;
                }
            }
        }

        if (hasBlendMode)
            page.RestoreState();

        if (hasCssClipPath)
            page.RestoreState();

        if (hasClip)
            page.RestoreState();

        if (hasWritingModeTransform)
            page.RestoreState();

        if (hasTransform)
            page.RestoreState();
    }

    private static string StripWeightSuffix(string fontName)
    {
        int idx = fontName.LastIndexOf("-W", StringComparison.Ordinal);
        if (idx <= 0 || idx + 2 >= fontName.Length || !char.IsDigit(fontName[idx + 2])) return fontName;
        return fontName.Substring(0, idx);
    }

    private static List<(string fontName, ushort[] glyphIds)> BuildFontFallbackRuns(
        string text, string mainFont, IReadOnlyList<string> fallbackFonts)
    {
        var runs = new List<(string fontName, ushort[] glyphIds)>();
        var current = new List<ushort>();
        string currentFont = mainFont;

        for (int i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            string font = mainFont;
            if (!CurrentPdfDoc!.TryGetGlyphId(mainFont, cp, out var gid) || gid == NotdefGlyph)
            {
                if (TryFindFallbackGlyph(cp, fallbackFonts, out var fbFont, out var fbGid))
                {
                    font = fbFont;
                    gid = fbGid;
                }
                else if (ArabicShaper.TryGetBaseForm(cp, out var baseText) &&
                         TryAppendBaseFormGlyphs(baseText, mainFont, fallbackFonts, runs, current, ref currentFont))
                {
                    continue;
                }
                else if (UnicodeScripts.IsDefaultIgnorable(cp))
                {
                    continue;
                }
                else
                {
                    gid = NotdefGlyph;
                }
            }

            if (font != currentFont && current.Count > 0)
            {
                runs.Add((currentFont, current.ToArray()));
                current.Clear();
            }
            currentFont = font;
            current.Add(gid);
        }

        if (current.Count > 0)
            runs.Add((currentFont, current.ToArray()));

        return runs;
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

    private static float ResolveBorderRadius(ComputedStyle style, string property, float boxWidth)
    {
        var value = style.Get(property) ?? style.Get("border-radius");
        if (string.IsNullOrEmpty(value) || value == "0" || value == "0px")
            return 0;

        var spaceIdx = value.IndexOf(' ');
        var hValue = spaceIdx > 0 ? value.Substring(0, spaceIdx) : value;

        float resolved = BlockLayout.ResolveLength(hValue, boxWidth, 16);
        return Math.Max(0, resolved);
    }

    private static void PaintTextEmphasis(PdfPage page, LayoutBox box, string text,
        float pdfX, float pdfY, float fontSize, string fontName, Color? textColor)
    {
        var emphasisStyle = box.Style.Get("text-emphasis-style") ?? box.Style.Get("text-emphasis");
        if (string.IsNullOrEmpty(emphasisStyle) || emphasisStyle == "none") return;

        bool isFilled = emphasisStyle.IndexOf("open", StringComparison.OrdinalIgnoreCase) < 0;
        string markChar;

        if (emphasisStyle.IndexOf("dot", StringComparison.OrdinalIgnoreCase) >= 0)
            markChar = isFilled ? "\u2022" : "\u25E6";
        else if (emphasisStyle.IndexOf("double-circle", StringComparison.OrdinalIgnoreCase) >= 0)
            markChar = isFilled ? "\u25C9" : "\u25CE";
        else if (emphasisStyle.IndexOf("circle", StringComparison.OrdinalIgnoreCase) >= 0)
            markChar = isFilled ? "\u25CF" : "\u25CB";
        else if (emphasisStyle.IndexOf("triangle", StringComparison.OrdinalIgnoreCase) >= 0)
            markChar = isFilled ? "\u25B2" : "\u25B3";
        else if (emphasisStyle.IndexOf("sesame", StringComparison.OrdinalIgnoreCase) >= 0)
            markChar = isFilled ? "\uFE45" : "\uFE46";
        else
        {
            var trimmed = emphasisStyle.Trim();
            if ((trimmed.StartsWith("\"") && trimmed.EndsWith("\"")) ||
                (trimmed.StartsWith("'") && trimmed.EndsWith("'")))
                markChar = trimmed.Length > 2 ? trimmed.Substring(1, trimmed.Length - 2) : "";
            else
                markChar = trimmed.Length > 0 ? trimmed.Substring(0, 1) : "";
            if (string.IsNullOrEmpty(markChar)) return;
        }

        var emphasisPos = box.Style.Get("text-emphasis-position") ?? "over right";
        bool isOver = emphasisPos.IndexOf("under", StringComparison.OrdinalIgnoreCase) < 0;

        float er = textColor?.R / 255f ?? 0f;
        float eg = textColor?.G / 255f ?? 0f;
        float eb = textColor?.B / 255f ?? 0f;
        var emphasisColorStr = box.Style.Get("text-emphasis-color");
        if (!IsUnsetOrCurrentColor(emphasisColorStr))
        {
            var ec = Color.TryParse(emphasisColorStr);
            if (ec.HasValue) { er = ec.Value.R / 255f; eg = ec.Value.G / 255f; eb = ec.Value.B / 255f; }
        }

        float markFontSize = fontSize * EmphasisMarkScale * PdfCoordinates.PxToPt;

        string emphFontStyle = box.Style.Get("font-style") ?? "";
        string emphPdfFont = StandardFontMetrics.ResolvePdfFontName(box.Style.FontFamily, box.Style.FontWeight, emphFontStyle);
        float markWidthPt = TextMeasurer.MeasureWidth(markChar, fontSize * EmphasisMarkScale,
            box.Style.FontFamily, box.Style.FontWeight, null)
            * PdfCoordinates.PxToPt;

        float charX = pdfX;
        for (int i = 0; i < text.Length; i++)
        {
            float charWidthPt = StandardFontMetrics.MeasureCharWidth(text[i], fontSize, emphPdfFont)
                * PdfCoordinates.PxToPt;

            float markX = charX + (charWidthPt - markWidthPt) / 2f;
            float markY = isOver
                ? pdfY + fontSize * EmphasisOverRiseEm * PdfCoordinates.PxToPt
                : pdfY - fontSize * EmphasisUnderDropEm * PdfCoordinates.PxToPt;

            page.AddText(markChar, markX, markY, fontName, markFontSize, er, eg, eb, 0, 0);
            charX += charWidthPt;
        }
    }

    private static void PaintBackgroundImage(PdfPage page, LayoutBox box, string bgImage,
        float effectiveX, float pageHeightPx, float adjustedY)
    {
        if (bgImage.StartsWith("linear-gradient(", StringComparison.OrdinalIgnoreCase) ||
            bgImage.StartsWith("repeating-linear-gradient(", StringComparison.OrdinalIgnoreCase))
        {
            float pdfX = effectiveX * PdfCoordinates.PxToPt;
            float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
            float pdfW = box.Width * PdfCoordinates.PxToPt;
            float pdfH = box.Height * PdfCoordinates.PxToPt;
            var gradientStates = new List<string>();
            var commands = PdfGradient.RenderLinearGradient(bgImage, pdfX, pdfY, pdfW, pdfH, gradientStates, CurrentBoxOpacity);
            if (commands != null)
            {
                page.AppendRawContent(commands);
                foreach (var state in gradientStates)
                    page.RegisterUsedExtGState(state);
            }
            return;
        }
        bool radial = bgImage.StartsWith("radial-gradient(", StringComparison.OrdinalIgnoreCase) ||
                      bgImage.StartsWith("repeating-radial-gradient(", StringComparison.OrdinalIgnoreCase);
        bool conic = bgImage.StartsWith("conic-gradient(", StringComparison.OrdinalIgnoreCase) ||
                     bgImage.StartsWith("repeating-conic-gradient(", StringComparison.OrdinalIgnoreCase);
        if (radial || conic)
        {
            float pdfX = effectiveX * PdfCoordinates.PxToPt;
            float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
            float pdfW = box.Width * PdfCoordinates.PxToPt;
            float pdfH = box.Height * PdfCoordinates.PxToPt;
            var gradientStates = new List<string>();
            var commands = radial
                ? PdfRadialGradient.Render(bgImage, pdfX, pdfY, pdfW, pdfH, gradientStates, CurrentBoxOpacity)
                : PdfConicGradient.Render(bgImage, pdfX, pdfY, pdfW, pdfH, gradientStates, CurrentBoxOpacity);
            if (commands != null)
            {
                page.AppendRawContent(commands);
                foreach (var state in gradientStates)
                    page.RegisterUsedExtGState(state);
            }
            return;
        }

        string? url = null;
        string? imageFunctionFallbackColor = null;
        if (bgImage.StartsWith("image-set(", StringComparison.OrdinalIgnoreCase) ||
            bgImage.StartsWith("-webkit-image-set(", StringComparison.OrdinalIgnoreCase))
        {
            url = ResolveImageSet(bgImage);
        }
        else if (bgImage.StartsWith("image(", StringComparison.OrdinalIgnoreCase))
        {
            bool imageFnIsRTL = box.Style.Get("direction") == "rtl";
            (url, imageFunctionFallbackColor) = ResolveImageFunction(bgImage, imageFnIsRTL);
        }
        else if (bgImage.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            int start = 4;
            int end = bgImage.Length - 1;
            if (end > start)
            {
                url = bgImage.Substring(start, end - start).Trim();
                if (url.Length >= 2 && ((url[0] == '\'' && url[url.Length - 1] == '\'') ||
                    (url[0] == '"' && url[url.Length - 1] == '"')))
                    url = url.Substring(1, url.Length - 2);
            }
        }

        if (string.IsNullOrEmpty(url))
        {
            PaintImageFunctionFallback(page, box, imageFunctionFallbackColor, effectiveX, pageHeightPx, adjustedY);
            return;
        }

        var data = LoadBackgroundImageData(url);
        if (data == null || data.Length == 0)
        {
            PaintImageFunctionFallback(page, box, imageFunctionFallbackColor, effectiveX, pageHeightPx, adjustedY);
            return;
        }

        PaintUrlBackground(page, box, url, data, effectiveX, pageHeightPx, adjustedY);
    }

    private static byte[]? LoadBackgroundImageData(string url)
    {
        var loader = ImageLoader;
        if (loader != null) return loader(url);

        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return EggPdf.Core.Resources.DataUri.TryDecode(url, out var bytes, out _) ? bytes : null;
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

    private static void PaintBorders(PdfPage page, LayoutBox box, float effectiveX,
        float pageHeightPt, float pageHeightPx, float adjustedY,
        bool hasRadius, float tlrPt, float trrPt, float brrPt, float blrPt)
    {
        var borderImageSrc = box.Style.Get("border-image-source");
        if (!string.IsNullOrEmpty(borderImageSrc) && borderImageSrc != "none")
        {
            bool isGradient = borderImageSrc.IndexOf("gradient(", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isGradient && CurrentPdfDoc != null)
            {
                float fontSize = BlockLayout.ResolveFontSize(box.Style.FontSize, 16f);
                float ringLeft = BlockLayout.VisibleBorderWidth(box.Style, "left", fontSize);
                float ringRight = BlockLayout.VisibleBorderWidth(box.Style, "right", fontSize);
                float ringTop = BlockLayout.VisibleBorderWidth(box.Style, "top", fontSize);
                float ringBottom = BlockLayout.VisibleBorderWidth(box.Style, "bottom", fontSize);
                float outerY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
                page.SaveState();
                ClipToRingBetween(page,
                    effectiveX * PdfCoordinates.PxToPt, outerY,
                    box.Width * PdfCoordinates.PxToPt, box.Height * PdfCoordinates.PxToPt,
                    (effectiveX + ringLeft) * PdfCoordinates.PxToPt, outerY + ringBottom * PdfCoordinates.PxToPt,
                    Math.Max(0f, box.Width - ringLeft - ringRight) * PdfCoordinates.PxToPt,
                    Math.Max(0f, box.Height - ringTop - ringBottom) * PdfCoordinates.PxToPt);
                PaintBackgroundImage(page, box, borderImageSrc, effectiveX, pageHeightPx, adjustedY);
                page.RestoreState();
                return;
            }
        }

        var fallbackStyle = box.Style.Get("border-style");
        var fallbackWidth = box.Style.Get("border-width");
        var fallbackColor = box.Style.Get("border-color");

        string[] sideStyles = new string[4];
        float[] sideWidths = new float[4];
        float[] sideR = new float[4], sideG = new float[4], sideB = new float[4], sideA = { 1f, 1f, 1f, 1f };
        bool anyBorder = false;
        bool allSolid = true;

        for (int s = 0; s < 4; s++)
        {
            sideStyles[s] = box.Style.Get(BorderStyleKeys[s]) ?? fallbackStyle ?? "";
            if (string.IsNullOrEmpty(sideStyles[s]) || sideStyles[s] == "none" || sideStyles[s] == "hidden")
            {
                sideWidths[s] = 0;
                continue;
            }

            anyBorder = true;
            if (sideStyles[s] != "solid") allSolid = false;

            var widthStr = box.Style.Get(BorderWidthKeys[s]) ?? fallbackWidth;
            if (widthStr != null && widthStr.IndexOf(' ') > 0) widthStr = widthStr.Split(' ')[0];
            sideWidths[s] = BlockLayout.BorderWidthKeyword(widthStr, 16);
            if (sideWidths[s] <= 0) { sideWidths[s] = 0; continue; }

            var colorStr = box.Style.Get(BorderColorKeys[s]) ?? fallbackColor;
            if (IsUnsetOrCurrentColor(colorStr))
                colorStr = box.Style.Color;
            if (!string.IsNullOrEmpty(colorStr))
            {
                var bc = ParseColor(colorStr!);
                if (bc.HasValue)
                {
                    sideR[s] = bc.Value.R / 255f;
                    sideG[s] = bc.Value.G / 255f;
                    sideB[s] = bc.Value.B / 255f;
                    sideA[s] = bc.Value.A / 255f;
                }
            }
        }

        if (!anyBorder) return;

        float pdfX = effectiveX * PdfCoordinates.PxToPt;
        float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
        float pdfW = box.Width * PdfCoordinates.PxToPt;
        float pdfH = box.Height * PdfCoordinates.PxToPt;

        bool uniformSolidBorder = allSolid && sideWidths[0] > 0 &&
            SameBorderSide(0, 1, sideWidths, sideR, sideG, sideB, sideA) &&
            SameBorderSide(0, 2, sideWidths, sideR, sideG, sideB, sideA) &&
            SameBorderSide(0, 3, sideWidths, sideR, sideG, sideB, sideA);

        if (uniformSolidBorder)
        {
            float widthPt = sideWidths[0] * PdfCoordinates.PxToPt;
            float half = Math.Min(widthPt / 2f, Math.Min(pdfW, pdfH) / 2f);
            bool translucent = BeginStrokeAlpha(page, sideA[0]);
            if (hasRadius)
                page.AddStrokeRoundedRectangle(pdfX + half, pdfY + half, pdfW - 2f * half, pdfH - 2f * half,
                    sideR[0], sideG[0], sideB[0], widthPt,
                    Math.Max(0f, tlrPt - half), Math.Max(0f, trrPt - half), Math.Max(0f, brrPt - half), Math.Max(0f, blrPt - half));
            else
                page.AddStrokeRectangle(pdfX + half, pdfY + half, pdfW - 2f * half, pdfH - 2f * half,
                    sideR[0], sideG[0], sideB[0], widthPt);
            EndStrokeAlpha(page, translucent);
            return;
        }

        float top = pdfY + pdfH;
        float right = pdfX + pdfW;
        float topInset = sideWidths[0] * PdfCoordinates.PxToPt / 2f;
        float rightInset = sideWidths[1] * PdfCoordinates.PxToPt / 2f;
        float bottomInset = sideWidths[2] * PdfCoordinates.PxToPt / 2f;
        float leftInset = sideWidths[3] * PdfCoordinates.PxToPt / 2f;
        PaintBorderSide(page, 0, pdfX, top - topInset, right, top - topInset, sideWidths, sideR, sideG, sideB, sideA, sideStyles);
        PaintBorderSide(page, 1, right - rightInset, top, right - rightInset, pdfY, sideWidths, sideR, sideG, sideB, sideA, sideStyles);
        PaintBorderSide(page, 2, right, pdfY + bottomInset, pdfX, pdfY + bottomInset, sideWidths, sideR, sideG, sideB, sideA, sideStyles);
        PaintBorderSide(page, 3, pdfX + leftInset, pdfY, pdfX + leftInset, top, sideWidths, sideR, sideG, sideB, sideA, sideStyles);
    }

    private static bool SameBorderSide(int a, int b, float[] widths, float[] r, float[] g, float[] blue, float[] alpha) =>
        widths[a] == widths[b] && r[a] == r[b] && g[a] == g[b] && blue[a] == blue[b] && alpha[a] == alpha[b];

    private static void PaintBorderSide(PdfPage page, int side, float x1, float y1, float x2, float y2,
        float[] widths, float[] r, float[] g, float[] b, float[] alpha, string[] styles)
    {
        if (widths[side] <= 0) return;
        bool translucent = BeginStrokeAlpha(page, alpha[side]);
        page.AddBorderLine(x1, y1, x2, y2, r[side], g[side], b[side], widths[side] * PdfCoordinates.PxToPt, styles[side]);
        EndStrokeAlpha(page, translucent);
    }

    private static bool BeginStrokeAlpha(PdfPage page, float alpha)
    {
        if (alpha >= 1f) return false;
        SetPaintOpacity(page, alpha);
        return true;
    }

    private static void EndStrokeAlpha(PdfPage page, bool translucent)
    {
        if (translucent) SetPaintOpacity(page, 1f);
    }

    private static bool ApplyWritingModeTransform(PdfPage page, LayoutBox box,
        float pageHeightPx, float adjustedY, float effectiveX)
    {
        var writingMode = box.Style.Get("writing-mode");
        if (writingMode != "vertical-rl" && writingMode != "vertical-lr")
            return false;

        float cx = (effectiveX + box.Width / 2f) * PdfCoordinates.PxToPt;
        float cy = (pageHeightPx - adjustedY - box.Height / 2f) * PdfCoordinates.PxToPt;

        float pdfA = 0f, pdfB = 1f, pdfC = -1f, pdfD = 0f;
        float pdfE = cx + cy;
        float pdfF = cy - cx;

        page.SaveState();
        page.ConcatMatrix(pdfA, pdfB, pdfC, pdfD, pdfE, pdfF);
        return true;
    }

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
            pos++;

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
            if (pos < value.Length) pos++;

            var args = ParseTransformArgs(argsStr, funcName, boxWidth, boxHeight);
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

    private static float[] ParseTransformArgs(string argsStr, string funcName, float boxWidth, float boxHeight)
    {
        var result = new List<float>();
        var parts = argsStr.Split(CommaSpaceSep, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (string.IsNullOrEmpty(part)) continue;
            if (part.EndsWith("%", StringComparison.Ordinal) && IsTranslateFunction(funcName) &&
                float.TryParse(part.Substring(0, part.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float percent))
            {
                bool vertical = funcName == "translateY" || (funcName != "translateX" && result.Count == 1);
                result.Add(percent / 100f * (vertical ? boxHeight : boxWidth));
                continue;
            }
            result.Add(ParseAngleOrLength(part));
        }
        return result.ToArray();
    }

    private static bool IsTranslateFunction(string funcName) =>
        funcName == "translate" || funcName == "translateX" || funcName == "translateY" || funcName == "translate3d";

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
            case "rotateZ":
            {
                float angle = args.Length > 0 ? args[0] : 0;
                float cos = (float)Math.Cos(angle);
                float sin = (float)Math.Sin(angle);
                return new Matrix2D { A = cos, B = sin, C = -sin, D = cos, E = 0, F = 0 };
            }
            case "rotateX":
            {
                float angle = args.Length > 0 ? args[0] : 0;
                float cos = (float)Math.Abs(Math.Cos(angle));
                return new Matrix2D { A = 1, B = 0, C = 0, D = cos, E = 0, F = 0 };
            }
            case "rotateY":
            {
                float angle = args.Length > 0 ? args[0] : 0;
                float cos = (float)Math.Abs(Math.Cos(angle));
                return new Matrix2D { A = cos, B = 0, C = 0, D = 1, E = 0, F = 0 };
            }
            case "rotate3d":
            {
                float x = args.Length > 0 ? args[0] : 0;
                float y = args.Length > 1 ? args[1] : 0;
                float z = args.Length > 2 ? args[2] : 0;
                float ang = args.Length > 3 ? args[3] : 0;
                float len = (float)Math.Sqrt(x * x + y * y + z * z);
                if (len < 0.0001f) return null;
                float nz = z / len;
                float cos = (float)Math.Cos(ang);
                float sin = (float)Math.Sin(ang);
                float a = cos + (1 - cos) * (1 - nz * nz) < 0 ? 0 : cos;
                return new Matrix2D { A = a, B = nz * sin, C = -nz * sin, D = a, E = 0, F = 0 };
            }
            case "translateZ":
            case "scaleZ":
            case "perspective":
                return null;
            case "translate3d":
            {
                float tx = args.Length > 0 ? args[0] : 0;
                float ty = args.Length > 1 ? args[1] : 0;
                return new Matrix2D { A = 1, B = 0, C = 0, D = 1, E = tx, F = ty };
            }
            case "scale3d":
            {
                float sx = args.Length > 0 ? args[0] : 1;
                float sy = args.Length > 1 ? args[1] : 1;
                return new Matrix2D { A = sx, B = 0, C = 0, D = sy, E = 0, F = 0 };
            }
            case "matrix3d":
            {
                if (args.Length >= 16)
                    return new Matrix2D { A = args[0], B = args[1], C = args[4], D = args[5], E = args[12], F = args[13] };
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

        float ePt = me * PdfCoordinates.PxToPt;
        float fPt = -mf * PdfCoordinates.PxToPt;

        float pdfA = ma;
        float pdfB = -mb;
        float pdfC = -mc;
        float pdfD = md;

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
    public static void SortByZIndex(List<LayoutBox> boxes)
    {
        var tagged = new (int z, int i)[boxes.Count];
        for (int i = 0; i < boxes.Count; i++)
            tagged[i] = (GetZIndex(boxes[i]), i);
        Array.Sort(tagged, (a, b) => a.z != b.z ? a.z.CompareTo(b.z) : a.i.CompareTo(b.i));
        var copy = new LayoutBox[boxes.Count];
        for (int i = 0; i < tagged.Length; i++)
            copy[i] = boxes[tagged[i].i];
        boxes.Clear();
        boxes.AddRange(copy);
    }

    private static int GetZIndex(LayoutBox box)
    {
        var zStr = box.Style.Get("z-index");
        if (!string.IsNullOrEmpty(zStr) && zStr != "auto" &&
            int.TryParse(zStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
            return z;
        return 0;
    }

    private static Color? ParseColor(string value)
    {
        return Color.TryParse(value);
    }

    private static bool IsUnsetOrCurrentColor(string? value) =>
        string.IsNullOrEmpty(value) || string.Equals(value, "currentcolor", StringComparison.OrdinalIgnoreCase);

    private static void PaintProgressOrMeter(PdfPage page, LayoutBox box, string tagName,
        float effectiveX, float pageHeightPx, float adjustedY, string? accentColorStr = null)
    {
        var elem = box.Element;
        if (elem == null) return;

        Color? accentColor = !string.IsNullOrEmpty(accentColorStr) ? ParseColor(accentColorStr) : null;

        float fraction = 0f;

        if (tagName == "progress")
        {
            var valueStr = elem.GetAttribute("value");
            var maxStr = elem.GetAttribute("max");
            if (string.IsNullOrEmpty(valueStr))
                fraction = IndeterminateProgressFill;
            else
            {
                float.TryParse(valueStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float value);
                float max = 1f;
                if (!string.IsNullOrEmpty(maxStr))
                    float.TryParse(maxStr, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out max);
                if (max <= 0) max = 1;
                fraction = Math.Max(0f, Math.Min(1f, value / max));
            }

            var fill = accentColor.HasValue ? ToRgb(accentColor.Value) : DefaultAccentRgb;
            PaintFillBar(page, box, effectiveX, pageHeightPx, adjustedY, fraction, fill.R, fill.G, fill.B);
        }
        else
        {
            var valueStr = elem.GetAttribute("value");
            var minStr = elem.GetAttribute("min");
            var maxStr = elem.GetAttribute("max");
            var lowStr = elem.GetAttribute("low");
            var highStr = elem.GetAttribute("high");

            float value = 0.5f;
            float min = 0f, max = 1f;
            if (!string.IsNullOrEmpty(valueStr))
                float.TryParse(valueStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value);
            if (!string.IsNullOrEmpty(minStr))
                float.TryParse(minStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out min);
            if (!string.IsNullOrEmpty(maxStr))
                float.TryParse(maxStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out max);
            if (max <= min) max = min + 1;

            fraction = Math.Max(0f, Math.Min(1f, (value - min) / (max - min)));

            float low = min + (max - min) * 0.25f;
            float high = min + (max - min) * 0.75f;
            if (!string.IsNullOrEmpty(lowStr))
                float.TryParse(lowStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out low);
            if (!string.IsNullOrEmpty(highStr))
                float.TryParse(highStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out high);

            (float R, float G, float B) fill;
            if (accentColor.HasValue)
                fill = ToRgb(accentColor.Value);
            else if (value >= low && value <= high)
                fill = MeterOptimumRgb;
            else if ((value < low && value >= min) || (value > high && value <= max))
                fill = MeterSuboptimumRgb;
            else
                fill = MeterEvenLessGoodRgb;

            PaintFillBar(page, box, effectiveX, pageHeightPx, adjustedY, fraction, fill.R, fill.G, fill.B);
        }
    }

    private static void PaintFillBar(PdfPage page, LayoutBox box, float effectiveX,
        float pageHeightPx, float adjustedY, float fraction, float r, float g, float b)
    {
        const float inset = 2f;
        float barX = (effectiveX + inset) * PdfCoordinates.PxToPt;
        float barY = (pageHeightPx - adjustedY - box.Height + inset) * PdfCoordinates.PxToPt;
        float maxW = (box.Width - inset * 2) * PdfCoordinates.PxToPt;
        float barH = (box.Height - inset * 2) * PdfCoordinates.PxToPt;
        float barW = maxW * fraction;

        if (barW <= 0 || barH <= 0) return;
        page.AddRectangle(barX, barY, barW, barH, r, g, b);
    }

    private static void PaintColorGlyphRun(PdfPage page, string text, float startX, float pdfY,
        string fontName, float pdfFontSize, float letterSpacing, float textR, float textG, float textB)
    {
        float x = startX;
        for (int i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            CurrentPdfDoc!.TryGetGlyphId(fontName, cp, out var gid);

            if (CurrentPdfDoc.TryGetColorLayers(fontName, cp, out var layers) && layers != null && layers.Count > 0)
                page.AddColorGlyphLayers(layers, x, pdfY, fontName, pdfFontSize, textR, textG, textB);
            else
                page.AddTextCID(new[] { gid }, x, pdfY, fontName, pdfFontSize, textR, textG, textB, 0, 0);

            CurrentPdfDoc.TryGetGlyphAdvancePt(fontName, gid, pdfFontSize, out var advance);
            x += advance + letterSpacing;
        }
    }

    private static void PaintCheckboxOrRadio(PdfPage page, LayoutBox box, string inputType,
        float effectiveX, float pageHeightPx, float adjustedY, string? accentColorStr,
        bool hasRadius, float tlrPt, float trrPt, float brrPt, float blrPt)
    {
        if (box.Width <= 0 || box.Height <= 0) return;
        if (box.Element == null || !box.Element.HasAttribute("checked")) return;

        Color? accentColor = !string.IsNullOrEmpty(accentColorStr) ? ParseColor(accentColorStr) : null;
        var (accR, accG, accB) = accentColor.HasValue ? ToRgb(accentColor.Value) : DefaultAccentRgb;

        float pdfX = effectiveX * PdfCoordinates.PxToPt;
        float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
        float pdfW = box.Width * PdfCoordinates.PxToPt;
        float pdfH = box.Height * PdfCoordinates.PxToPt;

        if (inputType == "radio")
        {
            float dotR = Math.Min(pdfW, pdfH) * 0.24f;
            float cx = pdfX + pdfW / 2f, cy = pdfY + pdfH / 2f;
            page.AddRoundedRectangle(cx - dotR, cy - dotR, dotR * 2f, dotR * 2f,
                accR, accG, accB, dotR, dotR, dotR, dotR);
            return;
        }

        if (hasRadius)
            page.AddRoundedRectangle(pdfX, pdfY, pdfW, pdfH, accR, accG, accB, tlrPt, trrPt, brrPt, blrPt);
        else
            page.AddRectangle(pdfX, pdfY, pdfW, pdfH, accR, accG, accB);

        float[] xs = { pdfX + pdfW * 0.22f, pdfX + pdfW * 0.42f, pdfX + pdfW * 0.80f };
        float[] ys = { pdfY + pdfH * 0.48f, pdfY + pdfH * 0.22f, pdfY + pdfH * 0.78f };
        page.AddStrokedPolyline(xs, ys, 1f, 1f, 1f, Math.Max(1f, pdfW * 0.12f));
    }

    private static void PaintSelectDropdownArrow(PdfPage page, LayoutBox box, float effectiveX, float pageHeightPx, float adjustedY)
    {
        const float arrowW = 8f, arrowH = 5f, rightInset = 8f;
        if (box.Width < arrowW + rightInset || box.Height < arrowH) return;

        float rightX = box.Width - rightInset;
        float centerYDown = box.Height / 2f;

        float apexX = effectiveX + (rightX - arrowW / 2f);
        float apexY = pageHeightPx - adjustedY - (centerYDown + arrowH / 2f);
        float leftX = effectiveX + (rightX - arrowW);
        float topY = pageHeightPx - adjustedY - (centerYDown - arrowH / 2f);
        float rightPx = effectiveX + rightX;

        page.AddFilledTriangle(
            apexX * PdfCoordinates.PxToPt, apexY * PdfCoordinates.PxToPt,
            leftX * PdfCoordinates.PxToPt, topY * PdfCoordinates.PxToPt,
            rightPx * PdfCoordinates.PxToPt, topY * PdfCoordinates.PxToPt,
            0.35f, 0.35f, 0.35f);
    }

    private static void ParseColumnRuleShorthand(string shorthand,
        ref string? width, ref string? style, ref string? color)
    {
        var parts = shorthand.Split(SpaceSep, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var p = part.Trim();
            if (string.IsNullOrEmpty(p)) continue;
            if (p == "none" || p == "solid" || p == "dashed" || p == "dotted" ||
                p == "double" || p == "groove" || p == "ridge" || p == "inset" || p == "outset")
            {
                style = p;
            }
            else if (p == "thin" || p == "medium" || p == "thick" ||
                     p.EndsWith("px", StringComparison.OrdinalIgnoreCase) ||
                     p.EndsWith("em", StringComparison.OrdinalIgnoreCase) ||
                     p.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
            {
                width = p;
            }
            else
            {
                color = p;
            }
        }
    }

    private static void ParseTextShadow(string shadow, float fontSize, Color? textColor,
        out float x, out float y, out float r, out float g, out float b, out float a)
    {
        x = y = 0;
        var fallback = textColor ?? Color.Black;
        r = fallback.R / 255f; g = fallback.G / 255f; b = fallback.B / 255f; a = fallback.A / 255f;
        if (string.IsNullOrEmpty(shadow) || shadow == "none") return;

        var parts = shadow.Trim().Split(SpaceSep, StringSplitOptions.RemoveEmptyEntries);
        int numIdx = 0;
        string? colorStr = null;

        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p.EndsWith("px") || p.EndsWith("em") || p.EndsWith("rem") ||
                (p.Length > 0 && (char.IsDigit(p[0]) || p[0] == '-' || p[0] == '.')))
            {
                float val = BlockLayout.ResolveLength(p, 0, fontSize);
                if (numIdx == 0) x = val;
                else if (numIdx == 1) y = val;
                numIdx++;
            }
            else
            {
                colorStr = colorStr == null ? p : colorStr + " " + p;
            }
        }

        if (!IsUnsetOrCurrentColor(colorStr))
        {
            var c = ParseColor(colorStr!);
            if (c.HasValue)
            {
                r = c.Value.R / 255f;
                g = c.Value.G / 255f;
                b = c.Value.B / 255f;
                a = c.Value.A / 255f;
            }
        }
    }

    private static void PaintBoxShadow(PdfPage page, LayoutBox box, float effectiveX,
        float pageHeightPx, float adjustedY)
    {
        var shadow = box.Style.Get("box-shadow");
        if (string.IsNullOrEmpty(shadow) || shadow == "none") return;

        float fontSize = 16;
        var fsStr = box.Style.FontSize;
        if (!string.IsNullOrEmpty(fsStr))
        {
            float resolved = BlockLayout.ResolveLength(fsStr, 0, 16);
            if (resolved > 0) fontSize = resolved;
        }

        var shadowList = SplitTopLevel(shadow, ',');
        foreach (var singleShadow in shadowList)
        {
            PaintSingleBoxShadow(page, box, effectiveX, pageHeightPx, adjustedY, singleShadow.Trim(), fontSize);
        }
    }

    private static void PaintSingleBoxShadow(PdfPage page, LayoutBox box, float effectiveX,
        float pageHeightPx, float adjustedY, string shadow, float fontSize)
    {
        var parts = shadow.Split(SpaceSep, StringSplitOptions.RemoveEmptyEntries);
        bool isInset = false;
        float sx = 0, sy = 0, blur = 0, spread = 0;
        string? colorStr = null;
        int numIdx = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (string.Equals(p, "inset", StringComparison.OrdinalIgnoreCase))
            {
                isInset = true;
            }
            else if (p.EndsWith("px") || p.EndsWith("em") ||
                (p.Length > 0 && (char.IsDigit(p[0]) || p[0] == '-' || p[0] == '.')))
            {
                float val = BlockLayout.ResolveLength(p, 0, fontSize);
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

        if (sa <= 0) return;

        if (isInset)
        {
            float elX = effectiveX * PdfCoordinates.PxToPt;
            float elY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
            float elW = box.Width * PdfCoordinates.PxToPt;
            float elH = box.Height * PdfCoordinates.PxToPt;

            float inX = (effectiveX + sx + spread) * PdfCoordinates.PxToPt;
            float inY = (pageHeightPx - adjustedY - box.Height + sy + spread) * PdfCoordinates.PxToPt;
            float inW = (box.Width - spread * 2) * PdfCoordinates.PxToPt;
            float inH = (box.Height - spread * 2) * PdfCoordinates.PxToPt;

            if (inW <= 0 || inH <= 0) return;

            page.SaveState();
            page.AddClipRect(elX, elY, elW, elH);
            if (sa < 1f) SetPaintOpacity(page, sa);
            page.AddRectangle(inX, inY, inW, inH, sr, sg, sb);
            if (sa < 1f) SetPaintOpacity(page, 1f);
            page.RestoreState();
        }
        else
        {
            float pdfX = (effectiveX + sx - spread) * PdfCoordinates.PxToPt;
            float pdfY = (pageHeightPx - adjustedY - box.Height - sy - spread) * PdfCoordinates.PxToPt;
            float pdfW = (box.Width + spread * 2) * PdfCoordinates.PxToPt;
            float pdfH = (box.Height + spread * 2) * PdfCoordinates.PxToPt;
            if (pdfW <= 0 || pdfH <= 0) return;

            float boxX = effectiveX * PdfCoordinates.PxToPt;
            float boxY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
            float boxW = box.Width * PdfCoordinates.PxToPt;
            float boxH = box.Height * PdfCoordinates.PxToPt;
            float outerX = Math.Min(pdfX, boxX), outerY = Math.Min(pdfY, boxY);
            float outerRight = Math.Max(pdfX + pdfW, boxX + boxW), outerTop = Math.Max(pdfY + pdfH, boxY + boxH);

            page.SaveState();
            ClipToRingBetween(page, outerX, outerY, outerRight - outerX, outerTop - outerY, boxX, boxY, boxW, boxH);
            if (sa < 1f) SetPaintOpacity(page, sa);
            float spreadPt = spread * PdfCoordinates.PxToPt;
            float tl = ShadowCornerRadius(box, "border-top-left-radius", spreadPt);
            float tr = ShadowCornerRadius(box, "border-top-right-radius", spreadPt);
            float br = ShadowCornerRadius(box, "border-bottom-right-radius", spreadPt);
            float bl = ShadowCornerRadius(box, "border-bottom-left-radius", spreadPt);
            if (tl > 0f || tr > 0f || br > 0f || bl > 0f)
                page.AddRoundedRectangle(pdfX, pdfY, pdfW, pdfH, sr, sg, sb, tl, tr, br, bl);
            else
                page.AddRectangle(pdfX, pdfY, pdfW, pdfH, sr, sg, sb);
            if (sa < 1f) SetPaintOpacity(page, 1f);
            page.RestoreState();
        }
    }

    private static float ShadowCornerRadius(LayoutBox box, string property, float spreadPt)
    {
        float radius = ResolveBorderRadius(box.Style, property, box.Width) * PdfCoordinates.PxToPt;
        return radius > 0f ? Math.Max(0f, radius + spreadPt) : 0f;
    }

    private static string? ResolveImageSet(string value)
    {
        int open = value.IndexOf('(');
        int close = value.LastIndexOf(')');
        if (open < 0 || close <= open) return null;
        string inner = value.Substring(open + 1, close - open - 1);

        string? bestUrl = null;
        float bestDeltaFrom1x = float.MaxValue;

        foreach (var rawCandidate in SplitTopLevel(inner, ','))
        {
            var candidate = rawCandidate.Trim();
            if (candidate.Length == 0) continue;

            string urlPart = candidate;
            float density = 1f;
            int lastSpace = candidate.LastIndexOf(' ');
            if (lastSpace >= 0)
            {
                string descriptor = candidate.Substring(lastSpace + 1).Trim();
                if (descriptor.EndsWith("x", StringComparison.OrdinalIgnoreCase) &&
                    float.TryParse(descriptor.Substring(0, descriptor.Length - 1), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float d))
                {
                    density = d;
                    urlPart = candidate.Substring(0, lastSpace).Trim();
                }
            }

            string url = ExtractUrlToken(urlPart);
            if (url.Length == 0) continue;

            float delta = Math.Abs(density - 1f);
            if (delta < bestDeltaFrom1x)
            {
                bestDeltaFrom1x = delta;
                bestUrl = url;
            }
        }

        return bestUrl;
    }

    private static string ExtractUrlToken(string token)
    {
        token = token.Trim();
        if (token.StartsWith("url(", StringComparison.OrdinalIgnoreCase) && token.EndsWith(")"))
            token = token.Substring(4, token.Length - 5).Trim();
        if (token.Length >= 2 && ((token[0] == '\'' && token[token.Length - 1] == '\'') ||
            (token[0] == '"' && token[token.Length - 1] == '"')))
            token = token.Substring(1, token.Length - 2);
        return token;
    }

    private static List<string> SplitTopLevel(string value, char delimiter)
    {
        var result = new List<string>();
        int depth = 0;
        char? quote = null;
        int start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (quote.HasValue)
            {
                if (c == quote.Value) quote = null;
                continue;
            }
            if (c == '\'' || c == '"') { quote = c; continue; }
            if (c == '(') depth++;
            else if (c == ')') { if (depth > 0) depth--; }
            else if (c == delimiter && depth == 0)
            {
                result.Add(value.Substring(start, i - start));
                start = i + 1;
            }
        }
        result.Add(value.Substring(start));
        return result;
    }

    private static float ParseObjectPositionFraction(string value, bool isVertical)
    {
        if (string.IsNullOrEmpty(value)) return 0.5f;
        var lower = value.ToLowerInvariant();
        if (lower == "left" || lower == "top") return 0f;
        if (lower == "center") return 0.5f;
        if (lower == "right" || lower == "bottom") return 1f;
        if (value.EndsWith("%") && float.TryParse(value.Substring(0, value.Length - 1),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pct))
            return pct / 100f;
        return 0.5f;
    }

    private static bool IgnoresExifOrientation(LayoutBox box) =>
        string.Equals(box.Style.Get("image-orientation"), "none", StringComparison.OrdinalIgnoreCase);

    private static int DisplayOrientation(LayoutBox box, PdfImage image) =>
        IgnoresExifOrientation(box) ? 1 : image.ExifOrientation;
}
