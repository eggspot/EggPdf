using System;
using EggPdf.Pdf;

namespace EggPdf;

public static partial class HtmlToPdf
{
    /// <summary>
    /// The alpha channel of an image for <c>shape-outside: url()</c>. Opaque images are a solid rectangle; JPEG and
    /// other formats this pipeline can't decode to pixels report null (the float keeps its rectangular exclusion).
    /// </summary>
    private static (int width, int height, byte[] alpha)? LoadShapeImageAlpha(string source)
    {
        var data = LoadImageData(source);
        if (data == null) return null;

        var image = DecodeImage(source, data);
        if (image == null || image.Format != PdfImageFormat.Raw) return null;

        var alpha = image.SMaskData;
        if (alpha == null)
        {
            alpha = new byte[image.Width * image.Height];
            for (int i = 0; i < alpha.Length; i++) alpha[i] = 255;
        }
        return (image.Width, image.Height, alpha);
    }
}
