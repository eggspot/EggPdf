namespace EggPdf;

/// <summary>
/// Facts about the PDF a render wrote, for callers that need them without parsing the bytes (e.g. billing
/// per page). Pass one as <see cref="PdfRenderOptions.Statistics"/> (or Fluent
/// <c>DocumentDescriptor.CollectStatistics</c>) and read it after the render returns. Use one per render:
/// each render resets it when it starts, so a render that throws leaves it at zero.
/// </summary>
public sealed class RenderStatistics
{
    /// <summary>Number of pages in the written PDF (every named page group included).</summary>
    public int PageCount { get; internal set; }
}
