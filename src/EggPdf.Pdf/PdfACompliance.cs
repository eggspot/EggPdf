using System;
using System.Globalization;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// PDF/A-1b compliance support.
/// Generates required metadata, ICC color profile reference, and conformance markers
/// for archival PDF output per ISO 19005-1:2005.
/// </summary>
public static class PdfACompliance
{
    /// <summary>PDF/A conformance levels.</summary>
    public enum ConformanceLevel
    {
        /// <summary>PDF/A-1b: basic conformance (visual reproduction).</summary>
        PdfA1b,
        /// <summary>PDF/A-2b: based on PDF 1.7.</summary>
        PdfA2b,
        /// <summary>PDF/A-3b: allows file attachments.</summary>
        PdfA3b,
    }

    /// <summary>
    /// Generate XMP metadata for PDF/A conformance.
    /// This XML must be embedded as a metadata stream in the PDF catalog.
    /// </summary>
    public static string GenerateXmpMetadata(string? title, string? author, ConformanceLevel level = ConformanceLevel.PdfA1b)
    {
        string part, conformance;
        switch (level)
        {
            case ConformanceLevel.PdfA2b: part = "2"; conformance = "B"; break;
            case ConformanceLevel.PdfA3b: part = "3"; conformance = "B"; break;
            default: part = "1"; conformance = "B"; break;
        }

        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var xmp = new StringBuilder();
        xmp.AppendLine("<?xpacket begin='\uFEFF' id='W5M0MpCehiHzreSzNTczkc9d'?>");
        xmp.AppendLine("<x:xmpmeta xmlns:x='adobe:ns:meta/'>");
        xmp.AppendLine("<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>");
        xmp.AppendLine("<rdf:Description rdf:about=''");
        xmp.AppendLine("  xmlns:dc='http://purl.org/dc/elements/1.1/'");
        xmp.AppendLine("  xmlns:xmp='http://ns.adobe.com/xap/1.0/'");
        xmp.AppendLine("  xmlns:pdfaid='http://www.aiim.org/pdfa/ns/id/'");
        xmp.AppendLine("  xmlns:pdf='http://ns.adobe.com/pdf/1.3/'>");

        // PDF/A identification
        xmp.AppendLine($"  <pdfaid:part>{part}</pdfaid:part>");
        xmp.AppendLine($"  <pdfaid:conformance>{conformance}</pdfaid:conformance>");

        // Dublin Core metadata
        if (!string.IsNullOrEmpty(title))
        {
            xmp.AppendLine("  <dc:title><rdf:Alt><rdf:li xml:lang='x-default'>");
            xmp.AppendLine($"    {EscapeXml(title)}");
            xmp.AppendLine("  </rdf:li></rdf:Alt></dc:title>");
        }
        if (!string.IsNullOrEmpty(author))
        {
            xmp.AppendLine("  <dc:creator><rdf:Seq><rdf:li>");
            xmp.AppendLine($"    {EscapeXml(author)}");
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
    /// Generate a minimal sRGB ICC color profile reference for PDF/A.
    /// PDF/A requires an output intent with an ICC profile for color reproduction.
    /// Returns the PDF objects needed for the OutputIntents array.
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
    /// Generate a minimal sRGB ICC profile (header only).
    /// A full ICC profile is ~3KB; this is a minimal valid header for PDF/A compliance checking.
    /// For production use, embed the full sRGB IEC61966-2.1 profile.
    /// </summary>
    public static byte[] GenerateMinimalSrgbProfile()
    {
        // Minimal ICC profile header (128 bytes)
        var profile = new byte[128];

        // Profile size (128 bytes for header-only)
        WriteU32BE(profile, 0, 128);

        // Preferred CMM: 'ADBE' (Adobe)
        profile[4] = 0x41; profile[5] = 0x44; profile[6] = 0x42; profile[7] = 0x45;

        // Profile version: 2.1.0
        profile[8] = 2; profile[9] = 0x10;

        // Device class: 'mntr' (monitor)
        profile[12] = 0x6D; profile[13] = 0x6E; profile[14] = 0x74; profile[15] = 0x72;

        // Color space: 'RGB '
        profile[16] = 0x52; profile[17] = 0x47; profile[18] = 0x42; profile[19] = 0x20;

        // Connection space: 'XYZ '
        profile[20] = 0x58; profile[21] = 0x59; profile[22] = 0x5A; profile[23] = 0x20;

        // Date/time: 2024-01-01 00:00:00
        WriteU16BE(profile, 24, 2024); // year
        WriteU16BE(profile, 26, 1);    // month
        WriteU16BE(profile, 28, 1);    // day

        // Signature: 'acsp'
        profile[36] = 0x61; profile[37] = 0x63; profile[38] = 0x73; profile[39] = 0x70;

        // Primary platform: 'MSFT'
        profile[40] = 0x4D; profile[41] = 0x53; profile[42] = 0x46; profile[43] = 0x54;

        // Rendering intent: perceptual (0)
        // Illuminant: D50 (standard for ICC)
        WriteU32BE(profile, 68, 0x0000F6D6); // X = 0.9642
        WriteU32BE(profile, 72, 0x00010000); // Y = 1.0000
        WriteU32BE(profile, 76, 0x0000D32D); // Z = 0.8249

        return profile;
    }

    private static void WriteU32BE(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)((value >> 16) & 0xFF);
        buf[offset + 2] = (byte)((value >> 8) & 0xFF);
        buf[offset + 3] = (byte)(value & 0xFF);
    }

    private static void WriteU16BE(byte[] buf, int offset, ushort value)
    {
        buf[offset] = (byte)(value >> 8);
        buf[offset + 1] = (byte)(value & 0xFF);
    }

    private static string EscapeXml(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
