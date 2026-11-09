using System;
using EggPdf.Core;
using EggPdf.Css;
using EggPdf.Layout;
using EggPdf.Pdf;

namespace EggPdf.Paint;

public static partial class BoxPainter
{
    private static (float X, float Top, float Width, float Height) ContentRect(LayoutBox box, float effectiveX, float adjustedY) =>
        (effectiveX + box.PaddingLeft,
         adjustedY + box.PaddingTop,
         Math.Max(0f, box.Width - box.PaddingLeft - box.PaddingRight),
         Math.Max(0f, box.Height - box.PaddingTop - box.PaddingBottom));

    /// <summary>
    /// Clip subsequent drawing to the box's border shape at the given rectangle (PDF points): a rounded
    /// rectangle when the box has a <c>border-radius</c>, else a plain rectangle.
    /// </summary>
    public static void ClipToBox(PdfPage page, LayoutBox box, float x, float y, float width, float height)
    {
        float topLeft = ResolveBorderRadius(box.Style, "border-top-left-radius", box.Width) * PdfCoordinates.PxToPt;
        float topRight = ResolveBorderRadius(box.Style, "border-top-right-radius", box.Width) * PdfCoordinates.PxToPt;
        float bottomRight = ResolveBorderRadius(box.Style, "border-bottom-right-radius", box.Width) * PdfCoordinates.PxToPt;
        float bottomLeft = ResolveBorderRadius(box.Style, "border-bottom-left-radius", box.Width) * PdfCoordinates.PxToPt;
        if (topLeft > 0f || topRight > 0f || bottomRight > 0f || bottomLeft > 0f)
            page.AddRoundedClip(x, y, width, height, topLeft, topRight, bottomRight, bottomLeft);
        else
            page.AddClipRect(x, y, width, height);
    }

    internal static bool ClipsDescendantPaint(ComputedStyle style)
    {
        var overflow = style.Get("overflow");
        if (overflow == "hidden" || overflow == "clip") return true;
        var overflowX = style.Get("overflow-x");
        var overflowY = style.Get("overflow-y");
        if (overflowX == "hidden" || overflowY == "hidden" || (overflowX == "clip" && overflowY == "clip")) return true;

        var contain = style.Get("contain");
        return !string.IsNullOrEmpty(contain) &&
            (contain!.IndexOf("paint", StringComparison.OrdinalIgnoreCase) >= 0 ||
             contain.IndexOf("strict", StringComparison.OrdinalIgnoreCase) >= 0 ||
             contain.IndexOf("content", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static void ClipToRingBetween(PdfPage page,
        float outerX, float outerY, float outerWidth, float outerHeight,
        float innerX, float innerY, float innerWidth, float innerHeight)
    {
        page.AppendRawContent(
            Pt(outerX) + " " + Pt(outerY) + " " + Pt(outerWidth) + " " + Pt(outerHeight) + " re\n" +
            Pt(innerX) + " " + Pt(innerY) + " " + Pt(innerWidth) + " " + Pt(innerHeight) + " re\n" +
            "W* n\n");
    }

    private static string Pt(float value) => value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
}
