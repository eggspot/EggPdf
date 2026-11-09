using System;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// PDF/A metadata generation: XMP conformance identification and the OutputIntent
/// dictionary referencing an embedded ICC profile (see <see cref="IccSrgbProfile"/>).
/// </summary>
public static class PdfACompliance
{
    private const int XmpPaddingLines = 20;
    private const int XmpPaddingLineLength = 100;

    /// <summary>
    /// Generate XMP metadata for PDF/A and/or PDF/UA-1 conformance. This XML must be embedded as
    /// a metadata stream in the PDF catalog. <paramref name="conformance"/> is null for a
    /// PDF/UA-1-only document (no PDF/A claim). When <paramref name="facturXFileName"/> is set,
    /// the Factur-X PDF/A extension schema is declared and populated (see
    /// <see cref="AppendFacturXExtensionSchema"/>) -- required so PDF/A validators and Factur-X
    /// readers recognize the embedded invoice XML attachment. When
    /// <paramref name="includePdfUA"/> is set, <c>pdfuaid:part</c> is declared and
    /// <paramref name="title"/> is required (PDF/UA-1 rule 7.1-8/7.1-9) -- an empty title is
    /// replaced with a placeholder rather than silently omitted. <paramref name="isUa2"/> selects
    /// PDF/UA-2 (ISO 14289-2:2024) instead of the default UA-1: <c>pdfuaid:part</c> becomes 2 and
    /// a <c>pdfuaid:rev</c> of 2024 is added.
    /// </summary>
    public static string GenerateXmpMetadata(string? title, string? author, PdfAConformance? conformance,
        string? facturXFileName = null, bool includePdfUA = false, string facturXConformanceLevel = "MINIMUM",
        bool isUa2 = false)
    {
        var now = PdfDates.XmpDate(DateTime.UtcNow);

        var xmp = new StringBuilder();
        xmp.AppendOpLine("<?xpacket begin='" + (char)0xFEFF + "' id='W5M0MpCehiHzreSzNTczkc9d'?>");
        xmp.AppendOpLine("<x:xmpmeta xmlns:x='adobe:ns:meta/'>");
        xmp.AppendOpLine("<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>");
        xmp.AppendOpLine("<rdf:Description rdf:about=''");
        xmp.AppendOpLine("  xmlns:dc='http://purl.org/dc/elements/1.1/'");
        xmp.AppendOpLine("  xmlns:xmp='http://ns.adobe.com/xap/1.0/'");
        if (conformance != null)
            xmp.AppendOpLine("  xmlns:pdfaid='http://www.aiim.org/pdfa/ns/id/'");
        if (includePdfUA)
            xmp.AppendOpLine("  xmlns:pdfuaid='http://www.aiim.org/pdfua/ns/id/'");
        xmp.AppendOpLine("  xmlns:pdf='http://ns.adobe.com/pdf/1.3/'>");

        if (conformance != null)
        {
            xmp.AppendOpLine($"  <pdfaid:part>{conformance.Value.Part()}</pdfaid:part>");
            xmp.AppendOpLine($"  <pdfaid:conformance>{conformance.Value.Level()}</pdfaid:conformance>");
        }

        if (includePdfUA)
        {
            xmp.AppendOpLine($"  <pdfuaid:part>{(isUa2 ? 2 : 1)}</pdfuaid:part>");
            if (isUa2)
                xmp.AppendOpLine("  <pdfuaid:rev>2024</pdfuaid:rev>");
        }

        var effectiveTitle = includePdfUA && string.IsNullOrEmpty(title) ? "Untitled Document" : title;
        if (!string.IsNullOrEmpty(effectiveTitle))
        {
            xmp.AppendOpLine("  <dc:title><rdf:Alt><rdf:li xml:lang='x-default'>");
            xmp.AppendOpLine($"    {EscapeXml(effectiveTitle!)}");
            xmp.AppendOpLine("  </rdf:li></rdf:Alt></dc:title>");
        }
        if (!string.IsNullOrEmpty(author))
        {
            xmp.AppendOpLine("  <dc:creator><rdf:Seq><rdf:li>");
            xmp.AppendOpLine($"    {EscapeXml(author!)}");
            xmp.AppendOpLine("  </rdf:li></rdf:Seq></dc:creator>");
        }

        xmp.AppendOpLine($"  <xmp:CreateDate>{now}</xmp:CreateDate>");
        xmp.AppendOpLine($"  <xmp:ModifyDate>{now}</xmp:ModifyDate>");
        xmp.AppendOpLine("  <xmp:CreatorTool>EggPdf</xmp:CreatorTool>");

        xmp.AppendOpLine("  <pdf:Producer>EggPdf</pdf:Producer>");

        xmp.AppendOpLine("</rdf:Description>");

        if (!string.IsNullOrEmpty(facturXFileName))
            AppendFacturXExtensionSchema(xmp, facturXFileName!, facturXConformanceLevel);

        xmp.AppendOpLine("</rdf:RDF>");
        xmp.AppendOpLine("</x:xmpmeta>");

        for (int i = 0; i < XmpPaddingLines; i++)
            xmp.AppendOpLine(new string(' ', XmpPaddingLineLength));

        xmp.AppendOpLine("<?xpacket end='w'?>");
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

    private static void AppendFacturXExtensionSchema(StringBuilder xmp, string facturXFileName, string conformanceLevel)
    {
        xmp.AppendOpLine("<rdf:Description rdf:about=''");
        xmp.AppendOpLine("  xmlns:pdfaExtension='http://www.aiim.org/pdfa/ns/extension/'");
        xmp.AppendOpLine("  xmlns:pdfaSchema='http://www.aiim.org/pdfa/ns/schema#'");
        xmp.AppendOpLine("  xmlns:pdfaProperty='http://www.aiim.org/pdfa/ns/property#'>");
        xmp.AppendOpLine("  <pdfaExtension:schemas>");
        xmp.AppendOpLine("    <rdf:Bag>");
        xmp.AppendOpLine("      <rdf:li rdf:parseType='Resource'>");
        xmp.AppendOpLine("        <pdfaSchema:schema>Factur-X PDFA Extension Schema</pdfaSchema:schema>");
        xmp.AppendOpLine("        <pdfaSchema:namespaceURI>urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#</pdfaSchema:namespaceURI>");
        xmp.AppendOpLine("        <pdfaSchema:prefix>fx</pdfaSchema:prefix>");
        xmp.AppendOpLine("        <pdfaSchema:property>");
        xmp.AppendOpLine("          <rdf:Seq>");
        AppendFacturXPropertyDef(xmp, "DocumentFileName", "name of the embedded XML invoice file");
        AppendFacturXPropertyDef(xmp, "DocumentType", "INVOICE");
        AppendFacturXPropertyDef(xmp, "Version", "The actual version of the Factur-X XML schema");
        AppendFacturXPropertyDef(xmp, "ConformanceLevel", "The conformance level of the embedded Factur-X data");
        xmp.AppendOpLine("          </rdf:Seq>");
        xmp.AppendOpLine("        </pdfaSchema:property>");
        xmp.AppendOpLine("      </rdf:li>");
        xmp.AppendOpLine("    </rdf:Bag>");
        xmp.AppendOpLine("  </pdfaExtension:schemas>");
        xmp.AppendOpLine("</rdf:Description>");

        xmp.AppendOpLine("<rdf:Description rdf:about=''");
        xmp.AppendOpLine("  xmlns:fx='urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#'>");
        xmp.AppendOpLine("  <fx:DocumentType>INVOICE</fx:DocumentType>");
        xmp.AppendOpLine($"  <fx:DocumentFileName>{EscapeXml(facturXFileName)}</fx:DocumentFileName>");
        xmp.AppendOpLine("  <fx:Version>1.0</fx:Version>");
        xmp.AppendOpLine($"  <fx:ConformanceLevel>{EscapeXml(conformanceLevel)}</fx:ConformanceLevel>");
        xmp.AppendOpLine("</rdf:Description>");
    }

    private static void AppendFacturXPropertyDef(StringBuilder xmp, string name, string description)
    {
        xmp.AppendOpLine("            <rdf:li rdf:parseType='Resource'>");
        xmp.AppendOpLine($"              <pdfaProperty:name>{name}</pdfaProperty:name>");
        xmp.AppendOpLine("              <pdfaProperty:valueType>Text</pdfaProperty:valueType>");
        xmp.AppendOpLine("              <pdfaProperty:category>external</pdfaProperty:category>");
        xmp.AppendOpLine($"              <pdfaProperty:description>{EscapeXml(description)}</pdfaProperty:description>");
        xmp.AppendOpLine("            </rdf:li>");
    }

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
