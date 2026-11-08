using EggPdf.Layout;
using EggPdf.Pdf;

namespace EggPdf.Paint;

public static partial class BoxPainter
{
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
}
