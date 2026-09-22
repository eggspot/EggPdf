using System;
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
    /// (PDF/A disallows encryption). PDF/A-1 additionally forbids transparency outright --
    /// <see cref="ValidatePdfA1NoTransparency"/> throws rather than silently drop it.
    /// </summary>
    public PdfAConformance? Conformance { get; set; }

    /// <summary>The PDF header version PDF/A-1 requires (it's based on PDF 1.4); every other mode keeps the writer's normal 1.7.</summary>
    private string PdfVersionHeader => Conformance != null && Conformance.Value.IsPart1() ? "%PDF-1.4" : "%PDF-1.7";

    /// <summary>
    /// PDF/A-1 (ISO 19005-1) forbids transparency outright: partial opacity, blend modes other
    /// than Normal/Compatible, and image alpha channels are all disallowed (veraPDF rules
    /// 6.4-1/2/4/5). Rather than silently dropping transparency the document actually asked for
    /// (producing visually wrong output) or silently ignoring the violation (producing a
    /// mislabeled non-conformant PDF), this throws so the caller can either remove the
    /// transparency or use PdfA2b/PdfA2u/PdfA3b/PdfA3u instead. A no-op unless Conformance is
    /// PdfA1b/PdfA1u.
    /// </summary>
    private void ValidatePdfA1NoTransparency()
    {
        if (Conformance == null || !Conformance.Value.IsPart1()) return;

        foreach (var page in _pages)
        {
            foreach (var gs in page.UsedExtGStates)
            {
                if (gs.StartsWith("GSBM_", StringComparison.Ordinal))
                {
                    var cssName = gs.Substring(5).Replace('_', '-');
                    var pdfBm = PdfPage.CssBlendModeToPdf(cssName) ?? "Normal";
                    if (pdfBm != "Normal" && pdfBm != "Compatible")
                        throw new InvalidOperationException(
                            $"PDF/A-1 forbids transparency: blend mode '{cssName}' is not allowed. Remove it, or use PdfA2b/PdfA2u/PdfA3b/PdfA3u instead.");
                }
                else if (gs.StartsWith("GS", StringComparison.Ordinal) &&
                         int.TryParse(gs.Substring(2), out int pct) && pct != 100)
                {
                    throw new InvalidOperationException(
                        $"PDF/A-1 forbids transparency: partial opacity ({pct}%) is not allowed. Remove it, or use PdfA2b/PdfA2u/PdfA3b/PdfA3u instead.");
                }
            }
        }

        foreach (var image in _images.Values)
        {
            if (image.SMaskData != null)
                throw new InvalidOperationException(
                    "PDF/A-1 forbids transparency: images with an alpha channel are not allowed. Remove the image's transparency, or use PdfA2b/PdfA2u/PdfA3b/PdfA3u instead.");
        }
    }

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
