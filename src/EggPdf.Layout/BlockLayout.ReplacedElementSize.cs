using System;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private const float DefaultObjectWidthPx = 300f;
    private const float DefaultObjectHeightPx = 150f;

    private const int FirstTransposingExifOrientation = 5;
    private const int LastExifOrientation = 8;

    [ThreadStatic]
    private static Func<string, (float Width, float Height, int ExifOrientation)?>? _threadImageSizeLoader;

    internal static Func<string, (float Width, float Height, int ExifOrientation)?>? ImageSizeLoader
    {
        get => _threadImageSizeLoader;
        set => _threadImageSizeLoader = value;
    }

    internal static (float Width, float Height) ResolveImageBoxSize(ComputedStyle style, HtmlElement element, string? source,
        float containingWidth, float fontSize)
    {
        var loader = _threadImageSizeLoader;
        bool honoursOrientation = !string.Equals(style.Get("image-orientation"), "none", StringComparison.OrdinalIgnoreCase);
        Func<(float Width, float Height)?> natural = () =>
        {
            if (string.IsNullOrEmpty(source) || loader == null || loader(source!) is not { } stored) return null;
            bool transposed = honoursOrientation &&
                              stored.ExifOrientation >= FirstTransposingExifOrientation && stored.ExifOrientation <= LastExifOrientation;
            return transposed ? (stored.Height, stored.Width) : (stored.Width, stored.Height);
        };
        return ResolveReplacedBoxSize(style, element, natural, containingWidth, fontSize, DefaultImageWidthPx, DefaultImageWidthPx);
    }

    internal static (float Width, float Height) ResolveSvgBoxSize(ComputedStyle style, HtmlElement svg,
        float containingWidth, float fontSize) =>
        ResolveReplacedBoxSize(style, svg, () => SvgIntrinsicSize(svg), containingWidth, fontSize, DefaultSvgWidthPx, DefaultObjectHeightPx);

    private static LayoutBox SizeReplacedElementBox(LayoutBox box, HtmlElement element, ComputedStyle style,
        float containingWidth, float fontSize)
    {
        (float Width, float Height) size;
        if (element.TagName == "svg")
        {
            size = ResolveSvgBoxSize(style, element, containingWidth, fontSize);
        }
        else
        {
            box.ImageSource = ResolveSrcset(element.GetAttribute("srcset"),
                    ExplicitImageWidth(style, element, containingWidth, fontSize) ?? DefaultImageWidthPx)
                ?? element.GetAttribute("src");
            size = ResolveImageBoxSize(style, element, box.ImageSource, containingWidth, fontSize);
        }

        float horizontalInsets = box.PaddingLeft + box.PaddingRight;
        float verticalInsets = box.PaddingTop + box.PaddingBottom;
        bool borderBox = style.Get("box-sizing") == "border-box";
        bool widthIncludesInsets = borderBox && IsSpecified(style.Width);
        bool heightIncludesInsets = borderBox && IsSpecified(style.Height);
        box.ContentWidth = widthIncludesInsets ? Math.Max(0f, size.Width - horizontalInsets) : size.Width;
        box.ContentHeight = heightIncludesInsets ? Math.Max(0f, size.Height - verticalInsets) : size.Height;
        box.Width = box.ContentWidth + horizontalInsets;
        box.Height = box.ContentHeight + verticalInsets;
        return box;
    }

    internal static float? ExplicitImageWidth(ComputedStyle style, HtmlElement element, float containingWidth, float fontSize) =>
        ExplicitReplacedDimension(style.Width, element.GetAttribute("width"), containingWidth, fontSize);

    private static (float Width, float Height) ResolveReplacedBoxSize(ComputedStyle style, HtmlElement element,
        Func<(float Width, float Height)?> naturalSize, float containingWidth, float fontSize, float defaultWidth, float defaultHeight)
    {
        float? width = ExplicitReplacedDimension(style.Width, element.GetAttribute("width"), containingWidth, fontSize);
        float? height = ExplicitReplacedDimension(style.Height, element.GetAttribute("height"), null, fontSize);

        float resolvedWidth, resolvedHeight;
        float ratio = 0f;
        if (width.HasValue && height.HasValue)
        {
            resolvedWidth = width.Value;
            resolvedHeight = height.Value;
        }
        else if (naturalSize() is { } natural && natural.Width > 0 && natural.Height > 0)
        {
            ratio = natural.Width / natural.Height;
            if (width.HasValue) { resolvedWidth = width.Value; resolvedHeight = resolvedWidth / ratio; }
            else if (height.HasValue) { resolvedHeight = height.Value; resolvedWidth = resolvedHeight * ratio; }
            else { resolvedWidth = natural.Width; resolvedHeight = natural.Height; }
        }
        else
        {
            resolvedWidth = width ?? defaultWidth;
            resolvedHeight = height ?? defaultHeight;
        }

        bool keepRatio = ratio > 0f && !(width.HasValue && height.HasValue);
        return ApplyReplacedSizeLimits(style, resolvedWidth, resolvedHeight, keepRatio ? ratio : 0f, containingWidth, fontSize);
    }

    private static (float Width, float Height) ApplyReplacedSizeLimits(ComputedStyle style, float width, float height,
        float ratio, float containingWidth, float fontSize)
    {
        float? maxWidth = ResolveOptionalLength(NoneAsNull(style.Get("max-width")), containingWidth, fontSize);
        float? minWidth = ResolveOptionalLength(style.Get("min-width"), containingWidth, fontSize);
        float? maxHeight = DefiniteHeightLimit(NoneAsNull(style.Get("max-height")), fontSize);
        float? minHeight = DefiniteHeightLimit(style.Get("min-height"), fontSize);

        float minW = Math.Max(0f, minWidth ?? 0f), minH = Math.Max(0f, minHeight ?? 0f);
        float maxW = Math.Max(minW, maxWidth ?? float.PositiveInfinity);
        float maxH = Math.Max(minH, maxHeight ?? float.PositiveInfinity);
        if (ratio <= 0f || width <= 0f || height <= 0f)
            return (Clamp(width, minW, maxW), Clamp(height, minH, maxH));
        return ConstrainKeepingRatio(width, height, minW, maxW, minH, maxH);
    }

    private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));

    private static bool IsSpecified(string? length) => !string.IsNullOrEmpty(length) && length != "auto";

    private static (float Width, float Height) ConstrainKeepingRatio(float w, float h, float minW, float maxW, float minH, float maxH)
    {
        bool wideOver = w > maxW, wideUnder = w < minW, tallOver = h > maxH, tallUnder = h < minH;
        if (wideOver && tallOver)
            return maxW / w <= maxH / h ? (maxW, Math.Max(minH, maxW * h / w)) : (Math.Max(minW, maxH * w / h), maxH);
        if (wideUnder && tallUnder)
            return minW / w <= minH / h ? (Math.Min(maxW, minH * w / h), minH) : (minW, Math.Min(maxH, minW * h / w));
        if (wideUnder && tallOver) return (minW, maxH);
        if (wideOver && tallUnder) return (maxW, minH);
        if (wideOver) return (maxW, Math.Max(maxW * h / w, minH));
        if (wideUnder) return (minW, Math.Min(minW * h / w, maxH));
        if (tallOver) return (Math.Max(maxH * w / h, minW), maxH);
        if (tallUnder) return (Math.Min(minH * w / h, maxW), minH);
        return (w, h);
    }

    private static string? NoneAsNull(string? value) => value == "none" ? null : value;

    private static float? DefiniteHeightLimit(string? value, float fontSize) =>
        string.IsNullOrEmpty(value) || value!.EndsWith("%", StringComparison.Ordinal) ? null : ResolveOptionalLength(value, 0f, fontSize);

    private static float? ExplicitReplacedDimension(string? cssValue, string? htmlAttribute, float? containingSize, float fontSize)
    {
        if (!string.IsNullOrEmpty(cssValue) && cssValue != "auto")
        {
            if (containingSize == null && cssValue!.EndsWith("%", StringComparison.Ordinal)) return null;
            var resolved = ResolveOptionalLength(cssValue, containingSize ?? 0f, fontSize);
            if (resolved.HasValue) return resolved.Value;
        }
        return ParseLengthAttribute(htmlAttribute, containingSize);
    }

    private static float? ParseLengthAttribute(string? value, float? containingSize)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var text = value!.Trim();
        int end = 0;
        while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.')) end++;
        if (end == 0 || !float.TryParse(text.Substring(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
            return null;
        if (end < text.Length && text[end] == '%')
            return containingSize.HasValue ? number / 100f * containingSize.Value : null;
        return number;
    }

    internal static (float Width, float Height)? SvgIntrinsicSize(HtmlElement svg)
    {
        float? width = ParseLengthAttribute(svg.GetAttribute("width"), null);
        float? height = ParseLengthAttribute(svg.GetAttribute("height"), null);
        bool hasViewBox = TryParseViewBoxSize(svg.GetAttribute("viewBox") ?? svg.GetAttribute("viewbox"), out float viewBoxWidth, out float viewBoxHeight);

        if (width.HasValue && height.HasValue) return (width.Value, height.Value);
        if (!hasViewBox) return null;

        float ratio = viewBoxWidth / viewBoxHeight;
        if (width.HasValue) return (width.Value, width.Value / ratio);
        if (height.HasValue) return (height.Value * ratio, height.Value);
        return ratio > DefaultObjectWidthPx / DefaultObjectHeightPx
            ? (DefaultObjectWidthPx, DefaultObjectWidthPx / ratio)
            : (DefaultObjectHeightPx * ratio, DefaultObjectHeightPx);
    }

    private static bool TryParseViewBoxSize(string? viewBox, out float width, out float height)
    {
        width = height = 0f;
        if (string.IsNullOrEmpty(viewBox)) return false;
        var parts = viewBox!.Split(new[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4 &&
               float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out width) &&
               float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out height) &&
               width > 0f && height > 0f;
    }
}
