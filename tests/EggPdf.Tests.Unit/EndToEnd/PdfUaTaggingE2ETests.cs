using System;
using System.Text;
using System.Text.RegularExpressions;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class PdfUaTaggingE2ETests
{
    [Fact]
    public void Tagged_SimpleDocument_ProducesStructTreeLinkedToRealContent()
    {
        var html = "<html lang=\"en-US\"><head><title>Report</title></head><body>" +
                    "<h1>Report</h1><p>Hello World</p></body></html>";

        var pdf = HtmlToPdf.Render(html, tagged: true);
        var text = PdfAssert.ValidPdf(pdf, "Report", "Hello World");

        text.Should().Contain("/Type /StructTreeRoot");
        text.Should().Contain("/MarkInfo << /Marked true >>");
        text.Should().Contain("/Lang (en-US)");
        text.Should().Contain("/Type /StructElem /S /H1");
        text.Should().Contain("/Type /StructElem /S /P");

        // Every /MCID n opened in the (uncompressed) content stream must have a matching MCR
        // entry in some StructElem's /K array -- this is the actual proof the tree links to
        // content, not just that both pieces exist independently.
        var mcids = Regex.Matches(text, @"/MCID (\d+)");
        mcids.Count.Should().BeGreaterThan(0);
        foreach (Match m in mcids)
            text.Should().Contain($"/MCID {m.Groups[1].Value} >>", "every opened MCID must be referenced by a StructElem's MCR");
    }

    [Fact]
    public void Tagged_Table_NestsRowsAndCellsWithScope()
    {
        var html = "<table><tr><th scope=\"col\">Name</th></tr><tr><td>Alice</td></tr></table>";

        var pdf = HtmlToPdf.Render(html, tagged: true);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().Contain("/S /Table");
        text.Should().Contain("/S /TR");
        text.Should().Contain("/S /TH");
        text.Should().Contain("/S /TD");
        text.Should().Contain("/A << /O /Table /Scope /Column >>");
    }

    [Fact]
    public void Tagged_List_NestsListItemsInLBody()
    {
        var html = "<ul><li>One</li><li>Two</li></ul>";

        var pdf = HtmlToPdf.Render(html, tagged: true);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().Contain("/S /L");
        text.Should().Contain("/S /LI");
        text.Should().Contain("/S /LBody");
    }

    [Fact]
    public void Tagged_ImageWithAlt_WritesFigureWithAltText()
    {
        // 1x1 red PNG
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var dataUri = "data:image/png;base64," + Convert.ToBase64String(png);
        var html = $"<img src=\"{dataUri}\" alt=\"A red dot\">";

        var pdf = HtmlToPdf.Render(html, tagged: true);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().Contain("/S /Figure");
        text.Should().Contain("/Alt (A red dot)");
    }

    [Fact]
    public void Tagged_CombinedWithPdfA_ProducesBothIdentifications()
    {
        var html = "<h1>Title</h1><p>Body text</p>";

        var pdf = HtmlToPdf.Render(html, PdfAConformance.PdfA2b, tagged: true);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().Contain("<pdfaid:part>2</pdfaid:part>");
        text.Should().Contain("<pdfuaid:part>1</pdfuaid:part>");
        text.Should().Contain("/OutputIntents [");
        text.Should().Contain("/Type /StructTreeRoot");
    }

    [Fact]
    public void PdfA2a_WithTagged_ProducesLevelAConformance()
    {
        var html = "<h1>Title</h1><p>Body text</p>";

        var pdf = HtmlToPdf.Render(html, PdfAConformance.PdfA2a, tagged: true);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().Contain("<pdfaid:part>2</pdfaid:part>");
        text.Should().Contain("<pdfaid:conformance>A</pdfaid:conformance>");
        text.Should().Contain("/Type /StructTreeRoot");
    }

    [Fact]
    public void PdfA2a_WithoutTagged_Throws()
    {
        Action act = () => HtmlToPdf.Render("<h1>Title</h1>", PdfAConformance.PdfA2a, tagged: false);
        act.Should().Throw<InvalidOperationException>().WithMessage("*accessibility tagging*");
    }

    [Fact]
    public void NotTagged_OmitsAllStructureOutput()
    {
        var pdf = HtmlToPdf.Render("<h1>Plain</h1><p>Text</p>");
        var text = PdfAssert.ValidPdf(pdf, "Plain", "Text");

        text.Should().NotContain("/StructTreeRoot");
        text.Should().NotContain("BDC");
    }

    [Fact]
    public void Tagged_MultiPageDocument_TagsContentOnEveryPage()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 60; i++)
            sb.Append($"<p>Paragraph {i}</p>");

        var pdf = HtmlToPdf.Render(sb.ToString(), tagged: true);
        var text = PdfAssert.ValidPdf(pdf);
        PdfAssert.PageCount(text).Should().BeGreaterThan(1);

        // ParentTree must have entries for more than one page (StructParents index > 0 appears).
        text.Should().MatchRegex(@"/StructParents [1-9]\d*");
    }

    [Fact]
    public void Tagged_NamedPageGroups_Throws()
    {
        var html = "<html><head><style>" +
                    "@page wide { size: landscape; } " +
                    ".chart { page: wide; }" +
                    "</style></head><body><p>Normal</p><div class=\"chart\">Wide content</div></body></html>";

        Action act = () => HtmlToPdf.Render(html, tagged: true);
        act.Should().Throw<InvalidOperationException>();
    }
}
