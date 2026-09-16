namespace EggPdf;

/// <summary>
/// Optional page/document settings for <see cref="HtmlToPdf"/>. These are translated
/// internally into the equivalent CSS (an injected <c>@page</c> rule, a <c>&lt;title&gt;</c>
/// tag, etc.) and appended to the document's &lt;head&gt; before rendering -- CSS remains the
/// single source of truth for layout, and because the generated rule is appended last, it wins
/// the cascade over any conflicting <c>@page</c> rule already in the HTML. This class exists
/// for callers who would rather set a few C# properties than write CSS by hand; it does not
/// add a parallel layout engine.
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
}
