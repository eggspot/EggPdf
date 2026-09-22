using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// PDF/A conformance support for <see cref="PdfDocument"/>: the ICC output intent and XMP
/// metadata objects, kept separate from the core writer in <c>PdfDocument.cs</c>.
/// </summary>
public partial class PdfDocument
{
    /// <summary>
    /// Optional PDF/A conformance level. When set, embeds an ICC output intent and XMP
    /// conformance metadata, forces full font embedding, and forbids <see cref="Encryption"/>
    /// (PDF/A disallows encryption).
    /// </summary>
    public PdfAConformance? Conformance { get; set; }

    /// <summary>Reserve object numbers for the ICC profile and XMP metadata streams (a no-op unless <see cref="Conformance"/> is set).</summary>
    private (int iccProfileObj, int metadataObj) AllocateConformanceObjects(PdfObjectAllocator alloc)
        => Conformance != null ? (alloc.Allocate(), alloc.Allocate()) : (0, 0);

    /// <summary>Append the catalog's /Metadata and /OutputIntents entries (a no-op unless <see cref="Conformance"/> is set).</summary>
    private void AppendConformanceCatalogEntries(StringBuilder catalogDict, int iccProfileObj, int metadataObj)
    {
        if (Conformance == null) return;
        catalogDict.Append($" /Metadata {metadataObj} 0 R");
        catalogDict.Append($" /OutputIntents [{PdfACompliance.GenerateOutputIntentDict(iccProfileObj)}]");
    }

    /// <summary>Write the ICC profile stream and XMP metadata stream objects (a no-op unless <see cref="Conformance"/> is set).</summary>
    private void WriteConformanceObjects(PdfStreamWriter writer, PdfObjectAllocator alloc, int iccProfileObj, int metadataObj)
    {
        if (Conformance == null) return;

        byte[] iccBytes = IccSrgbProfile.Generate();
        alloc.RecordOffset(iccProfileObj, writer.Position);
        writer.WriteLine($"{iccProfileObj} 0 obj");
        writer.WriteLine($"<< /N 3 /Alternate /DeviceRGB /Length {iccBytes.Length} >>");
        writer.WriteLine("stream");
        writer.WriteBytes(iccBytes);
        writer.WriteLine("");
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");

        string? facturXFileName = Invoice != null ? FacturXFileName : null;
        byte[] xmpBytes = Encoding.UTF8.GetBytes(PdfACompliance.GenerateXmpMetadata(Title, Author, Conformance.Value, facturXFileName));
        alloc.RecordOffset(metadataObj, writer.Position);
        writer.WriteLine($"{metadataObj} 0 obj");
        writer.WriteLine($"<< /Type /Metadata /Subtype /XML /Length {xmpBytes.Length} >>");
        writer.WriteLine("stream");
        writer.WriteBytes(xmpBytes);
        writer.WriteLine("");
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
    }
}
