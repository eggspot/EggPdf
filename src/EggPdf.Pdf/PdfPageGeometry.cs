namespace EggPdf.Pdf;

/// <summary>
/// The size and orientation of one page of an existing PDF as a viewer shows it: the crop box (clipped to the
/// media box) with the page's rotation applied. Units are PDF points (1/72 inch); a page's <c>/UserUnit</c> scale
/// factor (rare, PDF 1.6+) is not applied.
/// </summary>
public sealed class PdfPageGeometry
{
    internal PdfPageGeometry(int pageNumber, double width, double height, int rotation)
    {
        PageNumber = pageNumber;
        Width = width;
        Height = height;
        Rotation = rotation;
    }

    /// <summary>1-based page number.</summary>
    public int PageNumber { get; }

    /// <summary>Displayed width in points (the crop box's height when the page is rotated 90 or 270 degrees).</summary>
    public double Width { get; }

    /// <summary>Displayed height in points.</summary>
    public double Height { get; }

    /// <summary>Clockwise rotation a viewer applies to the page: 0, 90, 180 or 270.</summary>
    public int Rotation { get; }
}
