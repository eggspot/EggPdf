using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Generates the UN/CEFACT Cross Industry Invoice (CII) XML for Factur-X/ZUGFeRD from a
/// <see cref="FacturXInvoice"/>: the MINIMUM profile when it has no line items, an EN 16931
/// (Comfort)-conformant document when it does. Structure verified against the reference samples
/// published at github.com/invoice-x/factur-x-ng (flavors/factur-x/xml/samples/minimum.xml and
/// en16931.xml).
/// </summary>
public static class FacturXCiiWriter
{
    /// <summary>The MINIMUM profile's guideline identifier.</summary>
    public const string MinimumGuidelineId = "urn:factur-x.eu:1p0:minimum";

    /// <summary>The EN 16931 (Comfort) profile's guideline identifier, used whenever the invoice has line items.</summary>
    public const string En16931GuidelineId = "urn:cen.eu:en16931:2017";

    /// <summary>True if this invoice's data produces an EN 16931-conformant document rather than MINIMUM.</summary>
    public static bool IsEn16931(FacturXInvoice invoice) => invoice.LineItems.Count > 0;

    public static string Generate(FacturXInvoice invoice)
    {
        bool en16931 = IsEn16931(invoice);

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
        xml.Append("      <ram:ID>").Append(en16931 ? En16931GuidelineId : MinimumGuidelineId).Append("</ram:ID>\n");
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

        foreach (var line in invoice.LineItems)
            AppendLineItem(xml, invoice.CurrencyCode, line);

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

        if (en16931)
        {
            var totals = ComputeTotals(invoice);
            foreach (var group in totals.TaxBreakdown)
            {
                xml.Append("      <ram:ApplicableTradeTax>\n");
                xml.Append("        <ram:CalculatedAmount currencyID=\"").Append(invoice.CurrencyCode).Append("\">").Append(Amount(group.CalculatedAmount)).Append("</ram:CalculatedAmount>\n");
                xml.Append("        <ram:TypeCode>VAT</ram:TypeCode>\n");
                xml.Append("        <ram:BasisAmount currencyID=\"").Append(invoice.CurrencyCode).Append("\">").Append(Amount(group.BasisAmount)).Append("</ram:BasisAmount>\n");
                xml.Append("        <ram:CategoryCode>").Append(EscapeXml(group.CategoryCode)).Append("</ram:CategoryCode>\n");
                xml.Append("        <ram:RateApplicablePercent>").Append(Amount(group.RatePercent)).Append("</ram:RateApplicablePercent>\n");
                xml.Append("      </ram:ApplicableTradeTax>\n");
            }
            xml.Append("      <ram:SpecifiedTradeSettlementHeaderMonetarySummation>\n");
            xml.Append("        <ram:LineTotalAmount currencyID=\"").Append(invoice.CurrencyCode).Append("\">").Append(Amount(totals.LineTotal)).Append("</ram:LineTotalAmount>\n");
            xml.Append("        <ram:TaxBasisTotalAmount currencyID=\"").Append(invoice.CurrencyCode).Append("\">").Append(Amount(totals.TaxBasisTotal)).Append("</ram:TaxBasisTotalAmount>\n");
            xml.Append("        <ram:TaxTotalAmount currencyID=\"").Append(invoice.CurrencyCode).Append("\">").Append(Amount(totals.TaxTotal)).Append("</ram:TaxTotalAmount>\n");
            xml.Append("        <ram:GrandTotalAmount currencyID=\"").Append(invoice.CurrencyCode).Append("\">").Append(Amount(totals.GrandTotal)).Append("</ram:GrandTotalAmount>\n");
            xml.Append("        <ram:DuePayableAmount currencyID=\"").Append(invoice.CurrencyCode).Append("\">").Append(Amount(totals.GrandTotal)).Append("</ram:DuePayableAmount>\n");
            xml.Append("      </ram:SpecifiedTradeSettlementHeaderMonetarySummation>\n");
        }
        else
        {
            xml.Append("      <ram:SpecifiedTradeSettlementHeaderMonetarySummation>\n");
            xml.Append("        <ram:TaxBasisTotalAmount currencyID=\"").Append(invoice.CurrencyCode).Append("\">").Append(Amount(invoice.TaxBasisTotal)).Append("</ram:TaxBasisTotalAmount>\n");
            xml.Append("        <ram:TaxTotalAmount currencyID=\"").Append(invoice.CurrencyCode).Append("\">").Append(Amount(invoice.TaxTotal)).Append("</ram:TaxTotalAmount>\n");
            xml.Append("        <ram:GrandTotalAmount currencyID=\"").Append(invoice.CurrencyCode).Append("\">").Append(Amount(invoice.GrandTotal)).Append("</ram:GrandTotalAmount>\n");
            xml.Append("        <ram:DuePayableAmount currencyID=\"").Append(invoice.CurrencyCode).Append("\">").Append(Amount(invoice.DuePayableAmount)).Append("</ram:DuePayableAmount>\n");
            xml.Append("      </ram:SpecifiedTradeSettlementHeaderMonetarySummation>\n");
        }

        xml.Append("    </ram:ApplicableHeaderTradeSettlement>\n");
        xml.Append("  </rsm:SupplyChainTradeTransaction>\n");
        xml.Append("</rsm:CrossIndustryInvoice>\n");

        return xml.ToString();
    }

    private static void AppendLineItem(StringBuilder xml, string currencyCode, FacturXLineItem line)
    {
        xml.Append("    <ram:IncludedSupplyChainTradeLineItem>\n");
        xml.Append("      <ram:AssociatedDocumentLineDocument>\n");
        xml.Append("        <ram:LineID>").Append(EscapeXml(line.LineId)).Append("</ram:LineID>\n");
        xml.Append("      </ram:AssociatedDocumentLineDocument>\n");
        xml.Append("      <ram:SpecifiedTradeProduct>\n");
        xml.Append("        <ram:Name>").Append(EscapeXml(line.ItemName)).Append("</ram:Name>\n");
        xml.Append("      </ram:SpecifiedTradeProduct>\n");
        xml.Append("      <ram:SpecifiedLineTradeAgreement>\n");
        xml.Append("        <ram:NetPriceProductTradePrice>\n");
        xml.Append("          <ram:ChargeAmount currencyID=\"").Append(currencyCode).Append("\">").Append(Amount(line.NetUnitPrice)).Append("</ram:ChargeAmount>\n");
        xml.Append("        </ram:NetPriceProductTradePrice>\n");
        xml.Append("      </ram:SpecifiedLineTradeAgreement>\n");
        xml.Append("      <ram:SpecifiedLineTradeDelivery>\n");
        xml.Append("        <ram:BilledQuantity unitCode=\"").Append(EscapeXml(line.UnitCode)).Append("\">").Append(Quantity(line.BilledQuantity)).Append("</ram:BilledQuantity>\n");
        xml.Append("      </ram:SpecifiedLineTradeDelivery>\n");
        xml.Append("      <ram:SpecifiedLineTradeSettlement>\n");
        xml.Append("        <ram:ApplicableTradeTax>\n");
        xml.Append("          <ram:TypeCode>VAT</ram:TypeCode>\n");
        xml.Append("          <ram:CategoryCode>").Append(EscapeXml(line.VatCategoryCode)).Append("</ram:CategoryCode>\n");
        xml.Append("          <ram:RateApplicablePercent>").Append(Amount(line.VatRatePercent)).Append("</ram:RateApplicablePercent>\n");
        xml.Append("        </ram:ApplicableTradeTax>\n");
        xml.Append("        <ram:SpecifiedTradeSettlementLineMonetarySummation>\n");
        xml.Append("          <ram:LineTotalAmount currencyID=\"").Append(currencyCode).Append("\">").Append(Amount(line.LineTotalAmount)).Append("</ram:LineTotalAmount>\n");
        xml.Append("        </ram:SpecifiedTradeSettlementLineMonetarySummation>\n");
        xml.Append("      </ram:SpecifiedLineTradeSettlement>\n");
        xml.Append("    </ram:IncludedSupplyChainTradeLineItem>\n");
    }

    internal readonly struct TaxBreakdownGroup
    {
        public readonly string CategoryCode;
        public readonly decimal RatePercent;
        public readonly decimal BasisAmount;
        public readonly decimal CalculatedAmount;

        public TaxBreakdownGroup(string categoryCode, decimal ratePercent, decimal basisAmount, decimal calculatedAmount)
        {
            CategoryCode = categoryCode;
            RatePercent = ratePercent;
            BasisAmount = basisAmount;
            CalculatedAmount = calculatedAmount;
        }
    }

    internal readonly struct ComputedTotals
    {
        public readonly decimal LineTotal;
        public readonly decimal TaxBasisTotal;
        public readonly decimal TaxTotal;
        public readonly decimal GrandTotal;
        public readonly List<TaxBreakdownGroup> TaxBreakdown;

        public ComputedTotals(decimal lineTotal, decimal taxBasisTotal, decimal taxTotal, decimal grandTotal, List<TaxBreakdownGroup> taxBreakdown)
        {
            LineTotal = lineTotal;
            TaxBasisTotal = taxBasisTotal;
            TaxTotal = taxTotal;
            GrandTotal = grandTotal;
            TaxBreakdown = taxBreakdown;
        }
    }

    /// <summary>
    /// Compute the header-level monetary summation and per-(VAT category, rate) tax breakdown
    /// from an invoice's line items. Internal + exposed only via <see cref="Generate"/> so the
    /// header always reflects the lines rather than trusting separately-supplied totals that
    /// could silently disagree with them.
    /// </summary>
    internal static ComputedTotals ComputeTotals(FacturXInvoice invoice)
    {
        decimal lineTotal = 0;
        var groups = new List<(string category, decimal rate, decimal basis)>();

        foreach (var line in invoice.LineItems)
        {
            lineTotal += line.LineTotalAmount;

            int existingIndex = -1;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].category == line.VatCategoryCode && groups[i].rate == line.VatRatePercent)
                {
                    existingIndex = i;
                    break;
                }
            }
            if (existingIndex >= 0)
                groups[existingIndex] = (groups[existingIndex].category, groups[existingIndex].rate, groups[existingIndex].basis + line.LineTotalAmount);
            else
                groups.Add((line.VatCategoryCode, line.VatRatePercent, line.LineTotalAmount));
        }

        var breakdown = new List<TaxBreakdownGroup>(groups.Count);
        decimal taxTotal = 0;
        foreach (var group in groups)
        {
            decimal calculated = Math.Round(group.basis * group.rate / 100m, 2, MidpointRounding.AwayFromZero);
            taxTotal += calculated;
            breakdown.Add(new TaxBreakdownGroup(group.category, group.rate, group.basis, calculated));
        }

        decimal taxBasisTotal = lineTotal;
        decimal grandTotal = taxBasisTotal + taxTotal;

        return new ComputedTotals(lineTotal, taxBasisTotal, taxTotal, grandTotal, breakdown);
    }

    private static string Amount(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string Quantity(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string EscapeXml(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
