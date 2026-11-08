using System;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private const float DefaultObjectWidthPx = 300f;
    private const float DefaultObjectHeightPx = 150f;

    [ThreadStatic]
    private static Func<string, (float Width, float Height)?>? _threadImageSizeLoader;

    internal static Func<string, (float Width, float Height)?>? ImageSizeLoader
    {
        get => _threadImageSizeLoader;
        set => _threadImageSizeLoader = value;
    }

    internal static (float Width, float Height) ResolveImageBoxSize(ComputedStyle style, HtmlElement element, string? source,
        float containingWidth, float fontSize)
    {
        var loader = _threadImageSizeLoader;
        Func<(float Width, float Height)?> natural = () =>
            string.IsNullOrEmpty(source) || loader == null ? null : loader(source!);
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

        box.Width = box.ContentWidth = size.Width;
        box.Height = box.ContentHeight = size.Height;
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

        if (maxWidth.HasValue && width > maxWidth.Value)
        {
            width = maxWidth.Value;
            if (ratio > 0f) height = width / ratio;
        }
        if (maxHeight.HasValue && height > maxHeight.Value)
        {
            height = maxHeight.Value;
            if (ratio > 0f) width = height * ratio;
        }
        if (minWidth.HasValue && width < minWidth.Value)
        {
            width = minWidth.Value;
            if (ratio > 0f) height = width / ratio;
        }
        if (minHeight.HasValue && height < minHeight.Value)
        {
            height = minHeight.Value;
            if (ratio > 0f) width = height * ratio;
        }
        return (width, height);
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
