namespace EggPdf.Pdf;

/// <summary>
/// PDF/A conformance levels EggPdf can produce. Selecting one makes <see cref="PdfDocument"/>
/// embed an ICC output intent and XMP conformance metadata, and forces standard (non-embedded)
/// fonts to be embedded -- PDF/A requires every font referenced in content to be embedded.
/// </summary>
public enum PdfAConformance
{
    /// <summary>PDF/A-2b: ISO 19005-2 basic (visual) conformance, based on PDF 1.7.</summary>
    PdfA2b,

    /// <summary>PDF/A-3b: PDF/A-2b plus permission to embed arbitrary file attachments.</summary>
    PdfA3b,
}

/// <summary>The XMP <c>pdfaid:part</c>/<c>pdfaid:conformance</c> identifiers for each level.</summary>
public static class PdfAConformanceExtensions
{
    public static string Part(this PdfAConformance conformance) => conformance switch
    {
        PdfAConformance.PdfA3b => "3",
        _ => "2", // PdfA2b
    };

    public static string Level(this PdfAConformance conformance) => "B"; // both current levels are basic ("b")
}
