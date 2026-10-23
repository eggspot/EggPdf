namespace EggPdf.Pdf;

internal enum PdfPageTreeNodeType { Unspecified, Page, Pages, Other }

internal readonly struct PdfPageTreeSummary
{
    public static readonly PdfArray DirectKidsArray = new();

    public PdfPageTreeSummary(PdfPageTreeNodeType type, PdfObject? kids, PdfObject? count)
    {
        Type = type;
        Kids = kids;
        Count = count;
    }

    public PdfPageTreeNodeType Type { get; }
    public PdfObject? Kids { get; }
    public PdfObject? Count { get; }

    public static PdfPageTreeSummary Of(PdfDictionary node)
    {
        var type = (node.Get("Type") as PdfName)?.Value switch
        {
            null => PdfPageTreeNodeType.Unspecified,
            "Page" => PdfPageTreeNodeType.Page,
            "Pages" => PdfPageTreeNodeType.Pages,
            _ => PdfPageTreeNodeType.Other,
        };
        return new PdfPageTreeSummary(type, node.Get("Kids"), node.Get("Count"));
    }
}
