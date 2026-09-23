using System;
using System.Text;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class FacturXTests
{
    private static FacturXInvoice SampleInvoice() => new FacturXInvoice
    {
        InvoiceNumber = "2026-TEST-01",
        IssueDate = new DateTime(2026, 9, 22),
        CurrencyCode = "EUR",
        SellerName = "Eggspot SARL",
        SellerCountryCode = "FR",
        SellerVatId = "FR34999888779",
        BuyerName = "Acme Corp",
        BuyerReference = "PO-4711",
        TaxBasisTotal = 100.00m,
        TaxTotal = 20.00m,
        GrandTotal = 120.00m,
        DuePayableAmount = 120.00m,
    };

    [Fact]
    public void Generate_IncludesGuidelineIdAndCoreElements()
    {
        var xml = FacturXCiiWriter.Generate(SampleInvoice());

        xml.Should().Contain("<rsm:CrossIndustryInvoice");
        xml.Should().Contain($"<ram:ID>{FacturXCiiWriter.MinimumGuidelineId}</ram:ID>");
        xml.Should().Contain("<ram:ID>2026-TEST-01</ram:ID>");
        xml.Should().Contain("<ram:TypeCode>380</ram:TypeCode>");
        xml.Should().Contain("<udt:DateTimeString format=\"102\">20260922</udt:DateTimeString>");
        xml.Should().Contain("<ram:Name>Eggspot SARL</ram:Name>");
        xml.Should().Contain("<ram:Name>Acme Corp</ram:Name>");
        xml.Should().Contain("<ram:CountryID>FR</ram:CountryID>");
        xml.Should().Contain("<ram:ID schemeID=\"VA\">FR34999888779</ram:ID>");
        xml.Should().Contain("<ram:BuyerReference>PO-4711</ram:BuyerReference>");
        xml.Should().Contain("<ram:InvoiceCurrencyCode>EUR</ram:InvoiceCurrencyCode>");
        xml.Should().Contain("<ram:TaxBasisTotalAmount currencyID=\"EUR\">100.00</ram:TaxBasisTotalAmount>");
        xml.Should().Contain("<ram:TaxTotalAmount currencyID=\"EUR\">20.00</ram:TaxTotalAmount>");
        xml.Should().Contain("<ram:GrandTotalAmount currencyID=\"EUR\">120.00</ram:GrandTotalAmount>");
        xml.Should().Contain("<ram:DuePayableAmount currencyID=\"EUR\">120.00</ram:DuePayableAmount>");
        xml.Should().Contain("<ram:ApplicableHeaderTradeDelivery/>");
    }

    [Fact]
    public void Generate_OmitsOptionalElementsWhenNotSet()
    {
        var invoice = new FacturXInvoice
        {
            InvoiceNumber = "1",
            IssueDate = DateTime.UtcNow,
            SellerName = "Seller",
            BuyerName = "Buyer",
        };

        var xml = FacturXCiiWriter.Generate(invoice);

        xml.Should().NotContain("BuyerReference");
        xml.Should().NotContain("PostalTradeAddress");
        xml.Should().NotContain("SpecifiedTaxRegistration");
    }

    [Fact]
    public void Generate_EscapesXmlSpecialCharacters()
    {
        var invoice = SampleInvoice();
        invoice.SellerName = "Tom & Jerry <Ltd>";

        var xml = FacturXCiiWriter.Generate(invoice);

        xml.Should().Contain("Tom &amp; Jerry &lt;Ltd&gt;");
        xml.Should().NotContain("Tom & Jerry <Ltd>");
    }

    [Fact]
    public void PdfDocument_InvoiceWithoutPdfA3_Throws()
    {
        var doc = new PdfDocument
        {
            Conformance = PdfAConformance.PdfA2b,
            Invoice = SampleInvoice(),
        };
        doc.AddPage(595.28f, 841.89f);

        Action act = () => doc.ToByteArray();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PdfDocument_InvoiceWithoutConformance_Throws()
    {
        var doc = new PdfDocument { Invoice = SampleInvoice() };
        doc.AddPage(595.28f, 841.89f);

        Action act = () => doc.ToByteArray();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PdfDocument_InvoiceWithPdfA3b_EmbedsAttachmentAndXmpSchema()
    {
        var doc = new PdfDocument
        {
            Conformance = PdfAConformance.PdfA3b,
            Invoice = SampleInvoice(),
        };
        doc.AddPage(595.28f, 841.89f);

        var text = Encoding.Latin1.GetString(doc.ToByteArray());

        text.Should().Contain("/Type /Filespec");
        text.Should().Contain("/F (factur-x.xml)");
        text.Should().Contain("/AFRelationship /Data");
        text.Should().Contain("/EmbeddedFiles");
        text.Should().Contain("/AF [");
        text.Should().Contain("/Type /EmbeddedFile");
        text.Should().Contain("<rsm:CrossIndustryInvoice");

        text.Should().Contain("xmlns:fx='urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#'");
        text.Should().Contain("<fx:DocumentType>INVOICE</fx:DocumentType>");
        text.Should().Contain("<fx:DocumentFileName>factur-x.xml</fx:DocumentFileName>");
        text.Should().Contain("<fx:ConformanceLevel>MINIMUM</fx:ConformanceLevel>");
        text.Should().Contain("pdfaSchema:namespaceURI>urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#");
    }

    private static FacturXInvoice SampleInvoiceWithLineItems() => new FacturXInvoice
    {
        InvoiceNumber = "2026-TEST-02",
        IssueDate = new DateTime(2026, 9, 22),
        CurrencyCode = "EUR",
        SellerName = "Eggspot SARL",
        BuyerName = "Acme Corp",
        LineItems =
        {
            new FacturXLineItem
            {
                LineId = "1", ItemName = "Widget", NetUnitPrice = 10.00m, BilledQuantity = 5,
                UnitCode = "C62", LineTotalAmount = 50.00m, VatCategoryCode = "S", VatRatePercent = 20m,
            },
            new FacturXLineItem
            {
                LineId = "2", ItemName = "Gadget", NetUnitPrice = 25.00m, BilledQuantity = 2,
                UnitCode = "C62", LineTotalAmount = 50.00m, VatCategoryCode = "S", VatRatePercent = 20m,
            },
        },
    };

    [Fact]
    public void IsEn16931_TrueOnlyWhenLineItemsPresent()
    {
        FacturXCiiWriter.IsEn16931(SampleInvoice()).Should().BeFalse();
        FacturXCiiWriter.IsEn16931(SampleInvoiceWithLineItems()).Should().BeTrue();
    }

    [Fact]
    public void Generate_WithLineItems_UsesEn16931GuidelineIdAndLineElements()
    {
        var xml = FacturXCiiWriter.Generate(SampleInvoiceWithLineItems());

        xml.Should().Contain($"<ram:ID>{FacturXCiiWriter.En16931GuidelineId}</ram:ID>");
        xml.Should().Contain("<ram:IncludedSupplyChainTradeLineItem>");
        xml.Should().Contain("<ram:LineID>1</ram:LineID>");
        xml.Should().Contain("<ram:LineID>2</ram:LineID>");
        xml.Should().Contain("<ram:Name>Widget</ram:Name>");
        xml.Should().Contain("<ram:Name>Gadget</ram:Name>");
        xml.Should().Contain("<ram:ChargeAmount currencyID=\"EUR\">10.00</ram:ChargeAmount>");
        xml.Should().Contain("<ram:BilledQuantity unitCode=\"C62\">5</ram:BilledQuantity>");
        xml.Should().Contain("<ram:LineTotalAmount currencyID=\"EUR\">50.00</ram:LineTotalAmount>");
    }

    [Fact]
    public void Generate_WithLineItems_GroupsTaxBreakdownByRateAndComputesHeaderTotals()
    {
        var invoice = SampleInvoiceWithLineItems();
        // Both lines share the same 20% rate -- basis should be the combined 100.00, tax 20.00.
        var xml = FacturXCiiWriter.Generate(invoice);

        xml.Should().Contain("<ram:ApplicableTradeTax>");
        xml.Should().Contain("<ram:CalculatedAmount currencyID=\"EUR\">20.00</ram:CalculatedAmount>");
        xml.Should().Contain("<ram:BasisAmount currencyID=\"EUR\">100.00</ram:BasisAmount>");
        xml.Should().Contain("<ram:CategoryCode>S</ram:CategoryCode>");
        xml.Should().Contain("<ram:RateApplicablePercent>20.00</ram:RateApplicablePercent>");

        xml.Should().Contain("<ram:LineTotalAmount currencyID=\"EUR\">100.00</ram:LineTotalAmount>");
        xml.Should().Contain("<ram:TaxBasisTotalAmount currencyID=\"EUR\">100.00</ram:TaxBasisTotalAmount>");
        xml.Should().Contain("<ram:TaxTotalAmount currencyID=\"EUR\">20.00</ram:TaxTotalAmount>");
        xml.Should().Contain("<ram:GrandTotalAmount currencyID=\"EUR\">120.00</ram:GrandTotalAmount>");
        xml.Should().Contain("<ram:DuePayableAmount currencyID=\"EUR\">120.00</ram:DuePayableAmount>");
    }

    [Fact]
    public void Generate_WithLineItems_SeparatesDifferentVatRatesIntoDistinctGroups()
    {
        var invoice = SampleInvoiceWithLineItems();
        invoice.LineItems[1].VatRatePercent = 10m; // second line now at a different rate

        var xml = FacturXCiiWriter.Generate(invoice);

        // Each line always carries its own line-level ApplicableTradeTax (2 lines = 2), plus one
        // header-level ApplicableTradeTax per distinct (category, rate) group -- 2 distinct rates
        // (20% and 10%) here means 2 more, not 1: total 4.
        int taxEntries = 0;
        int idx = 0;
        while ((idx = xml.IndexOf("<ram:ApplicableTradeTax>", idx, StringComparison.Ordinal)) >= 0)
        {
            taxEntries++;
            idx += 1;
        }
        taxEntries.Should().Be(4);
        xml.Should().Contain("<ram:RateApplicablePercent>10.00</ram:RateApplicablePercent>");
        xml.Should().Contain("<ram:RateApplicablePercent>20.00</ram:RateApplicablePercent>");
    }

    [Fact]
    public void Generate_WithLineItems_IgnoresHeaderTotalPropertiesInFavorOfComputedValues()
    {
        var invoice = SampleInvoiceWithLineItems();
        // Deliberately wrong header totals -- must be ignored in favor of the line-item math.
        invoice.TaxBasisTotal = 1m;
        invoice.TaxTotal = 1m;
        invoice.GrandTotal = 1m;
        invoice.DuePayableAmount = 1m;

        var xml = FacturXCiiWriter.Generate(invoice);

        xml.Should().Contain("<ram:GrandTotalAmount currencyID=\"EUR\">120.00</ram:GrandTotalAmount>");
        xml.Should().NotContain(">1.00<");
    }

    [Fact]
    public void PdfDocument_InvoiceWithLineItems_UsesAlternativeAfRelationshipAndEn16931Level()
    {
        var doc = new PdfDocument
        {
            Conformance = PdfAConformance.PdfA3b,
            Invoice = SampleInvoiceWithLineItems(),
        };
        doc.AddPage(595.28f, 841.89f);

        var text = Encoding.Latin1.GetString(doc.ToByteArray());

        text.Should().Contain("/AFRelationship /Alternative");
        text.Should().Contain("<fx:ConformanceLevel>EN16931</fx:ConformanceLevel>");
    }
}
