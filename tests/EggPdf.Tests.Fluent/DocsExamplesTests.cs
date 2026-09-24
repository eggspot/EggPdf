using System.Collections.Generic;
using System.Text;
using EggPdf.Fluent;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Fluent;

/// <summary>
/// The code samples in README.md, llms.txt and site/fluent-api.html, verbatim (modulo the
/// placeholder data): if the API changes so a documented sample no longer compiles or renders,
/// these fail and the docs get updated in the same change.
/// </summary>
public class DocsExamplesTests
{
    private sealed record Line(string Name, int Qty);

    private static readonly List<Line> Lines = new() { new Line("Widget", 3), new Line("Gadget", 1) };

    [Fact]
    public void ReadmeAndGuideQuickStart_CompilesAndRenders()
    {
        var lines = Lines;

        byte[] pdf = Document.Create(doc => doc
            .Title("Invoice INV-142")
            .Page(page => page
                .Size(PageSize.A4)
                .Margin(top: 70, right: 30, bottom: 50, left: 30)
                .Header(h => h.Text("Acme Corp").Bold().FontSize(18))
                .Footer(f => f.AlignCenter().PageNumberOfTotal())
                .Watermark("DRAFT")
                .Content(c =>
                {
                    c.Heading(HeadingLevel.H1, "Invoice");
                    c.Item().Row(row =>
                    {
                        row.RelativeItem().Text("Bill to: Jane Doe");
                        row.ConstantItem(Length.Mm(40)).AlignRight().Text("2026-09-24");
                    });
                    c.Item().Table(table =>
                    {
                        table.Header(h => { h.Cell().Text("Item").Bold(); h.Cell().Text("Qty").Bold(); });
                        foreach (var line in lines)
                            table.Row(r => { r.Cell().Text(line.Name); r.Cell().Text(line.Qty.ToString()); })
                                 .AvoidBreakInside();
                    });
                    c.Item().PinBottom().Border(1, Colors.Gray).Padding(10).Text("Signature");
                })))
            .Render();

        // The "Page X of Y" footer makes the renderer embed a subset font, so body text is written
        // as glyph ids rather than searchable literals -- assert structure, not the words.
        var text = Encoding.Latin1.GetString(pdf);
        text.Should().StartWith("%PDF-");
        text.Should().Contain("/Title (Invoice INV-142)");
        text.Should().Contain("/Outlines", "the H1 becomes a bookmark");
    }

    [Fact]
    public void GuideSections_TextGridListsLinksPages_CompileAndRender()
    {
        byte[] pdf = Document.Create(doc => doc
            .Title("Annual report").Author("Finance team").Language("en")
            .FontFace("Brand Sans", "fonts/brand.ttf", weight: 700)
            .Css(".total { font-weight: 700 } @media print { .noprint { display: none } }")
            .Conformance(PdfAConformance.PdfA2b)
            .Tagged()
            .Page(page => page
                .Size(PageSize.Letter.Landscape())
                .Margin(top: 60, right: 20, bottom: 60, left: 20)
                .Header(h => h.Text("Confidential").FontColor(Colors.Gray))
                .Footer(f => f.AlignRight().PageNumberOfTotal(prefix: "Page ", separator: " / "))
                .Watermark("DRAFT", fontSize: Length.Px(120), rotationDegrees: -35, opacity: 0.1f, color: Colors.Red)
                .Content(c =>
                {
                    c.Item()
                        .Text("Quarterly report")
                        .FontSize(20).Bold().FontColor(Colors.Navy)
                        .LetterSpacing(Length.Px(0.5f)).TextTransform(TextCase.Uppercase)
                        .Padding(8, 12).Background(Color.FromHex("#f1f5f9"))
                        .Border(1, Colors.LightGray, BorderLineStyle.Dashed).BorderRadius(6)
                        .BoxShadow(0, 2, 6, Color.FromRgba(0, 0, 0, 64));

                    c.Item().Row(row =>
                    {
                        row.Gap(12).JustifyContent(FlexJustify.SpaceBetween).AlignItems(FlexAlign.Center).Wrap();
                        row.RelativeItem(2).Text("Wide");
                        row.RelativeItem(1).Text("Narrow");
                        row.ConstantItem(Length.Mm(30)).Text("Fixed");
                    });

                    c.Item().Grid(new[] { GridTrack.Fr(1), GridTrack.Fr(2) }, g =>
                    {
                        g.Gap(10).AutoRows(Length.Px(40));
                        g.Item().Text("A");
                        g.Item().Text("B");
                        g.Item(columnSpan: 2).Text("Spans both columns");
                    });
                    c.Item().Grid(3, g => { });

                    c.Item().BulletList(l => { l.Item().Text("One"); l.Item().Text("Two"); });
                    c.Item().NumberedList(l => l.Item().Text("First"));
                    c.Heading(HeadingLevel.H2, "Section");
                    c.Item().Hyperlink("https://example.com", "Visit us");
                    c.Item().Hyperlink("#terms", "See the terms");
                    c.Item().Id("terms").Text("Terms and conditions");

                    c.Item().RawStyle("column-count", "2");
                    c.Item().RawAttribute("data-section", "intro");
                    c.Item().Raw("<dl><dt>Term</dt><dd>Definition</dd></dl>");
                })))
            .Render();

        Encoding.Latin1.GetString(pdf).Should().StartWith("%PDF-");
    }

    [Fact]
    public void StepByStepSample_CompilesAndRenders()
    {
        var doc = Document.New().Title("Report");
        var page = doc.AddPage().Size(PageSize.A4).Margin(30);
        page.Header().Text("Acme").Bold();
        page.Footer().AlignCenter().PageNumberOfTotal();

        var body = page.Content();
        body.Heading(HeadingLevel.H1, "Summary");
        body.Item().Text("Quarterly results");
        AddSalesTable(body);

        byte[] pdf = doc.Render();
        Encoding.Latin1.GetString(pdf).Should().Contain("/Outlines");

        static void AddSalesTable(ColumnDescriptor content)
        {
            var table = content.Item().Table();
            table.Header(h => { h.Cell().Text("Region").Bold(); h.Cell().Text("Sales").Bold(); });
            table.Row(r => { r.Cell().Text("North"); r.Cell().Text("1,200"); });
        }
    }

    [Fact]
    public void GuideMixedPageGroups_CompileAndRender()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(p => p.Size(PageSize.A4).Content(c => c.Item().Text("Report body")))
                .Page(p => p.Size(PageSize.A4.Landscape()).Content(c => c.Item().Text("Wide appendix table"))))
            .Render();

        Encoding.Latin1.GetString(pdf).Should().Contain("Wide appendix table");
    }

    [Fact]
    public void LlmsTxtExample_CompilesAndRenders()
    {
        byte[] pdf = Document.Create(doc => doc
            .Page(page => page
                .Footer(f => f.AlignCenter().PageNumberOfTotal())
                .Content(c =>
                {
                    c.Heading(HeadingLevel.H1, "Hello");
                    c.Item().Table(t => { t.Header(h => h.Cell().Text("Item").Bold()); t.Row(r => r.Cell().Text("Widget")); });
                    c.Item().Hyperlink("#end", "Jump");
                    c.Item().Id("end").Text("Done").FontColor(Colors.Red);
                })))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        text.Should().StartWith("%PDF-");
        text.Should().Contain("/Outlines");
    }
}
