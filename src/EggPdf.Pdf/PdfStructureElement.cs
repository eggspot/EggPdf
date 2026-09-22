using System.Collections.Generic;

namespace EggPdf.Pdf;

/// <summary>
/// A node in a PDF/UA-1 tagged-PDF structure tree. <see cref="PdfDocument.StructureTree"/> holds
/// the root; <see cref="Kids"/> mixes nested structure elements (containers like Table/TR/L) with
/// direct content references (leaf text/image runs, as page+MCID pairs) in reading order -- both
/// are valid children of a PDF structure element and order matters for reading order.
/// </summary>
public class PdfStructureElement
{
    /// <summary>The PDF standard structure type: "Document", "H1"-"H6", "P", "Table", "TR", "TH", "TD", "L", "LI", "LBody", "Figure", "Link", "Span", "Div".</summary>
    public string Type { get; }

    /// <summary>Alternate text (required on Figure elements without visible text equivalent).</summary>
    public string? Alt { get; set; }

    /// <summary>Language override for this element's content, when it differs from the document default.</summary>
    public string? Lang { get; set; }

    /// <summary>Table header scope for TH elements: "Row", "Column", or "Both".</summary>
    public string? TableScope { get; set; }

    /// <summary>
    /// Ordered children: an entry is either a nested <see cref="PdfStructureElement"/> (Element set,
    /// PageIndex/Mcid ignored) or a leaf content reference (Element null, PageIndex/Mcid identify the
    /// marked-content span in a specific page's content stream).
    /// </summary>
    public List<(PdfStructureElement? Element, int PageIndex, int Mcid)> Kids { get; } = new();

    public PdfStructureElement(string type)
    {
        Type = type;
    }

    /// <summary>Append a nested structure element as a child.</summary>
    public PdfStructureElement AddChild(PdfStructureElement child)
    {
        Kids.Add((child, 0, 0));
        return child;
    }

    /// <summary>Append a leaf content reference (a marked-content span painted for this element).</summary>
    public void AddContentRef(int pageIndex, int mcid)
    {
        Kids.Add((null, pageIndex, mcid));
    }
}
