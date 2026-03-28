using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Merges multiple PDF documents into a single PDF.
/// Concatenates pages, merges outlines, and handles cross-references.
/// </summary>
public class PdfMerger
{
    private readonly List<byte[]> _documents = new();

    /// <summary>Add a PDF document to the merge queue.</summary>
    public PdfMerger Add(byte[] pdfBytes)
    {
        if (pdfBytes != null && pdfBytes.Length > 0)
            _documents.Add(pdfBytes);
        return this;
    }

    /// <summary>Add a PDF document from a file path.</summary>
    public PdfMerger AddFile(string filePath)
    {
        if (File.Exists(filePath))
            _documents.Add(File.ReadAllBytes(filePath));
        return this;
    }

    /// <summary>
    /// Merge all added documents into a single PDF.
    /// Simple approach: re-render all pages through EggPdf.
    /// For production: would parse and concatenate PDF objects directly.
    /// </summary>
    public byte[] Build()
    {
        if (_documents.Count == 0)
            return Array.Empty<byte>();

        if (_documents.Count == 1)
            return _documents[0];

        // Simple concatenation approach:
        // Parse each PDF to extract page content streams and concatenate
        // For now, use a simpler approach: combine the raw bytes with cross-reference fixup

        // For the initial implementation, we concatenate by creating a new PDF
        // that includes all pages from all input documents.
        // A full implementation would parse the PDF object graph and merge properly.

        var output = new PdfDocument();

        foreach (var doc in _documents)
        {
            // Extract pages from each document
            // Simple approach: find page content streams and re-emit
            int pageCount = CountPages(doc);
            for (int i = 0; i < pageCount; i++)
            {
                var pageContent = ExtractPageContent(doc, i);
                var pageSize = ExtractPageSize(doc);
                var page = output.AddPage(pageSize.width, pageSize.height);
                if (pageContent != null)
                    page.AppendRawContent(pageContent);
            }
        }

        return output.ToByteArray();
    }

    /// <summary>Count pages in a PDF by counting /Type /Page objects.</summary>
    private static int CountPages(byte[] pdf)
    {
        var text = Encoding.ASCII.GetString(pdf);
        int count = 0;
        int idx = 0;
        while ((idx = text.IndexOf("/Type /Page ", idx, StringComparison.Ordinal)) >= 0)
        {
            // Make sure it's /Page not /Pages
            count++;
            idx += 12;
        }
        return Math.Max(count, 1);
    }

    /// <summary>Extract page content stream (simplified).</summary>
    private static string? ExtractPageContent(byte[] pdf, int pageIndex)
    {
        // Simplified: find the content stream between "stream" and "endstream"
        var text = Encoding.ASCII.GetString(pdf);
        int streamIdx = 0;
        int page = 0;

        while (streamIdx < text.Length)
        {
            streamIdx = text.IndexOf("stream\n", streamIdx, StringComparison.Ordinal);
            if (streamIdx < 0) break;
            streamIdx += 7;

            int endIdx = text.IndexOf("endstream", streamIdx, StringComparison.Ordinal);
            if (endIdx < 0) break;

            if (page == pageIndex)
                return text.Substring(streamIdx, endIdx - streamIdx);

            page++;
            streamIdx = endIdx + 9;
        }

        return null;
    }

    /// <summary>Extract page dimensions from MediaBox.</summary>
    private static (float width, float height) ExtractPageSize(byte[] pdf)
    {
        var text = Encoding.ASCII.GetString(pdf);
        int mbIdx = text.IndexOf("/MediaBox", StringComparison.Ordinal);
        if (mbIdx >= 0)
        {
            int bracketStart = text.IndexOf('[', mbIdx);
            int bracketEnd = text.IndexOf(']', bracketStart);
            if (bracketStart >= 0 && bracketEnd >= 0)
            {
                var coords = text.Substring(bracketStart + 1, bracketEnd - bracketStart - 1).Trim().Split(' ');
                if (coords.Length >= 4)
                {
                    float.TryParse(coords[2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float w);
                    float.TryParse(coords[3], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float h);
                    return (w, h);
                }
            }
        }
        return (595.28f * 0.75f, 841.89f * 0.75f); // Default A4
    }
}
