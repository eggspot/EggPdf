using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Factur-X/ZUGFeRD e-invoice support for <see cref="PdfDocument"/>: embeds the CII invoice XML
/// as a PDF/A-3 file attachment (<c>/EmbeddedFiles</c> name tree + <c>/AF</c> associated file),
/// kept separate from the core writer in <c>PdfDocument.cs</c>.
/// </summary>
public partial class PdfDocument
{
    internal const string FacturXFileName = "factur-x.xml";

    /// <summary>
    /// Optional Factur-X/ZUGFeRD invoice data. Produces the MINIMUM profile with no line items,
    /// or an EN 16931-conformant document with one or more (see <see cref="FacturXInvoice"/>).
    /// When set, requires <see cref="Conformance"/> to be <see cref="PdfAConformance.PdfA3b"/> or
    /// <see cref="PdfAConformance.PdfA3u"/> -- Factur-X's embedded-XML attachment is only
    /// permitted under PDF/A-3.
    /// </summary>
    public FacturXInvoice? Invoice { get; set; }

    /// <summary>Reserve object numbers for the embedded file stream and its filespec (a no-op unless <see cref="Invoice"/> is set).</summary>
    private (int embeddedFileObj, int filespecObj) AllocateFacturXObjects(PdfObjectAllocator alloc)
        => Invoice != null ? (alloc.Allocate(), alloc.Allocate()) : (0, 0);

    /// <summary>Append the catalog's /Names/EmbeddedFiles and /AF entries (a no-op unless <see cref="Invoice"/> is set).</summary>
    private void AppendFacturXCatalogEntries(StringBuilder catalogDict, int filespecObj)
    {
        if (Invoice == null) return;
        catalogDict.Append($" /Names << /EmbeddedFiles << /Names [({FacturXFileName}) {filespecObj} 0 R] >> >>");
        catalogDict.Append($" /AF [{filespecObj} 0 R]");
    }

    /// <summary>Write the embedded-file stream and filespec objects (a no-op unless <see cref="Invoice"/> is set).</summary>
    private void WriteFacturXObjects(PdfStreamWriter writer, PdfObjectAllocator alloc, int embeddedFileObj, int filespecObj)
    {
        if (Invoice == null) return;

        byte[] xmlBytes = Encoding.UTF8.GetBytes(FacturXCiiWriter.Generate(Invoice));

        alloc.RecordOffset(embeddedFileObj, writer.Position);
        writer.WriteLine($"{embeddedFileObj} 0 obj");
        writer.WriteLine($"<< /Type /EmbeddedFile /Subtype /text#2Fxml /Length {xmlBytes.Length} >>");
        writer.WriteLine("stream");
        writer.WriteBytes(xmlBytes);
        writer.WriteLine("");
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");

        // /AFRelationship /Data is correct for the MINIMUM/BASIC WL profiles; EN16931 (and above)
        // requires /Alternative for German legal validity.
        bool en16931 = FacturXCiiWriter.IsEn16931(Invoice);
        string relationship = en16931 ? "Alternative" : "Data";
        string desc = en16931 ? "Factur-X EN 16931 invoice data" : "Factur-X MINIMUM invoice data";

        alloc.RecordOffset(filespecObj, writer.Position);
        writer.WriteLine($"{filespecObj} 0 obj");
        writer.WriteLine($"<< /Type /Filespec /F ({FacturXFileName}) /UF ({FacturXFileName})");
        writer.WriteLine($"/EF << /F {embeddedFileObj} 0 R /UF {embeddedFileObj} 0 R >>");
        writer.WriteLine($"/AFRelationship /{relationship} /Desc ({desc}) >>");
        writer.WriteLine("endobj");
    }
}
