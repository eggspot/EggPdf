using System;
using System.Globalization;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Generates the UN/CEFACT Cross Industry Invoice (CII) XML for the Factur-X/ZUGFeRD MINIMUM
/// profile from a <see cref="FacturXInvoice"/>. Structure verified against the reference MINIMUM
/// sample published at github.com/invoice-x/factur-x-ng (flavors/factur-x/xml/samples/minimum.xml).
/// </summary>
public static class FacturXCiiWriter
{
    /// <summary>The MINIMUM profile's guideline identifier, declared in every generated document.</summary>
    public const string MinimumGuidelineId = "urn:factur-x.eu:1p0:minimum";

    public static string Generate(FacturXInvoice invoice)
    {
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        xml.Append("<rsm:CrossIndustryInvoice")
           .Append(" xmlns:qdt=\"urn:un:unece:uncefact:data:standard:QualifiedDataType:100\"")
           .Append(" xmlns:ram=\"urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100\"")
           .Append(" xmlns:rsm=\"urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100\"")
           .Append(" xmlns:udt=\"urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100\"")
           .Append(" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\n");

        xml.Append("  <rsm:ExchangedDocumentContext>\n");
        xml.Append("    <ram:GuidelineSpecifiedDocumentContextParameter>\n");
        xml.Append("      <ram:ID>").Append(MinimumGuidelineId).Append("</ram:ID>\n");
        xml.Append("    </ram:GuidelineSpecifiedDocumentContextParameter>\n");
        xml.Append("  </rsm:ExchangedDocumentContext>\n");

        xml.Append("  <rsm:ExchangedDocument>\n");
        xml.Append("    <ram:ID>").Append(EscapeXml(invoice.InvoiceNumber)).Append("</ram:ID>\n");
        xml.Append("    <ram:TypeCode>380</ram:TypeCode>\n"); // 380 = commercial invoice (UNTDID 1001)
        xml.Append("    <ram:IssueDateTime>\n");
        xml.Append("      <udt:DateTimeString format=\"102\">").Append(invoice.IssueDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append("</udt:DateTimeString>\n");
        xml.Append("    </ram:IssueDateTime>\n");
        xml.Append("  </rsm:ExchangedDocument>\n");

        xml.Append("  <rsm:SupplyChainTradeTransaction>\n");
        xml.Append("    <ram:ApplicableHeaderTradeAgreement>\n");
        if (!string.IsNullOrEmpty(invoice.BuyerReference))
            xml.Append("      <ram:BuyerReference>").Append(EscapeXml(invoice.BuyerReference!)).Append("</ram:BuyerReference>\n");
        xml.Append("      <ram:SellerTradeParty>\n");
        xml.Append("        <ram:Name>").Append(EscapeXml(invoice.SellerName)).Append("</ram:Name>\n");
        if (!string.IsNullOrEmpty(invoice.SellerCountryCode))
        {
            xml.Append("        <ram:PostalTradeAddress>\n");
            xml.Append("          <ram:CountryID>").Append(EscapeXml(invoice.SellerCountryCode!)).Append("</ram:CountryID>\n");
            xml.Append("        </ram:PostalTradeAddress>\n");
        }
        if (!string.IsNullOrEmpty(invoice.SellerVatId))
        {
            xml.Append("        <ram:SpecifiedTaxRegistration>\n");
            xml.Append("          <ram:ID schemeID=\"VA\">").Append(EscapeXml(invoice.SellerVatId!)).Append("</ram:ID>\n");
            xml.Append("        </ram:SpecifiedTaxRegistration>\n");
        }
        xml.Append("      </ram:SellerTradeParty>\n");
        xml.Append("      <ram:BuyerTradeParty>\n");
        xml.Append("        <ram:Name>").Append(EscapeXml(invoice.BuyerName)).Append("</ram:Name>\n");
        xml.Append("      </ram:BuyerTradeParty>\n");
        xml.Append("    </ram:ApplicableHeaderTradeAgreement>\n");
        xml.Append("    <ram:ApplicableHeaderTradeDelivery/>\n");
        xml.Append("    <ram:ApplicableHeaderTradeSettlement>\n");
        xml.Append("      <ram:InvoiceCurrencyCode>").Append(EscapeXml(invoice.CurrencyCode)).Append("</ram:InvoiceCurrencyCode>\n");
        xml.Append("      <ram:SpecifiedTradeSettlementHeaderMonetarySummation>\n");
        xml.Append("        <ram:TaxBasisTotalAmount currencyID=\"").Append(invoice.CurrencyCode).Append("\">").Append(Amount(invoice.TaxBasisTotal)).Append("</ram:TaxBasisTotalAmount>\n");
        xml.Append("        <ram:TaxTotalAmount currencyID=\"").Append(invoice.CurrencyCode).Append("\">").Append(Amount(invoice.TaxTotal)).Append("</ram:TaxTotalAmount>\n");
        xml.Append("        <ram:GrandTotalAmount currencyID=\"").Append(invoice.CurrencyCode).Append("\">").Append(Amount(invoice.GrandTotal)).Append("</ram:GrandTotalAmount>\n");
        xml.Append("        <ram:DuePayableAmount currencyID=\"").Append(invoice.CurrencyCode).Append("\">").Append(Amount(invoice.DuePayableAmount)).Append("</ram:DuePayableAmount>\n");
        xml.Append("      </ram:SpecifiedTradeSettlementHeaderMonetarySummation>\n");
        xml.Append("    </ram:ApplicableHeaderTradeSettlement>\n");
        xml.Append("  </rsm:SupplyChainTradeTransaction>\n");
        xml.Append("</rsm:CrossIndustryInvoice>\n");

        return xml.ToString();
    }

    private static string Amount(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string EscapeXml(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
