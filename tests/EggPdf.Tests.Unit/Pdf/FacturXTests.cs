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
}
