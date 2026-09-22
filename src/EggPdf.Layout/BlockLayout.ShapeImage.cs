using System;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    /// <summary>
    /// Supplies an image's alpha channel (width, height, one byte per pixel) so <c>shape-outside: url()</c> can wrap
    /// text around the image's opaque pixels. Set per render by the pipeline (images are otherwise decoded only
    /// after layout); when it is null or returns null the float keeps its rectangular exclusion.
    /// </summary>
    public static Func<string, (int width, int height, byte[] alpha)?>? ShapeImageLoader
    {
        get => ShapeOutsideParser.ImageLoader;
        set => ShapeOutsideParser.ImageLoader = value;
    }
}
