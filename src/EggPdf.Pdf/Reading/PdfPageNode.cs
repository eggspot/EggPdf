namespace EggPdf.Pdf;

internal sealed class PdfPageNode
{
    public PdfPageNode(PdfReference? reference, PdfDictionary dictionary, double[] visibleBox, int rotation, PdfObject? resources,
        PdfPageGeometry geometry)
    {
        Reference = reference;
        Dictionary = dictionary;
        VisibleBox = visibleBox;
        Rotation = rotation;
        Resources = resources;
        Geometry = geometry;
    }

    public PdfReference? Reference { get; }

    public PdfDictionary Dictionary { get; }

    public double[] VisibleBox { get; }

    public int Rotation { get; }

    public PdfObject? Resources { get; }

    public PdfPageGeometry Geometry { get; }
}
