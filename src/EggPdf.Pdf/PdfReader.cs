using System;
using System.Collections.Generic;

namespace EggPdf.Pdf;

/// <summary>
/// Reads an existing PDF from any producer: classic and stream cross-references, incremental updates, compressed
/// object streams, and files whose cross-reference is damaged (repaired by scanning). Safe for untrusted uploads:
/// nesting depth, page-tree depth, cross-reference size and decompressed stream size are capped, and a file that
/// can't be read throws <see cref="PdfFormatException"/>.
/// </summary>
public sealed class PdfReader
{
    private const int MaxPageTreeDepth = 64;
    private const int MaxPages = 1_000_000;
    private const int HeaderSearchWindow = 1024;

    private readonly List<PdfPageGeometry> _pages;

    private PdfReader(PdfObjectStore store, List<PdfPageGeometry> pages)
    {
        Store = store;
        _pages = pages;
    }

    internal PdfObjectStore Store { get; }

    /// <summary>Number of pages in the document.</summary>
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
        try
        {
            var store = PdfObjectStore.Load(pdf);
            return new PdfReader(store, ReadPages(store));
        }
        catch (Exception ex) when (ex is not PdfFormatException && ex is not NotSupportedException && ex is not OutOfMemoryException)
        {
            throw new PdfFormatException("The PDF is damaged and can't be read.", ex);
        }
    }

    /// <summary>Counts the pages of a PDF held in memory; same as <c>Open(pdf).PageCount</c>.</summary>
    /// <exception cref="PdfFormatException">The bytes aren't a readable PDF, or exceed a safety limit.</exception>
    public static int GetPageCount(byte[] pdf) => Open(pdf).PageCount;

    /// <summary>The displayed size and rotation of page <paramref name="pageNumber"/> (1-based).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageNumber"/> is below 1 or above <see cref="PageCount"/>.</exception>
    public PdfPageGeometry GetPage(int pageNumber)
    {
        if (pageNumber < 1 || pageNumber > _pages.Count)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber, "Page numbers run from 1 to PageCount.");
        return _pages[pageNumber - 1];
    }

    private static List<PdfPageGeometry> ReadPages(PdfObjectStore store)
    {
        var catalog = store.FindCatalog()!;
        var root = store.Resolve(catalog.Get("Pages")) as PdfDictionary
            ?? throw new PdfFormatException("The document catalog has no page tree.");

        var pages = new List<PdfPageGeometry>();
        var visited = new HashSet<PdfDictionary>();
        var pending = new Stack<(PdfDictionary Node, double[]? MediaBox, double[]? CropBox, int Rotation, int Depth)>();
        pending.Push((root, null, null, 0, 0));
        while (pending.Count > 0)
        {
            var (node, inheritedMedia, inheritedCrop, inheritedRotation, depth) = pending.Pop();
            if (!visited.Add(node)) throw new PdfFormatException("The page tree contains a cycle.");
            if (depth > MaxPageTreeDepth) throw new PdfFormatException("The page tree is nested too deeply.");

            var mediaBox = ReadRectangle(store, node.Get("MediaBox")) ?? inheritedMedia;
            var cropBox = ReadRectangle(store, node.Get("CropBox")) ?? inheritedCrop;
            int rotation = store.Resolve(node.Get("Rotate")) is PdfNumber rotate ? NormalizeRotation(rotate.Value) : inheritedRotation;

            var kids = store.Resolve(node.Get("Kids")) as PdfArray;
            bool isPage = node.Get("Type") is PdfName { Value: "Page" } || kids == null;
            if (isPage)
            {
                if (pages.Count >= MaxPages) throw new PdfFormatException("The document has too many pages.");
                pages.Add(Geometry(pages.Count + 1, mediaBox, cropBox, rotation));
                continue;
            }

            for (int i = kids!.Items.Count - 1; i >= 0; i--)
                if (store.Resolve(kids.Items[i]) is PdfDictionary kid)
                    pending.Push((kid, mediaBox, cropBox, rotation, depth + 1));
        }
        return pages;
    }

    private static PdfPageGeometry Geometry(int pageNumber, double[]? mediaBox, double[]? cropBox, int rotation)
    {
        var media = mediaBox ?? new[] { 0.0, 0.0, 612.0, 792.0 };
        var visible = cropBox == null ? media : Intersect(cropBox, media) ?? media;
        double width = visible[2] - visible[0];
        double height = visible[3] - visible[1];
        return rotation % 180 == 0
            ? new PdfPageGeometry(pageNumber, width, height, rotation)
            : new PdfPageGeometry(pageNumber, height, width, rotation);
    }

    private static double[]? ReadRectangle(PdfObjectStore store, PdfObject? value)
    {
        if (store.Resolve(value) is not PdfArray array || array.Items.Count != 4) return null;
        var numbers = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (store.Resolve(array.Items[i]) is not PdfNumber number) return null;
            numbers[i] = number.Value;
        }
        var rectangle = new[]
        {
            Math.Min(numbers[0], numbers[2]), Math.Min(numbers[1], numbers[3]),
            Math.Max(numbers[0], numbers[2]), Math.Max(numbers[1], numbers[3]),
        };
        return rectangle[2] > rectangle[0] && rectangle[3] > rectangle[1] ? rectangle : null;
    }

    private static double[]? Intersect(double[] a, double[] b)
    {
        var result = new[] { Math.Max(a[0], b[0]), Math.Max(a[1], b[1]), Math.Min(a[2], b[2]), Math.Min(a[3], b[3]) };
        return result[2] > result[0] && result[3] > result[1] ? result : null;
    }

    private static int NormalizeRotation(double degrees)
    {
        if (double.IsNaN(degrees) || double.IsInfinity(degrees) || degrees % 90 != 0) return 0;
        int normalized = (int)(degrees % 360);
        return normalized < 0 ? normalized + 360 : normalized;
    }
}
