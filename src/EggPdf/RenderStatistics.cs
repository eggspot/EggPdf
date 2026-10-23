namespace EggPdf;

/// <summary>
/// Facts about the PDF a render wrote, for callers that need them without parsing the bytes (e.g. billing
/// per page). Pass one as <see cref="PdfRenderOptions.Statistics"/> (or Fluent
/// <c>DocumentDescriptor.CollectStatistics</c>) and read it after the render returns. Every render resets it
/// when it is called, so a render that throws or is cancelled leaves it at zero. Not thread-safe: use one
/// per in-flight render.
/// </summary>
public sealed class RenderStatistics
{
    /// <summary>Number of pages in the written PDF (every named page group included).</summary>
    public int PageCount { get; internal set; }

    internal void Reset() => PageCount = 0;
}
