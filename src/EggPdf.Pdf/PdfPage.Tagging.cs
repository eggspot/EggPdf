namespace EggPdf.Pdf;

/// <summary>
/// Marked-content support for PDF/UA-1 tagging: assigning MCIDs and wrapping painted content in
/// BDC/EMC, kept separate from the core page writer in <c>PdfPage.cs</c>.
/// </summary>
public partial class PdfPage
{
    /// <summary>This page's position in <see cref="PdfDocument"/>'s page list, set by <see cref="PdfDocument.AddPage"/>. Doubles as the page's /StructParents key.</summary>
    public int PageIndex { get; internal set; }

    /// <summary>Next marked-content ID to assign on this page (MCIDs are per-page, starting at 0).</summary>
    private int _nextMcid;

    /// <summary>True once at least one marked-content span has been opened -- signals the page needs /StructParents.</summary>
    internal bool HasMarkedContent { get; private set; }

    /// <summary>
    /// Open a marked-content sequence (<c>/&lt;tag&gt; &lt;&lt; /MCID n &gt;&gt; BDC</c>) for
    /// content about to be painted and return its MCID. Callers must pair this with
    /// <see cref="EndMarkedContent"/> (in a try/finally) even if painting throws.
    /// </summary>
    public int BeginMarkedContent(string tag)
    {
        int mcid = _nextMcid++;
        HasMarkedContent = true;
        ContentStream.AppendOpLine($"/{tag} << /MCID {mcid} >> BDC");
        return mcid;
    }

    /// <summary>Close the marked-content sequence opened by <see cref="BeginMarkedContent"/>.</summary>
    public void EndMarkedContent()
    {
        ContentStream.AppendOpLine("EMC");
    }
}
