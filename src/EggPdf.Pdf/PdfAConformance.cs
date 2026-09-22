namespace EggPdf.Pdf;

/// <summary>
/// PDF/A conformance levels EggPdf can produce. Selecting one makes <see cref="PdfDocument"/>
/// embed an ICC output intent and XMP conformance metadata, and forces standard (non-embedded)
/// fonts to be embedded -- PDF/A requires every font referenced in content to be embedded.
/// </summary>
public enum PdfAConformance
{
    /// <summary>
    /// PDF/A-1b: ISO 19005-1 basic (visual) conformance, based on PDF 1.4. Forbids transparency
    /// outright (partial opacity, blend modes other than Normal/Compatible, image alpha channels)
    /// -- rendering throws if the document uses any of these, rather than silently dropping them.
    /// </summary>
    PdfA1b,

    /// <summary>PDF/A-1u: PDF/A-1b plus guaranteed-correct Unicode text extraction (see <see cref="PdfA2u"/> for why this needs no extra writer behavior).</summary>
    PdfA1u,

    /// <summary>PDF/A-2b: ISO 19005-2 basic (visual) conformance, based on PDF 1.7.</summary>
    PdfA2b,

    /// <summary>PDF/A-3b: PDF/A-2b plus permission to embed arbitrary file attachments.</summary>
    PdfA3b,

    /// <summary>
    /// PDF/A-2u: PDF/A-2b plus guaranteed-correct Unicode text extraction. EggPdf's
    /// PDF/A embedding already routes every font through CIDFont Type 2 with a real
    /// ToUnicode CMap, so this adds no extra writer behavior over PdfA2b -- it only
    /// asserts the guarantee in the XMP conformance level.
    /// </summary>
    PdfA2u,

    /// <summary>PDF/A-3u: PDF/A-3b plus guaranteed-correct Unicode text extraction (see <see cref="PdfA2u"/>).</summary>
    PdfA3u,
}

/// <summary>The XMP <c>pdfaid:part</c>/<c>pdfaid:conformance</c> identifiers for each level.</summary>
public static class PdfAConformanceExtensions
{
    public static string Part(this PdfAConformance conformance) => conformance switch
    {
        PdfAConformance.PdfA1b or PdfAConformance.PdfA1u => "1",
        PdfAConformance.PdfA3b or PdfAConformance.PdfA3u => "3",
        _ => "2", // PdfA2b, PdfA2u
    };

    public static string Level(this PdfAConformance conformance) => conformance switch
    {
        PdfAConformance.PdfA1u or PdfAConformance.PdfA2u or PdfAConformance.PdfA3u => "U",
        _ => "B", // PdfA1b, PdfA2b, PdfA3b
    };

    /// <summary>PDF/A-1 is based on PDF 1.4 and forbids transparency outright.</summary>
    public static bool IsPart1(this PdfAConformance conformance)
        => conformance == PdfAConformance.PdfA1b || conformance == PdfAConformance.PdfA1u;
}
