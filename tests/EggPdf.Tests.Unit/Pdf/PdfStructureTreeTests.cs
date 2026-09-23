using System;
using System.Text;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfStructureTreeTests
{
    private static (PdfDocument doc, PdfPage page) DocWithTaggedParagraph(PdfAConformance? conformance = null)
    {
        var doc = new PdfDocument { Conformance = conformance, Title = "My Title" };
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
    public void NoStructureTree_OmitsAllTaggingEntries()
    {
        var doc = new PdfDocument();
        doc.AddPage(595.28f, 841.89f);
        var text = Encoding.Latin1.GetString(doc.ToByteArray());

        text.Should().NotContain("/StructTreeRoot");
        text.Should().NotContain("/MarkInfo");
        text.Should().NotContain("/Tabs");
    }

    [Fact]
    public void StructureTree_Catalog_HasRequiredEntries()
    {
        var (doc, _) = DocWithTaggedParagraph();
        var text = Encoding.Latin1.GetString(doc.ToByteArray());

        text.Should().Contain("/StructTreeRoot");
        text.Should().Contain("/MarkInfo << /Marked true >>");
        text.Should().Contain("/ViewerPreferences << /DisplayDocTitle true >>");
        text.Should().Contain("/Lang (en)");
        text.Should().Contain("/Tabs /S");
        text.Should().Contain("/StructParents 0");
    }

    [Fact]
    public void StructureTree_WritesStructTreeRootAndParentTree()
    {
        var (doc, _) = DocWithTaggedParagraph();
        var text = Encoding.Latin1.GetString(doc.ToByteArray());

        text.Should().Contain("/Type /StructTreeRoot");
        text.Should().Contain("/ParentTree");
        text.Should().Contain("/ParentTreeNextKey 1");
        text.Should().Contain("/Nums [0 [");
    }

    [Fact]
    public void StructureTree_WritesStructElemsWithTypeAndParent()
    {
        var (doc, _) = DocWithTaggedParagraph();
        var text = Encoding.Latin1.GetString(doc.ToByteArray());

        text.Should().Contain("/Type /StructElem /S /Document");
        text.Should().Contain("/Type /StructElem /S /P");
    }

    [Fact]
    public void StructureTree_ContentReference_IsMcrWithMatchingPageAndMcid()
    {
        var (doc, page) = DocWithTaggedParagraph();
        var text = Encoding.Latin1.GetString(doc.ToByteArray());

        text.Should().Contain("/Type /MCR");
        text.Should().Contain("/MCID 0");
        page.PageIndex.Should().Be(0);
    }

    [Fact]
    public void ContentStream_HasMatchingBdcEmc()
    {
        var (doc, _) = DocWithTaggedParagraph();
        // Content streams are FlateDecode-compressed by default; disable compression to inspect the raw stream text.
        doc.CompressContentStreams = false;
        var text = Encoding.Latin1.GetString(doc.ToByteArray());

        text.Should().Contain("/P << /MCID 0 >> BDC");
        text.Should().Contain("EMC");
    }

    [Fact]
    public void StructureTree_WithoutPdfA_OmitsPdfaidButHasPdfuaid()
    {
        var (doc, _) = DocWithTaggedParagraph(conformance: null);
        var text = Encoding.Latin1.GetString(doc.ToByteArray());

        text.Should().NotContain("pdfaid:");
        text.Should().Contain("xmlns:pdfuaid='http://www.aiim.org/pdfua/ns/id/'");
        text.Should().Contain("<pdfuaid:part>1</pdfuaid:part>");
        text.Should().Contain("<dc:title>");
        text.Should().Contain("My Title");
        // Tagging alone doesn't need an ICC output intent -- that's a PDF/A-only requirement.
        text.Should().NotContain("/OutputIntents");
    }

    [Fact]
    public void StructureTree_CombinedWithPdfA_HasBothIdentifications()
    {
        var (doc, _) = DocWithTaggedParagraph(conformance: PdfAConformance.PdfA2b);
        var text = Encoding.Latin1.GetString(doc.ToByteArray());

        text.Should().Contain("<pdfaid:part>2</pdfaid:part>");
        text.Should().Contain("<pdfuaid:part>1</pdfuaid:part>");
        text.Should().Contain("/OutputIntents [");
    }

    [Fact]
    public void StructureTree_NoTitleSet_DefaultsToPlaceholder()
    {
        var doc = new PdfDocument(); // no Title set
        var page = doc.AddPage(595.28f, 841.89f);
        var docElem = new PdfStructureElement("Document");
        int mcid = page.BeginMarkedContent("P");
        page.AddText("Hi", 72, 720, "Helvetica", 12);
        page.EndMarkedContent();
        docElem.AddContentRef(page.PageIndex, mcid);
        doc.StructureTree = docElem;

        var text = Encoding.Latin1.GetString(doc.ToByteArray());
        text.Should().Contain("Untitled Document");
    }

    [Fact]
    public void StructureTree_WithEncryption_Throws()
    {
        var doc = new PdfDocument
        {
            Encryption = new PdfEncryption(),
            StructureTree = new PdfStructureElement("Document"),
        };
        doc.AddPage(595.28f, 841.89f);

        Action act = () => doc.ToByteArray();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FigureElement_WithAlt_WritesAltEntry()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595.28f, 841.89f);
        var docElem = new PdfStructureElement("Document");
        var figElem = docElem.AddChild(new PdfStructureElement("Figure") { Alt = "A red circle" });
        int mcid = page.BeginMarkedContent("Figure");
        page.EndMarkedContent();
        figElem.AddContentRef(page.PageIndex, mcid);
        doc.StructureTree = docElem;

        var text = Encoding.Latin1.GetString(doc.ToByteArray());
        text.Should().Contain("/Alt (A red circle)");
    }

    [Fact]
    public void TableHeaderElement_WithScope_WritesAttributeDict()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595.28f, 841.89f);
        var docElem = new PdfStructureElement("Document");
        var thElem = docElem.AddChild(new PdfStructureElement("TH") { TableScope = "Column" });
        int mcid = page.BeginMarkedContent("TH");
        page.EndMarkedContent();
        thElem.AddContentRef(page.PageIndex, mcid);
        doc.StructureTree = docElem;

        var text = Encoding.Latin1.GetString(doc.ToByteArray());
        text.Should().Contain("/A << /O /Table /Scope /Column >>");
    }

    [Fact]
    public void LinkAnnotation_Tagged_WritesObjrAndStructParentAsIndirectObject()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595.28f, 841.89f);
        var docElem = new PdfStructureElement("Document");
        var linkElem = docElem.AddChild(new PdfStructureElement("Link"));
        int mcid = page.BeginMarkedContent("Link");
        page.AddText("Click here", 72, 720, "Helvetica", 12);
        page.EndMarkedContent();
        linkElem.AddContentRef(page.PageIndex, mcid);
        var link = page.AddLink(72, 700, 100, 20, "https://example.com");
        link.TaggedElement = linkElem;
        doc.StructureTree = docElem;

        var text = Encoding.Latin1.GetString(doc.ToByteArray());

        // The annotation must be an indirect object (referenced from /Annots, not written inline)
        // so /Type /OBJR /Obj can point back to the exact same dictionary.
        text.Should().MatchRegex(@"/Annots \[\s*\d+ 0 R");
        text.Should().Contain("/Type /OBJR");
        // One page (index 0) already occupies ParentTree key 0, so the annotation's own key
        // starts at 1 -- it must never collide with a page's /StructParents value.
        text.Should().Contain("/StructParent 1");
        text.Should().Contain("/Subtype /Link");
    }

    [Fact]
    public void LinkAnnotation_Untagged_StaysInlineWithNoStructParent()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595.28f, 841.89f);
        var docElem = new PdfStructureElement("Document");
        int mcid = page.BeginMarkedContent("P");
        page.EndMarkedContent();
        docElem.AddContentRef(page.PageIndex, mcid);
        doc.StructureTree = docElem;
        page.AddLink(72, 700, 100, 20, "https://example.com"); // TaggedElement left null

        var text = Encoding.Latin1.GetString(doc.ToByteArray());

        text.Should().NotContain("/StructParent ");
        text.Should().NotContain("/Type /OBJR");
        text.Should().Contain("/Type /Annot /Subtype /Link");
    }

    [Fact]
    public void StructureTree_AlwaysWritesRoleMapForLandmarkFallback()
    {
        var (doc, _) = DocWithTaggedParagraph();
        var text = Encoding.Latin1.GetString(doc.ToByteArray());

        text.Should().Contain("/RoleMap");
        text.Should().Contain("/Nav /Div");
        text.Should().Contain("/Header /Div");
        text.Should().Contain("/Footer /Div");
        text.Should().Contain("/Aside /Div");
        text.Should().Contain("/Main /Div");
        text.Should().Contain("/Article /Sect");
        text.Should().Contain("/Section /Sect");
    }
}
