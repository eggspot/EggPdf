using EggPdf.Html.Dom;

namespace EggPdf.Layout;

/// <summary>
/// Shared by every per-word <see cref="LayoutBox"/> fragment that one line of an inline element's
/// text content splits into (see <c>BlockLayout.InlineRuns.cs</c>'s <c>LayoutInlineRuns</c>) --
/// multi-word inline elements (<c>&lt;a&gt;</c>, <c>&lt;strong&gt;</c>, etc.) produce one
/// <see cref="LayoutBox"/> per word for line-breaking, and only the first fragment keeps a direct
/// <see cref="LayoutBox.Element"/> reference (background/border painting stays first-fragment-only,
/// unchanged by this type). A consumer that needs the full extent of one line of the element --
/// a multi-word <c>&lt;a&gt;</c> link annotation, PDF/UA-1 structure tagging -- reads
/// <see cref="LayoutBox.InlineSpan"/> instead of a single fragment's own bounds. Wrapping resets to
/// a new span (see the width/height union in <see cref="Union"/> vs. starting fresh), so one
/// inline element that spans multiple lines gets one span per line, matching how a PDF link
/// annotation's rectangle can't itself wrap.
/// </summary>
public class InlineElementSpan
{
    public HtmlElement Element { get; }
    public float X { get; private set; }
    public float Y { get; private set; }
    public float Width { get; private set; }
    public float Height { get; private set; }

    /// <summary>
    /// Opaque slot for a consumer (the paint layer) to cache state keyed to "have I already
    /// handled this span" -- e.g. the <c>PdfLinkAnnotation</c> already created for it, so a second
    /// word-fragment on the same line doesn't create a second, overlapping annotation. Untyped so
    /// this layout-layer type doesn't need to reference the PDF writer's types.
    /// </summary>
    public object? PaintTag { get; set; }

    public InlineElementSpan(HtmlElement element, float x, float y, float width, float height)
    {
        Element = element;
        X = x; Y = y; Width = width; Height = height;
    }

    /// <summary>Extend this span's rect to also cover another fragment's bounds on the same line.</summary>
    public void Union(float x, float y, float width, float height)
    {
        float right = System.Math.Max(X + Width, x + width);
        float bottom = System.Math.Max(Y + Height, y + height);
        X = System.Math.Min(X, x);
        Y = System.Math.Min(Y, y);
        Width = right - X;
        Height = bottom - Y;
    }
}
