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
    private const int NormalExifOrientation = 1;

    private static Func<string, (float Width, float Height, int ExifOrientation)?> CreateImageSizeLoader(ResourceLoader resources)
    {
        var measured = new Dictionary<string, (float Width, float Height, int ExifOrientation)?>(StringComparer.Ordinal);
        return source =>
        {
            if (measured.TryGetValue(source, out var known)) return known;
            var size = MeasureImage(resources.Load(source, ResourceType.Image));
            measured[source] = size;
            return size;
        };
    }

    private static (float Width, float Height, int ExifOrientation)? MeasureImage(byte[]? data)
    {
        if (data == null) return null;
        if (ImageIntrinsicSize.TryRead(data, out int width, out int height, out int orientation))
            return (width, height, orientation);

        var svg = EggPdf.Svg.SvgImageDocument.ParseRoot(data);
        if (svg == null) return null;
        var (svgWidth, svgHeight) = BlockLayout.SvgIntrinsicSize(svg) ?? (DefaultSvgImageWidthPx, DefaultSvgImageHeightPx);
        return (svgWidth, svgHeight, NormalExifOrientation);
    }
}
