using EggPdf.Pdf;

namespace EggPdf;

/// <summary>
/// Optional page/document and PDF-writer settings for <see cref="HtmlToPdf"/>. The page/document
/// properties (PageSize, Margin*, Title, Author, UserStyleSheet) are translated internally into
/// the equivalent CSS (an injected <c>@page</c> rule, a <c>&lt;title&gt;</c> tag, etc.) and
/// appended to the document's &lt;head&gt; before rendering -- CSS remains the single source of
/// truth for layout, and because the generated rule is appended last, it wins the cascade over
/// any conflicting <c>@page</c> rule already in the HTML. Encryption/Conformance/Invoice are
/// PDF-writer-level settings with no CSS equivalent; they're applied directly to the
/// <see cref="Pdf.PdfDocument"/> that gets written. This class exists so every entry point
/// (<see cref="HtmlToPdf"/>, the CLI, the REST API) can share one options shape rather than each
/// growing its own; it does not add a parallel layout engine.
/// </summary>
public class PdfRenderOptions
{
    /// <summary>Named page size: A3, A4, A5, Letter, Legal, Tabloid, etc.</summary>
    public string? PageSize { get; set; }

    /// <summary>"portrait" or "landscape".</summary>
    public string? Orientation { get; set; }

    /// <summary>Page margin in CSS pixels, applied to all four sides. Overridden per-side by the MarginTop/Right/Bottom/Left properties below.</summary>
    public float? Margin { get; set; }

    /// <summary>Top page margin in CSS pixels. Overrides <see cref="Margin"/> for this side.</summary>
    public float? MarginTop { get; set; }

    /// <summary>Right page margin in CSS pixels. Overrides <see cref="Margin"/> for this side.</summary>
    public float? MarginRight { get; set; }

    /// <summary>Bottom page margin in CSS pixels. Overrides <see cref="Margin"/> for this side.</summary>
    public float? MarginBottom { get; set; }

    /// <summary>Left page margin in CSS pixels. Overrides <see cref="Margin"/> for this side.</summary>
    public float? MarginLeft { get; set; }

    /// <summary>PDF document title metadata. Falls back to the HTML's own &lt;title&gt; tag when not set.</summary>
    public string? Title { get; set; }

    /// <summary>PDF document author metadata. Falls back to the HTML's own &lt;meta name="author"&gt; tag when not set.</summary>
    public string? Author { get; set; }

    /// <summary>Additional CSS injected into the document's &lt;head&gt;, after any @page rule generated from the properties above.</summary>
    public string? UserStyleSheet { get; set; }

    /// <summary>Optional RC4 encryption settings for the rendered PDF. Cannot be combined with <see cref="Conformance"/> (PDF/A forbids encryption).</summary>
    public PdfEncryption? Encryption { get; set; }

    /// <summary>Optional PDF/A conformance level for the rendered PDF.</summary>
    public PdfAConformance? Conformance { get; set; }

    /// <summary>Optional Factur-X/ZUGFeRD invoice data (MINIMUM profile) to embed. Requires <see cref="Conformance"/> to be <see cref="PdfAConformance.PdfA3b"/> or <see cref="PdfAConformance.PdfA3u"/>.</summary>
    public FacturXInvoice? Invoice { get; set; }

    /// <summary>
    /// Produce a PDF/UA-1 tagged PDF (structure tree, alt text, /Lang, /MarkInfo). Combinable
    /// with <see cref="Conformance"/> for a PDF/A + PDF/UA-1 document. Not yet supported with
    /// named page groups (<c>page: &lt;name&gt;</c> + <c>@page &lt;name&gt;</c>).
    /// </summary>
    public bool Tagged { get; set; }
}
