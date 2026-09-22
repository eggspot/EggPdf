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
    /// Generate XMP metadata for PDF/A and/or PDF/UA-1 conformance. This XML must be embedded as
    /// a metadata stream in the PDF catalog. <paramref name="conformance"/> is null for a
    /// PDF/UA-1-only document (no PDF/A claim). When <paramref name="facturXFileName"/> is set,
    /// the Factur-X PDF/A extension schema is declared and populated (see
    /// <see cref="AppendFacturXExtensionSchema"/>) -- required so PDF/A validators and Factur-X
    /// readers recognize the embedded invoice XML attachment. When
    /// <paramref name="includePdfUA"/> is set, <c>pdfuaid:part</c> is declared and
    /// <paramref name="title"/> is required (PDF/UA-1 rule 7.1-8/7.1-9) -- an empty title is
    /// replaced with a placeholder rather than silently omitted.
    /// </summary>
    public static string GenerateXmpMetadata(string? title, string? author, PdfAConformance? conformance,
        string? facturXFileName = null, bool includePdfUA = false)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var xmp = new StringBuilder();
        xmp.AppendLine("<?xpacket begin='" + (char)0xFEFF + "' id='W5M0MpCehiHzreSzNTczkc9d'?>");
        xmp.AppendLine("<x:xmpmeta xmlns:x='adobe:ns:meta/'>");
        xmp.AppendLine("<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>");
        xmp.AppendLine("<rdf:Description rdf:about=''");
        xmp.AppendLine("  xmlns:dc='http://purl.org/dc/elements/1.1/'");
        xmp.AppendLine("  xmlns:xmp='http://ns.adobe.com/xap/1.0/'");
        if (conformance != null)
            xmp.AppendLine("  xmlns:pdfaid='http://www.aiim.org/pdfa/ns/id/'");
        if (includePdfUA)
            xmp.AppendLine("  xmlns:pdfuaid='http://www.aiim.org/pdfua/ns/id/'");
        xmp.AppendLine("  xmlns:pdf='http://ns.adobe.com/pdf/1.3/'>");

        // PDF/A identification
        if (conformance != null)
        {
            xmp.AppendLine($"  <pdfaid:part>{conformance.Value.Part()}</pdfaid:part>");
            xmp.AppendLine($"  <pdfaid:conformance>{conformance.Value.Level()}</pdfaid:conformance>");
        }

        // PDF/UA identification (part only -- PDF/UA-1 has no conformance letter, unlike PDF/A)
        if (includePdfUA)
            xmp.AppendLine("  <pdfuaid:part>1</pdfuaid:part>");

        // Dublin Core metadata. PDF/UA-1 requires a title (rule 7.1-8/7.1-9); default rather
        // than silently omit it when the caller didn't set one.
        var effectiveTitle = includePdfUA && string.IsNullOrEmpty(title) ? "Untitled Document" : title;
        if (!string.IsNullOrEmpty(effectiveTitle))
        {
            xmp.AppendLine("  <dc:title><rdf:Alt><rdf:li xml:lang='x-default'>");
            xmp.AppendLine($"    {EscapeXml(effectiveTitle!)}");
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

        if (!string.IsNullOrEmpty(facturXFileName))
            AppendFacturXExtensionSchema(xmp, facturXFileName!);

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
    /// Declare and populate the Factur-X PDF/A extension schema (namespace prefix <c>fx</c>,
    /// <c>urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#</c>) -- the PDF/A Extension
    /// Schema mechanism a validator uses to know what <c>fx:*</c> properties mean, plus the
    /// actual property values identifying the embedded invoice attachment. Verified against the
    /// reference schema published at github.com/atgp/factur-x (xmp/Factur-X_extension_schema.xmp).
    /// </summary>
    private static void AppendFacturXExtensionSchema(StringBuilder xmp, string facturXFileName)
    {
        xmp.AppendLine("<rdf:Description rdf:about=''");
        xmp.AppendLine("  xmlns:pdfaExtension='http://www.aiim.org/pdfa/ns/extension/'");
        xmp.AppendLine("  xmlns:pdfaSchema='http://www.aiim.org/pdfa/ns/schema#'");
        xmp.AppendLine("  xmlns:pdfaProperty='http://www.aiim.org/pdfa/ns/property#'>");
        xmp.AppendLine("  <pdfaExtension:schemas>");
        xmp.AppendLine("    <rdf:Bag>");
        xmp.AppendLine("      <rdf:li rdf:parseType='Resource'>");
        xmp.AppendLine("        <pdfaSchema:schema>Factur-X PDFA Extension Schema</pdfaSchema:schema>");
        xmp.AppendLine("        <pdfaSchema:namespaceURI>urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#</pdfaSchema:namespaceURI>");
        xmp.AppendLine("        <pdfaSchema:prefix>fx</pdfaSchema:prefix>");
        xmp.AppendLine("        <pdfaSchema:property>");
        xmp.AppendLine("          <rdf:Seq>");
        AppendFacturXPropertyDef(xmp, "DocumentFileName", "name of the embedded XML invoice file");
        AppendFacturXPropertyDef(xmp, "DocumentType", "INVOICE");
        AppendFacturXPropertyDef(xmp, "Version", "The actual version of the Factur-X XML schema");
        AppendFacturXPropertyDef(xmp, "ConformanceLevel", "The conformance level of the embedded Factur-X data");
        xmp.AppendLine("          </rdf:Seq>");
        xmp.AppendLine("        </pdfaSchema:property>");
        xmp.AppendLine("      </rdf:li>");
        xmp.AppendLine("    </rdf:Bag>");
        xmp.AppendLine("  </pdfaExtension:schemas>");
        xmp.AppendLine("</rdf:Description>");

        xmp.AppendLine("<rdf:Description rdf:about=''");
        xmp.AppendLine("  xmlns:fx='urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#'>");
        xmp.AppendLine("  <fx:DocumentType>INVOICE</fx:DocumentType>");
        xmp.AppendLine($"  <fx:DocumentFileName>{EscapeXml(facturXFileName)}</fx:DocumentFileName>");
        xmp.AppendLine("  <fx:Version>1.0</fx:Version>");
        xmp.AppendLine("  <fx:ConformanceLevel>MINIMUM</fx:ConformanceLevel>");
        xmp.AppendLine("</rdf:Description>");
    }

    private static void AppendFacturXPropertyDef(StringBuilder xmp, string name, string description)
    {
        xmp.AppendLine("            <rdf:li rdf:parseType='Resource'>");
        xmp.AppendLine($"              <pdfaProperty:name>{name}</pdfaProperty:name>");
        xmp.AppendLine("              <pdfaProperty:valueType>Text</pdfaProperty:valueType>");
        xmp.AppendLine("              <pdfaProperty:category>external</pdfaProperty:category>");
        xmp.AppendLine($"              <pdfaProperty:description>{EscapeXml(description)}</pdfaProperty:description>");
        xmp.AppendLine("            </rdf:li>");
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
