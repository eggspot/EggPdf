using System;

namespace EggPdf.Pdf;

/// <summary>
/// Reads an existing PDF from any producer: classic and stream cross-references, incremental updates, compressed
/// object streams, and files whose cross-reference is damaged (repaired by scanning). Safe for untrusted uploads:
/// nesting depth, page-tree depth, cross-reference size and decompressed stream size are capped, and a file that
/// can't be read throws <see cref="PdfFormatException"/>. Pages are read on demand, so opening a document and
/// looking up a page cost about the same for a 150,000-page file as for a one-page file.
/// </summary>
public sealed class PdfReader
{
    private const int HeaderSearchWindow = 1024;

    private readonly PdfPageTree _pages;

    private PdfReader(PdfObjectStore store, PdfPageTree pages)
    {
        Store = store;
        _pages = pages;
    }

    internal PdfObjectStore Store { get; }

    /// <summary>
    /// Number of pages in the document, from the page tree's <c>/Count</c>. When the tree's counts don't match the
    /// pages it holds, the pages are counted instead.
    /// </summary>
    public int PageCount => _pages.Count;

    /// <summary>True when the document is encrypted (it has an /Encrypt dictionary); pages can usually still be counted.</summary>
    public bool IsEncrypted => Store.IsEncrypted;

    /// <summary>Opens a PDF held in memory.</summary>
    /// <exception cref="PdfFormatException">The bytes aren't a readable PDF, or exceed a safety limit.</exception>
    /// <exception cref="NotSupportedException">The pages sit inside an encrypted object stream.</exception>
    public static PdfReader Open(byte[] pdf)
    {
        if (pdf == null) throw new ArgumentNullException(nameof(pdf));
        if (PdfSyntaxParser.IndexOf(pdf, "%PDF-", 0, Math.Min(pdf.Length, HeaderSearchWindow)) < 0)
            throw new PdfFormatException("The data doesn't start with a %PDF- header.");
        return Guarded(() =>
        {
            var store = PdfObjectStore.Load(pdf);
            return new PdfReader(store, new PdfPageTree(store));
        });
    }

    /// <summary>Counts the pages of a PDF held in memory; same as <c>Open(pdf).PageCount</c>.</summary>
    /// <exception cref="PdfFormatException">The bytes aren't a readable PDF, or exceed a safety limit.</exception>
    /// <exception cref="NotSupportedException">The pages sit inside an encrypted object stream.</exception>
    public static int GetPageCount(byte[] pdf) => Open(pdf).PageCount;

    /// <summary>The displayed size and rotation of page <paramref name="pageNumber"/> (1-based).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageNumber"/> is below 1 or above <see cref="PageCount"/>.</exception>
    /// <exception cref="PdfFormatException">The page's part of the page tree is damaged or inconsistent.</exception>
    /// <exception cref="NotSupportedException">The page sits inside an encrypted object stream.</exception>
    public PdfPageGeometry GetPage(int pageNumber) => GetPageNode(pageNumber).Geometry;

    internal PdfPageNode GetPageNode(int pageNumber)
    {
        if (pageNumber < 1 || pageNumber > _pages.Count)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber, "Page numbers run from 1 to PageCount.");
        return Guarded(() =>
        {
            Store.BeginOperation();
            return _pages.Find(pageNumber);
        });
    }

    private static T Guarded<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is not PdfFormatException && ex is not NotSupportedException && ex is not OutOfMemoryException)
        {
            throw new PdfFormatException("The PDF is damaged and can't be read.", ex);
        }
    }
}
