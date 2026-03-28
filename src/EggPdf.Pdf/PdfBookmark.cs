namespace EggPdf.Pdf;

/// <summary>
/// Represents a PDF bookmark (document outline entry) derived from HTML headings.
/// </summary>
public class PdfBookmark
{
    /// <summary>The display title of the bookmark.</summary>
    public string Title { get; set; } = "";

    /// <summary>Heading level (1-6 for h1-h6).</summary>
    public int Level { get; set; }

    /// <summary>Zero-based page index the bookmark points to.</summary>
    public int PageIndex { get; set; }

    /// <summary>Y position in PDF points (from bottom of page).</summary>
    public float TopPt { get; set; }
}
