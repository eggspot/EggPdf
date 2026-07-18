using System;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>Lightweight structural probes over raw PDF bytes.</summary>
internal static class PdfInspect
{
    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    /// <summary>
    /// True if the PDF declares an /Encrypt entry, whether written as a direct
    /// dictionary (/Encrypt &lt;&lt; ... &gt;&gt;) or an indirect reference
    /// (/Encrypt 12 0 R). Substring matching on "/Encrypt &lt;&lt;" alone misses
    /// the indirect form, which is what most non-EggPdf writers emit.
    /// </summary>
    public static bool IsEncrypted(byte[] pdf)
    {
        if (pdf == null || pdf.Length == 0) return false;
        var text = Latin1.GetString(pdf);

        int idx = 0;
        while ((idx = text.IndexOf("/Encrypt", idx, StringComparison.Ordinal)) >= 0)
        {
            int p = idx + "/Encrypt".Length;
            // Skip whitespace after the key.
            while (p < text.Length && (text[p] == ' ' || text[p] == '\r' || text[p] == '\n' || text[p] == '\t'))
                p++;
            if (p < text.Length)
            {
                char c = text[p];
                // Direct dict "<<" or indirect reference "N 0 R" (starts with a digit).
                if ((c == '<' && p + 1 < text.Length && text[p + 1] == '<') ||
                    (c >= '0' && c <= '9'))
                    return true;
            }
            idx = p;
        }
        return false;
    }
}
