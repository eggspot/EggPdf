using System;
using System.Collections.Generic;
using EggPdf.Core.Resources;
using EggPdf.Layout;
using EggPdf.Pdf;

namespace EggPdf;

public static partial class HtmlToPdf
{
    private const float DefaultSvgImageWidthPx = 300f;
    private const float DefaultSvgImageHeightPx = 150f;

    private static Func<string, (float Width, float Height)?> CreateImageSizeLoader(ResourceLoader resources)
    {
        var measured = new Dictionary<string, (float Width, float Height)?>(StringComparer.Ordinal);
        return source =>
        {
            if (measured.TryGetValue(source, out var known)) return known;
            var size = MeasureImage(resources.Load(source, ResourceType.Image));
            measured[source] = size;
            return size;
        };
    }

    private static (float Width, float Height)? MeasureImage(byte[]? data)
    {
        if (data == null) return null;
        if (ImageIntrinsicSize.TryRead(data, out int width, out int height, out int orientation))
            return ImageIntrinsicSize.SwapsWidthAndHeight(orientation) ? (height, width) : (width, height);

        var svg = SvgImages.ParseRoot(data);
        if (svg == null) return null;
        return BlockLayout.SvgIntrinsicSize(svg) ?? (DefaultSvgImageWidthPx, DefaultSvgImageHeightPx);
    }
}
