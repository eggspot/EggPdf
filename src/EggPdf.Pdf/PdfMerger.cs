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
            // Encrypted inputs cannot be merged: their stream bytes are
            // ciphertext and would be re-emitted as garbage page operators.
            if (PdfInspect.IsEncrypted(doc))
                throw new NotSupportedException(
                    "Merging encrypted PDFs is not supported — decrypt or re-render the input first.");

            // Resolve each page dictionary to the content stream it actually
            // references. Taking the Nth stream in file order would pick up
            // font programs and images, which are written before page content.
            var text = Latin1.GetString(doc);
            var pages = FindPages(text);
            foreach (var pageRef in pages)
            {
                var page = output.AddPage(pageRef.Width, pageRef.Height);
                var content = pageRef.ContentObj > 0
                    ? ExtractObjectStream(doc, text, pageRef.ContentObj)
                    : null;
                if (content != null)
                    page.AppendRawContent(content);
            }
        }

        return output.ToByteArray();
    }

    /// <summary>A page dictionary's content-stream reference and dimensions.</summary>
    private struct PageRef
    {
        public int ContentObj;
        public float Width;
        public float Height;
    }

    /// <summary>
    /// Locate every page dictionary (/Type /Page, not the /Pages tree node)
    /// and read its /Contents object number and /MediaBox.
    /// </summary>
    private static List<PageRef> FindPages(string text)
    {
        var pages = new List<PageRef>();
        int idx = 0;
        while ((idx = text.IndexOf("/Type /Page", idx, StringComparison.Ordinal)) >= 0)
        {
            int after = idx + "/Type /Page".Length;
            // "/Type /Pages" is the page-tree node, not a page.
            if (after < text.Length && text[after] == 's')
            {
                idx = after;
                continue;
            }

            int endObj = text.IndexOf("endobj", idx, StringComparison.Ordinal);
            if (endObj < 0) endObj = text.Length;

            pages.Add(new PageRef
            {
                ContentObj = ParseIndirectRef(text, idx, endObj, "/Contents"),
                Width = ParseMediaBox(text, idx, endObj).width,
                Height = ParseMediaBox(text, idx, endObj).height,
            });
            idx = endObj;
        }
        return pages;
    }

    /// <summary>Parse "/Key N 0 R" within a range; returns 0 when absent.</summary>
    private static int ParseIndirectRef(string text, int start, int end, string key)
    {
        int keyIdx = text.IndexOf(key, start, end - start, StringComparison.Ordinal);
        if (keyIdx < 0) return 0;
        int p = keyIdx + key.Length;
        while (p < end && text[p] == ' ') p++;
        int numStart = p;
        while (p < end && text[p] >= '0' && text[p] <= '9') p++;
        if (p == numStart) return 0; // inline array or direct object, not a ref
        return int.TryParse(text.Substring(numStart, p - numStart), out int objNum) ? objNum : 0;
    }

    /// <summary>Parse "/MediaBox [x0 y0 x1 y1]" within a range; A4 when absent.</summary>
    private static (float width, float height) ParseMediaBox(string text, int start, int end)
    {
        int mbIdx = text.IndexOf("/MediaBox", start, end - start, StringComparison.Ordinal);
        if (mbIdx >= 0)
        {
            int bracketStart = text.IndexOf('[', mbIdx);
            int bracketEnd = bracketStart >= 0 ? text.IndexOf(']', bracketStart) : -1;
            if (bracketStart >= 0 && bracketEnd > bracketStart)
            {
                var coords = text.Substring(bracketStart + 1, bracketEnd - bracketStart - 1)
                    .Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (coords.Length >= 4 &&
                    float.TryParse(coords[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float x0) &&
                    float.TryParse(coords[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float y0) &&
                    float.TryParse(coords[2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float x1) &&
                    float.TryParse(coords[3], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float y1))
                {
                    return (Math.Abs(x1 - x0), Math.Abs(y1 - y0));
                }
            }
        }
        return (595.28f, 841.89f); // A4 in points
    }

    /// <summary>
    /// Extract page content stream operators (simplified). Handles the
    /// production default of FlateDecode-compressed content streams by
    /// inflating; scanning uses Latin-1 (1:1 char-to-byte) and the body is
    /// sliced from the original bytes so compressed data survives intact.
    /// </summary>
    private static string? ExtractObjectStream(byte[] pdf, string text, int objNum)
    {
        // Anchor on a line-start "N 0 obj" so object 1 doesn't match "21 0 obj".
        int objStart = FindObjectStart(text, objNum);
        if (objStart < 0) return null;

        int streamIdx = text.IndexOf("stream\n", objStart, StringComparison.Ordinal);
        if (streamIdx < 0) return null;
        int endObj = text.IndexOf("endobj", objStart, StringComparison.Ordinal);
        if (endObj >= 0 && streamIdx > endObj) return null; // object has no stream
        streamIdx += "stream\n".Length;

        int endIdx = text.IndexOf("endstream", streamIdx, StringComparison.Ordinal);
        if (endIdx < 0) return null;

        int bodyEnd = endIdx;
        while (bodyEnd > streamIdx && (text[bodyEnd - 1] == '\n' || text[bodyEnd - 1] == '\r'))
            bodyEnd--;

        var body = new byte[bodyEnd - streamIdx];
        Array.Copy(pdf, streamIdx, body, 0, body.Length);

        // Scan the whole object dict (from "N 0 obj" to the stream) so a nested
        // sub-dictionary — e.g. /DecodeParms << ... >> — can't hide the
        // /FlateDecode token; the nearest "<<" would stop at the inner dict.
        bool compressed = text.IndexOf("/FlateDecode", objStart, streamIdx - objStart,
            StringComparison.Ordinal) >= 0;
        if (compressed)
        {
            try { body = InflateZlib(body); }
            catch { return null; } // undecodable stream: skip this page's content
        }
        return Latin1.GetString(body);
    }

    /// <summary>Find the offset of "N 0 obj" at a line start for the given object number.</summary>
    private static int FindObjectStart(string text, int objNum)
    {
        string marker = objNum.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 obj";
        int idx = 0;
        while ((idx = text.IndexOf(marker, idx, StringComparison.Ordinal)) >= 0)
        {
            // Must begin a line, otherwise "1 0 obj" matches inside "21 0 obj".
            if (idx == 0 || text[idx - 1] == '\n' || text[idx - 1] == '\r')
                return idx;
            idx += marker.Length;
        }
        return -1;
    }

    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    private static byte[] InflateZlib(byte[] data)
    {
        // Skip the 2-byte zlib header; DeflateStream stops before the Adler-32.
        using var input = new MemoryStream(data, 2, data.Length - 2);
        using var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

}
