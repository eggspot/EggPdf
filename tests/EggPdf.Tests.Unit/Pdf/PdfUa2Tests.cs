using System;
using System.Text;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfUa2Tests
{
    private static (PdfDocument doc, PdfPage page) DocWithTaggedParagraph(PdfUaVersion uaVersion)
    {
        var doc = new PdfDocument { UaVersion = uaVersion };
        var page = doc.AddPage(595.28f, 841.89f);

        var docElem = new PdfStructureElement("Document");
        var pElem = docElem.AddChild(new PdfStructureElement("P"));
        int mcid = page.BeginMarkedContent("P");
        page.AddText("Hello", 72, 720, "Helvetica", 12);
        page.EndMarkedContent();
        pElem.AddContentRef(page.PageIndex, mcid);

        doc.StructureTree = docElem;
        return (doc, page);
    }

    [Fact]
    public void Ua1Default_HeaderStaysPdf17()
    {
        var (doc, _) = DocWithTaggedParagraph(PdfUaVersion.Ua1);
        var text = Encoding.Latin1.GetString(doc.ToByteArray());
        text.Should().StartWith("%PDF-1.7");
    }

    [Fact]
    public void Ua2_HeaderIsPdf20()
    {
        var (doc, _) = DocWithTaggedParagraph(PdfUaVersion.Ua2);
        var text = Encoding.Latin1.GetString(doc.ToByteArray());
        text.Should().StartWith("%PDF-2.0");
    }

    [Fact]
    public void Ua2_XmpHasPartTwoAndRev2024()
    {
        var (doc, _) = DocWithTaggedParagraph(PdfUaVersion.Ua2);
        var text = Encoding.Latin1.GetString(doc.ToByteArray());
        text.Should().Contain("<pdfuaid:part>2</pdfuaid:part>");
        text.Should().Contain("<pdfuaid:rev>2024</pdfuaid:rev>");
    }

    [Fact]
    public void Ua1_XmpHasPartOneAndNoRev()
    {
        var (doc, _) = DocWithTaggedParagraph(PdfUaVersion.Ua1);
        var text = Encoding.Latin1.GetString(doc.ToByteArray());
        text.Should().Contain("<pdfuaid:part>1</pdfuaid:part>");
        text.Should().NotContain("pdfuaid:rev");
    }

    [Fact]
    public void Ua2_StructTreeRootDeclaresPdf2Namespace()
    {
        var (doc, _) = DocWithTaggedParagraph(PdfUaVersion.Ua2);
        var text = Encoding.Latin1.GetString(doc.ToByteArray());

        text.Should().Contain("/Type /Namespace /NS (http://iso.org/pdf2/ssn)");
        text.Should().MatchRegex(@"/Type /StructTreeRoot[^>]*/Namespaces \[\d+ 0 R\]");
    }

    [Fact]
    public void Ua2_StructElemsReferenceTheNamespace()
    {
        var (doc, _) = DocWithTaggedParagraph(PdfUaVersion.Ua2);
        var text = Encoding.Latin1.GetString(doc.ToByteArray());

        text.Should().MatchRegex(@"/Type /StructElem /S /P /P \d+ 0 R /K \[.*?\] /NS \d+ 0 R");
    }

    [Fact]
    public void Ua1_StructTreeRootHasNoNamespacesEntry()
    {
        var (doc, _) = DocWithTaggedParagraph(PdfUaVersion.Ua1);
        var text = Encoding.Latin1.GetString(doc.ToByteArray());

        text.Should().NotContain("/Namespaces");
        text.Should().NotContain("/Type /Namespace");
    }

    [Fact]
    public void Ua2_CombinedWithPdfAConformance_Throws()
    {
        var doc = new PdfDocument
        {
            Conformance = PdfAConformance.PdfA2b,
            UaVersion = PdfUaVersion.Ua2,
            StructureTree = new PdfStructureElement("Document"),
        };
        doc.AddPage(595.28f, 841.89f);

        Action act = () => doc.ToByteArray();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Ua1_RoleMapStillPresentForLandmarkFallback()
    {
        var (doc, _) = DocWithTaggedParagraph(PdfUaVersion.Ua1);
        var text = Encoding.Latin1.GetString(doc.ToByteArray());
        text.Should().Contain("/RoleMap");
    }

    [Fact]
    public void Ua2_NoRoleMapEmitted()
    {
        // UA-2 landmarks resolve straight to their standard fallback type (Div/Sect) instead of a
        // custom name + RoleMap indirection -- see StructureTreeBuilder's Ua2LandmarkFallback.
        var (doc, _) = DocWithTaggedParagraph(PdfUaVersion.Ua2);
        var text = Encoding.Latin1.GetString(doc.ToByteArray());
        text.Should().NotContain("/RoleMap");
    }
}
