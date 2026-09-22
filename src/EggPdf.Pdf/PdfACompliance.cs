using System;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// PDF/A metadata generation: XMP conformance identification and the OutputIntent
/// dictionary referencing an embedded ICC profile (see <see cref="IccSrgbProfile"/>).
/// </summary>
public static class PdfACompliance
{
    /// <summary>
    /// Generate XMP metadata for PDF/A conformance.
    /// This XML must be embedded as a metadata stream in the PDF catalog.
    /// </summary>
    public static string GenerateXmpMetadata(string? title, string? author, PdfAConformance conformance)
    {
        string part = conformance.Part();
        string level = conformance.Level();

        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var xmp = new StringBuilder();
        xmp.AppendLine("<?xpacket begin='" + (char)0xFEFF + "' id='W5M0MpCehiHzreSzNTczkc9d'?>");
        xmp.AppendLine("<x:xmpmeta xmlns:x='adobe:ns:meta/'>");
        xmp.AppendLine("<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>");
        xmp.AppendLine("<rdf:Description rdf:about=''");
        xmp.AppendLine("  xmlns:dc='http://purl.org/dc/elements/1.1/'");
        xmp.AppendLine("  xmlns:xmp='http://ns.adobe.com/xap/1.0/'");
        xmp.AppendLine("  xmlns:pdfaid='http://www.aiim.org/pdfa/ns/id/'");
        xmp.AppendLine("  xmlns:pdf='http://ns.adobe.com/pdf/1.3/'>");

        // PDF/A identification
        xmp.AppendLine($"  <pdfaid:part>{part}</pdfaid:part>");
        xmp.AppendLine($"  <pdfaid:conformance>{level}</pdfaid:conformance>");

        // Dublin Core metadata
        if (!string.IsNullOrEmpty(title))
        {
            xmp.AppendLine("  <dc:title><rdf:Alt><rdf:li xml:lang='x-default'>");
            xmp.AppendLine($"    {EscapeXml(title!)}");
            xmp.AppendLine("  </rdf:li></rdf:Alt></dc:title>");
        }
        if (!string.IsNullOrEmpty(author))
        {
            xmp.AppendLine("  <dc:creator><rdf:Seq><rdf:li>");
            xmp.AppendLine($"    {EscapeXml(author!)}");
            xmp.AppendLine("  </rdf:li></rdf:Seq></dc:creator>");
        }

        // XMP basic
        xmp.AppendLine($"  <xmp:CreateDate>{now}</xmp:CreateDate>");
        xmp.AppendLine($"  <xmp:ModifyDate>{now}</xmp:ModifyDate>");
        xmp.AppendLine("  <xmp:CreatorTool>EggPdf</xmp:CreatorTool>");

        // PDF info
        xmp.AppendLine("  <pdf:Producer>EggPdf</pdf:Producer>");

        xmp.AppendLine("</rdf:Description>");
        xmp.AppendLine("</rdf:RDF>");
        xmp.AppendLine("</x:xmpmeta>");

        // Padding (XMP spec recommends 2KB padding for in-place updates)
        for (int i = 0; i < 20; i++)
            xmp.AppendLine(new string(' ', 100));

        xmp.AppendLine("<?xpacket end='w'?>");
        return xmp.ToString();
    }

    /// <summary>
    /// The /OutputIntent dictionary (as inline PDF syntax) PDF/A requires, referencing an
    /// embedded ICC profile stream by object number. Returns the objects needed for the
    /// catalog's /OutputIntents array.
    /// </summary>
    public static string GenerateOutputIntentDict(int iccProfileObjRef)
    {
        var sb = new StringBuilder();
        sb.Append("<< /Type /OutputIntent");
        sb.Append(" /S /GTS_PDFA1");
        sb.Append(" /OutputConditionIdentifier (sRGB)");
        sb.Append(" /RegistryName (http://www.color.org)");
        sb.Append(" /Info (sRGB IEC61966-2.1)");
        sb.Append($" /DestOutputProfile {iccProfileObjRef} 0 R");
        sb.Append(" >>");
        return sb.ToString();
    }

    /// <summary>
    /// Escape XML special characters and drop characters XML 1.0 disallows outright (C0 controls
    /// other than tab/LF/CR) -- title/author come from arbitrary HTML and must not be able to
    /// produce a non-well-formed XMP packet.
    /// </summary>
    private static string EscapeXml(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') continue;
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
