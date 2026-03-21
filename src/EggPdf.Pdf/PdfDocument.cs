using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Generates a valid PDF 1.7 document. Handles object numbering, xref table, and trailer.
/// </summary>
public class PdfDocument
{
    private readonly List<PdfPage> _pages = new();
    public string? Title { get; set; }
    public string? Author { get; set; }

    /// <summary>Add a page with dimensions in PDF points.</summary>
    public PdfPage AddPage(float widthPt, float heightPt)
    {
        var page = new PdfPage(widthPt, heightPt);
        _pages.Add(page);
        return page;
    }

    /// <summary>Write the PDF to a byte array.</summary>
    public byte[] ToByteArray()
    {
        using var ms = new MemoryStream();
        WriteTo(ms);
        return ms.ToArray();
    }

    /// <summary>Write the PDF to a stream.</summary>
    public void WriteTo(Stream output)
    {
        var writer = new PdfStreamWriter(output);

        // Header
        writer.WriteLine("%PDF-1.7");
        writer.WriteLine("%\xE2\xE3\xCF\xD3"); // binary marker

        // Collect all fonts used across pages
        var allFonts = new HashSet<string>();
        foreach (var page in _pages)
            foreach (var font in page.UsedFonts)
                allFonts.Add(font);

        // Object numbering plan:
        // 1: Catalog
        // 2: Pages
        // 3..N: Page objects (each page = page dict + content stream = 2 objects)
        // Then: font resources, info dict
        int nextObj = 1;
        int catalogObj = nextObj++;
        int pagesObj = nextObj++;

        // Pre-calculate page objects
        var pageObjs = new List<(int pageDict, int contentStream, int? annotArray)>();
        foreach (var page in _pages)
        {
            int pd = nextObj++;
            int cs = nextObj++;
            int? ann = page.Links.Count > 0 ? nextObj++ : null;
            if (page.Links.Count > 0)
            {
                foreach (var _ in page.Links)
                    nextObj++; // annotation objects
            }
            pageObjs.Add((pd, cs, ann));
        }

        // Font objects
        var fontObjs = new Dictionary<string, int>();
        foreach (var font in allFonts)
            fontObjs[font] = nextObj++;

        // Info dictionary
        int infoObj = nextObj++;

        var offsets = new Dictionary<int, long>();

        // Write Catalog
        offsets[catalogObj] = writer.Position;
        writer.WriteLine($"{catalogObj} 0 obj");
        writer.WriteLine($"<< /Type /Catalog /Pages {pagesObj} 0 R >>");
        writer.WriteLine("endobj");

        // Write Pages
        offsets[pagesObj] = writer.Position;
        writer.WriteLine($"{pagesObj} 0 obj");
        var kids = string.Join(" ", pageObjs.ConvertAll(p => $"{p.pageDict} 0 R"));
        writer.WriteLine($"<< /Type /Pages /Kids [{kids}] /Count {_pages.Count} >>");
        writer.WriteLine("endobj");

        // Write font objects
        foreach (var kv in fontObjs)
        {
            offsets[kv.Value] = writer.Position;
            writer.WriteLine($"{kv.Value} 0 obj");
            writer.WriteLine($"<< /Type /Font /Subtype /Type1 /BaseFont /{kv.Key} >>");
            writer.WriteLine("endobj");
        }

        // Build font resource dict reference
        var fontResources = new StringBuilder();
        fontResources.Append("<< ");
        foreach (var kv in fontObjs)
            fontResources.Append($"/{kv.Key} {kv.Value} 0 R ");
        fontResources.Append(">>");

        // Write each page
        for (int i = 0; i < _pages.Count; i++)
        {
            var page = _pages[i];
            var (pageDictObj, contentStreamObj, annotArrayObj) = pageObjs[i];

            // Content stream
            var contentBytes = Encoding.ASCII.GetBytes(page.ContentStream.ToString());

            offsets[contentStreamObj] = writer.Position;
            writer.WriteLine($"{contentStreamObj} 0 obj");
            writer.WriteLine($"<< /Length {contentBytes.Length} >>");
            writer.WriteLine("stream");
            writer.WriteBytes(contentBytes);
            writer.WriteLine("");
            writer.WriteLine("endstream");
            writer.WriteLine("endobj");

            // Link annotations
            var annotRefs = new List<string>();
            if (page.Links.Count > 0)
            {
                int annotStartObj = annotArrayObj!.Value + 1 - page.Links.Count; // wrong calc, let me fix
                // Actually we need to track annotation object numbers properly
            }

            // Write annotation objects
            var annotObjNumbers = new List<int>();
            if (page.Links.Count > 0)
            {
                foreach (var link in page.Links)
                {
                    // Find next annotation obj number
                    // We already allocated them sequentially after annotArrayObj
                }
            }

            // Page dictionary
            offsets[pageDictObj] = writer.Position;
            writer.WriteLine($"{pageDictObj} 0 obj");
            var pageDict = new StringBuilder();
            pageDict.Append("<< /Type /Page");
            pageDict.Append($" /Parent {pagesObj} 0 R");
            pageDict.Append($" /MediaBox [0 0 {F(page.WidthPt)} {F(page.HeightPt)}]");
            pageDict.Append($" /Contents {contentStreamObj} 0 R");
            if (allFonts.Count > 0)
                pageDict.Append($" /Resources << /Font {fontResources} >>");

            // Add link annotations inline (simpler approach)
            if (page.Links.Count > 0)
            {
                pageDict.Append(" /Annots [");
                foreach (var link in page.Links)
                {
                    float x1 = link.X;
                    float y1 = link.Y;
                    float x2 = link.X + link.Width;
                    float y2 = link.Y + link.Height;
                    pageDict.Append($" << /Type /Annot /Subtype /Link /Rect [{F(x1)} {F(y1)} {F(x2)} {F(y2)}]");
                    pageDict.Append($" /Border [0 0 0]");
                    pageDict.Append($" /A << /Type /Action /S /URI /URI ({link.Url}) >> >>");
                }
                pageDict.Append(" ]");
            }

            pageDict.Append(" >>");
            writer.WriteLine(pageDict.ToString());
            writer.WriteLine("endobj");
        }

        // Info dictionary
        offsets[infoObj] = writer.Position;
        writer.WriteLine($"{infoObj} 0 obj");
        var info = new StringBuilder();
        info.Append("<< /Producer (EggPdf)");
        if (!string.IsNullOrEmpty(Title))
            info.Append($" /Title ({EscapePdfString(Title)})");
        if (!string.IsNullOrEmpty(Author))
            info.Append($" /Author ({EscapePdfString(Author)})");
        info.Append($" /CreationDate (D:{DateTime.UtcNow:yyyyMMddHHmmss}Z)");
        info.Append(" >>");
        writer.WriteLine(info.ToString());
        writer.WriteLine("endobj");

        // Cross-reference table
        long xrefOffset = writer.Position;
        int totalObjects = nextObj;
        writer.WriteLine("xref");
        writer.WriteLine($"0 {totalObjects}");
        writer.WriteLine("0000000000 65535 f ");

        for (int obj = 1; obj < totalObjects; obj++)
        {
            if (offsets.TryGetValue(obj, out long offset))
                writer.WriteLine($"{offset:D10} 00000 n ");
            else
                writer.WriteLine("0000000000 00000 f ");
        }

        // Trailer
        writer.WriteLine("trailer");
        writer.WriteLine($"<< /Size {totalObjects} /Root {catalogObj} 0 R /Info {infoObj} 0 R >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefOffset.ToString());
        writer.WriteLine("%%EOF");
    }

    private static string F(float value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string EscapePdfString(string text)
        => text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}

/// <summary>Helper for tracking byte positions while writing.</summary>
internal class PdfStreamWriter
{
    private readonly Stream _stream;
    public long Position => _stream.Position;

    public PdfStreamWriter(Stream stream) { _stream = stream; }

    public void WriteLine(string line)
    {
        var bytes = Encoding.ASCII.GetBytes(line + "\n");
        _stream.Write(bytes, 0, bytes.Length);
    }

    public void WriteBytes(byte[] data)
    {
        _stream.Write(data, 0, data.Length);
    }
}
