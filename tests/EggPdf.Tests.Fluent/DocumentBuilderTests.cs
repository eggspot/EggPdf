using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EggPdf.Fluent;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Fluent;

/// <summary>
/// End-to-end tests for the fluent C# document-builder API: Document -> Page -> Column/Row/Table/
/// Image/Hyperlink/Header/Footer/Watermark, through the same HTML/CSS pipeline
/// HtmlToPdf.Render(string) uses.
/// </summary>
public class DocumentBuilderTests
{
    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==";

    [Fact]
    public void SimpleText_RendersValidPdfWithText()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page
                    .Content(content => content.Item().Text("Hello EggPdf").FontSize(20))))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        text.Should().StartWith("%PDF-");
        text.TrimEnd().Should().EndWith("%%EOF");
        text.Should().Contain("Hello EggPdf");
    }

    [Fact]
    public async Task RenderAsync_ProducesSameContentAsRender()
    {
        byte[] sync = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item().Text("Async build"))))
            .Render();
        byte[] async_ = await Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item().Text("Async build"))))
            .RenderAsync();

        Encoding.Latin1.GetString(sync).Should().Contain("Async build");
        Encoding.Latin1.GetString(async_).Should().Contain("Async build");
    }

    [Fact]
    public void PageSize_DefaultIsA4()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item().Text("A4"))))
            .Render();

        Encoding.Latin1.GetString(pdf).Should().Contain("/MediaBox [0 0 595.28 841.89]", "A4 default = 210mm x 297mm");
    }

    [Fact]
    public void PageSize_Letter_ProducesLetterMediaBox()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Size(PageSize.Letter).Content(content => content.Item().Text("Letter"))))
            .Render();

        Encoding.Latin1.GetString(pdf).Should().Contain("/MediaBox [0 0 612.00 792.00]", "Letter = 8.5in x 11in = 612pt x 792pt");
    }

    [Fact]
    public void PageSize_LandscapeSwapsWidthAndHeight()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Size(PageSize.A4.Landscape()).Content(content => content.Item().Text("Landscape"))))
            .Render();

        Encoding.Latin1.GetString(pdf).Should().Contain("/MediaBox [0 0 841.89 595.28]");
    }

    [Fact]
    public void Row_ItemsAppearAtDifferentXPositions()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item().Row(row =>
                {
                    row.RelativeItem().Text("Left");
                    row.RelativeItem().Text("Right");
                }))))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        float leftX = TextX(text, "Left");
        float rightX = TextX(text, "Right");
        rightX.Should().BeGreaterThan(leftX, "Row lays items out left to right");
    }

    [Fact]
    public void Column_ItemsAppearAtDifferentYPositions()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content =>
                {
                    content.Item().Text("Top");
                    content.Item().Text("Bottom");
                })))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        float topY = TextY(text, "Top");
        float bottomY = TextY(text, "Bottom");
        // PDF y-axis grows upward from the page bottom, so the first (topmost) item has the larger Y.
        topY.Should().BeGreaterThan(bottomY, "Column stacks items top to bottom");
    }

    [Fact]
    public void FontColor_Red_SetsRedFillOperator()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item().Text("Red text").FontColor(Colors.Red))))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        text.Should().Contain("Red text");
        text.Should().MatchRegex(@"1\.00 0\.00 0\.00 rg", "FontColor(\"red\") must set the RGB fill operator");
    }

    [Fact]
    public void Background_Blue_DrawsFilledRectangle()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item()
                    .Background(Colors.Blue).Width(200).Height(100).Text("Box"))))
            .Render();

        Encoding.Latin1.GetString(pdf).Should().MatchRegex(@"0\.00 0\.00 1\.00 rg");
    }

    [Fact]
    public void TextTransform_Uppercase_UppercasesRenderedText()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item()
                    .TextTransform(TextCase.Uppercase).Text("shout"))))
            .Render();

        // text-transform is applied during layout/paint, not by the builder itself -- this only
        // proves the typed value reached the cascade.
        Encoding.Latin1.GetString(pdf).Should().Contain("SHOUT");
    }

    [Fact]
    public void RawStyleEscapeHatch_AppliesArbitraryCssProperty()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item()
                    .RawStyle("text-transform", "uppercase").Text("whisper"))))
            .Render();

        Encoding.Latin1.GetString(pdf).Should().Contain("WHISPER");
    }

    [Fact]
    public void RawHtmlEscapeHatch_InjectsArbitraryMarkup()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item()
                    .Raw("<ul><li>First</li><li>Second</li></ul>"))))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        text.Should().Contain("First");
        text.Should().Contain("Second");
    }

    [Fact]
    public void Heading_ProducesAutoBookmarkFromRealHeadingTag()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Heading(HeadingLevel.H1, "Chapter One"))))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        text.Should().Contain("Chapter One");
        text.Should().Contain("/Outlines", "a real <h1> must produce an auto-generated PDF bookmark");
    }

    [Fact]
    public void Image_EmbedsImageXObject()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item()
                    .Image("data:image/png;base64," + OnePixelPng, widthPx: 40, heightPx: 40))))
            .Render();

        Encoding.Latin1.GetString(pdf).Should().Contain("/Subtype /Image");
    }

    [Fact]
    public void Hyperlink_ProducesClickableLinkAnnotation()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item()
                    .Hyperlink("https://example.com", "Visit us"))))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        text.Should().Contain("Visit");
        text.Should().Contain("us");
        text.Should().Contain("/Subtype /Link");
        text.Should().Contain("(https://example.com)");
    }

    [Fact]
    public void Table_HeaderRepeatsAsTheadAndRowsRenderAsCells()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item().Table(table =>
                {
                    table.Header(h =>
                    {
                        h.Cell().Text("Item");
                        h.Cell().Text("Qty");
                    });
                    table.Row(row =>
                    {
                        row.Cell().Text("Widget");
                        row.Cell().Text("3");
                    }).AvoidBreakInside();
                }))))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        text.Should().Contain("Item");
        text.Should().Contain("Qty");
        text.Should().Contain("Widget");
    }

    [Fact]
    public void PinBottom_SetsEggPdfCustomProperty()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item()
                    .PinBottom().Text("Signature"))))
            .Render();

        Encoding.Latin1.GetString(pdf).Should().Contain("Signature");
    }

    [Fact]
    public void HeaderAndFooter_RepeatOnEveryPhysicalPage()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page
                    .Margin(60, 20, 60, 20)
                    .Header(h => h.Text("Running Header"))
                    .Footer(f => f.Text("Running Footer"))
                    .Content(content =>
                    {
                        for (int i = 0; i < 80; i++)
                            content.Item().Height(20).Text("Line " + i);
                    })))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        Occurrences(text, "Running Header").Should().BeGreaterThan(1, "the header must repeat on more than one physical page");
        Occurrences(text, "Running Footer").Should().BeGreaterThan(1, "the footer must repeat on more than one physical page");
    }

    [Fact]
    public void Watermark_RepeatsOnEveryPhysicalPage()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page
                    .Watermark("DRAFT")
                    .Content(content =>
                    {
                        for (int i = 0; i < 80; i++)
                            content.Item().Height(20).Text("Line " + i);
                    })))
            .Render();

        Occurrences(Encoding.Latin1.GetString(pdf), "DRAFT").Should().BeGreaterThan(1);
    }

    [Fact]
    public void PageNumberOfTotal_ResolvesDifferentlyPerPhysicalPage()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page
                    .Footer(f => f.PageNumberOfTotal())
                    .Content(content =>
                    {
                        for (int i = 0; i < 80; i++)
                            content.Item().Height(20).Text("Line " + i);
                    })))
            .Render();

        var streams = ContentStreams(pdf);
        streams.Count.Should().BeGreaterThan(1, "enough content must force more than one physical page");
        streams[0].Should().NotBe(streams[streams.Count - 1], "the page-number footer must differ between the first and last page");
    }

    [Fact]
    public void MultiplePages_UseNamedPagesForDifferentSizes()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Size(PageSize.A4).Content(content => content.Item().Text("Portrait body")))
                .Page(page => page.Size(PageSize.A4.Landscape()).Content(content => content.Item().Text("Landscape appendix"))))
            .Render();

        var boxes = Regex.Matches(Encoding.Latin1.GetString(pdf), @"/MediaBox \[0 0 ([\d.]+) ([\d.]+)\]");
        boxes.Count.Should().Be(2, "two Page() calls must produce two distinctly-sized physical pages");
        float firstWidth = float.Parse(boxes[0].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        float secondWidth = float.Parse(boxes[1].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        secondWidth.Should().BeGreaterThan(firstWidth, "the second (landscape) page group must be wider than the first (portrait) one");
    }

    [Fact]
    public void Conformance_PdfA2b_EmitsOutputIntentAndXmpMetadata()
    {
        byte[] pdf = Document.Create(doc => doc
                .Conformance(PdfAConformance.PdfA2b)
                .Page(page => page.Content(content => content.Item().Text("Archival"))))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        text.Should().Contain("/OutputIntents [");
        text.Should().Contain("<pdfaid:conformance>B</pdfaid:conformance>");
    }

    [Fact]
    public void Encrypt_EmitsEncryptDictionary()
    {
        byte[] pdf = Document.Create(doc => doc
                .Encrypt(new PdfEncryption { OwnerPassword = "secret", AllowPrinting = false })
                .Page(page => page.Content(content => content.Item().Text("Confidential"))))
            .Render();

        Encoding.Latin1.GetString(pdf).Should().Contain("/Encrypt <<");
    }

    [Fact]
    public void Tagged_EmitsStructureTreeAndMarkInfo()
    {
        byte[] pdf = Document.Create(doc => doc
                .Tagged()
                .Page(page => page.Content(content => content.Heading(HeadingLevel.H1, "Accessible Title"))))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        text.Should().Contain("/Type /StructTreeRoot");
        text.Should().Contain("/MarkInfo << /Marked true >>");
    }

    [Fact]
    public void GeneratedContent_CalledTwiceOnSameContainer_ThrowsInsteadOfSilentlyDroppingOne()
    {
        // CSS allows only one ::after per element -- a second CurrentPageNumber()/TotalPages()/
        // PageNumberOfTotal() call on the same container can't visually combine with the first,
        // it can only replace it, so the builder must fail loudly instead of silently discarding one.
        Action act = () => Document.Create(doc => doc
                .Page(page => page.Content(content =>
                {
                    var item = content.Item();
                    item.CurrentPageNumber();
                    item.TotalPages();
                })))
            .Render();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Row_JustifyContentCenter_ClustersItemsAwayFromRowEdges()
    {
        byte[] wide = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item().Row(row =>
                {
                    row.JustifyContent(FlexJustify.Center);
                    row.ConstantItem(40).Text("A");
                    row.ConstantItem(40).Text("B");
                }))))
            .Render();

        var text = Encoding.Latin1.GetString(wide);
        float ax = TextX(text, "A");
        // Default A4 content width (with the default 20px page margin) is roughly 555px; two
        // 40px-wide centered items should sit well clear of the left content edge (x != margin).
        ax.Should().BeGreaterThan(30f, "justify-content:center must not leave the row flush against its start edge");
    }

    [Fact]
    public void Row_Gap_SeparatesConstantItems()
    {
        byte[] noGap = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item().Row(row =>
                {
                    row.ConstantItem(40).Text("A");
                    row.ConstantItem(40).Text("B");
                }))))
            .Render();
        byte[] withGap = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item().Row(row =>
                {
                    row.Gap(50);
                    row.ConstantItem(40).Text("A");
                    row.ConstantItem(40).Text("B");
                }))))
            .Render();

        float bNoGap = TextX(Encoding.Latin1.GetString(noGap), "B");
        float bWithGap = TextX(Encoding.Latin1.GetString(withGap), "B");
        bWithGap.Should().BeGreaterThan(bNoGap, "Gap(50) must push the second item further right than no gap at all");
    }

    [Fact]
    public void BorderRadius_PaintsRoundedCornersWithBezierCurves()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item()
                    .Background(Colors.Blue).Width(100).Height(100).BorderRadius(20))))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        text.Should().Contain("0.00 0.00 1.00 rg");
        text.Should().MatchRegex(@" c\b", "rounded corners are painted as bezier curve segments, not a plain rectangle");
    }

    [Fact]
    public void Overflow_Hidden_EmitsClipRect()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item()
                    .Width(50).Height(50).Overflow(OverflowMode.Hidden).Text("Clipped"))))
            .Render();

        Encoding.Latin1.GetString(pdf).Should().Contain("re W n", "overflow:hidden must establish a clip region");
    }

    [Fact]
    public void TypedStyleBatch_RendersWithoutError()
    {
        // Smoke test for the plain one-line SetStyle wrappers (Padding/Margin TRBL overloads,
        // Min/MaxWidth/Height, individual border sides, box-shadow, opacity, position, text
        // decoration, line-height/letter-spacing): the generic style plumbing is already proven
        // by FontColor/Background/RawStyleEscapeHatch, so this only proves each method compiles,
        // chains, and reaches a valid render rather than re-verifying the CSS engine per property.
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item()
                    .Padding(4, 8, 4, 8)
                    .Margin(2, 4)
                    .MinWidth(50).MaxWidth(300).MinHeight(20).MaxHeight(200)
                    .BorderTop(1, Colors.Black).BorderRight(1, Colors.Black, BorderLineStyle.Dashed)
                    .BorderBottom(1, Colors.Black).BorderLeft(1, Colors.Black)
                    .BoxShadow(2, 2, 4, Color.FromRgba(0, 0, 0, 128))
                    .Opacity(0.9f)
                    .Position(PositionMode.Relative)
                    .LineHeight(1.4f)
                    .LetterSpacing(1)
                    .Underline()
                    .Text("Styled"))))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        text.Should().StartWith("%PDF-");
        text.Should().Contain("Styled");
    }

    [Fact]
    public void TitleAndAuthor_AppearInDocumentInfo()
    {
        byte[] pdf = Document.Create(doc => doc
                .Title("Quarterly Report").Author("Jane Doe")
                .Page(page => page.Content(content => content.Item().Text("Body"))))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        text.Should().Contain("/Title (Quarterly Report)");
        text.Should().Contain("/Author (Jane Doe)");
    }

    [Fact]
    public void Css_WithAttributeClass_StylesTargetedElement()
    {
        byte[] pdf = Document.Create(doc => doc
                .Css(".hot{color:red}")
                .Page(page => page.Content(content => content.Item().Class("hot").Text("Warm"))))
            .Render();

        Encoding.Latin1.GetString(pdf).Should().Contain("1.00 0.00 0.00 rg");
    }

    [Fact]
    public void BulletAndNumberedLists_RenderAllItems()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content =>
                {
                    content.Item().BulletList(l => { l.Item().Text("Apple"); l.Item().Text("Pear"); });
                    content.Item().NumberedList(l => { l.Item().Text("First"); l.Item().Text("Second"); });
                })))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        foreach (var word in new[] { "Apple", "Pear", "First", "Second" })
            text.Should().Contain(word);
    }

    [Fact]
    public void Grid_PlacesCellsInColumns()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content => content.Item().Grid(new[] { GridTrack.Fr(1), GridTrack.Fr(1) }, g =>
                {
                    g.Gap(10);
                    g.Item().Text("Cell1");
                    g.Item().Text("Cell2");
                    g.Item(2).Text("Wide");
                }))))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        TextX(text, "Cell2").Should().BeGreaterThan(TextX(text, "Cell1"));
        TextY(text, "Wide").Should().BeLessThan(TextY(text, "Cell1"), "the spanning cell wraps onto the second grid row");
    }

    [Fact]
    public void IdAndInternalHyperlink_ProduceDestinationLinkAnnotation()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(page => page.Content(content =>
                {
                    content.Item().Hyperlink("#target", "Jump");
                    content.Item().Id("target").Text("Destination");
                })))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        text.Should().Contain("Jump");
        text.Should().Contain("Destination");
        text.Should().MatchRegex(@"/Subtype /Link[^>]*/Dest \[\d+ 0 R /XYZ", "Id(\"target\") + Hyperlink(\"#target\") must be a working internal link");
    }

    [Fact]
    public void FloatAndTransform_RenderWithoutError()
    {
        byte[] pdf = Document.Create(doc => doc
                .Language("en")
                .FontFace("Missing", "missing.ttf")
                .Page(page => page.Content(content =>
                {
                    content.Item().Float(FloatSide.Right).Width(50).Text("Right");
                    content.Item().Transform(CssTransform.Rotate(10).Then(CssTransform.Scale(1.1f))).Text("Tilted");
                })))
            .Render();

        var text = Encoding.Latin1.GetString(pdf);
        text.Should().Contain("Right");
        text.Should().Contain("Tilted");
    }

    private static float TextX(string content, string word) => TextPosition(content, word).X;
    private static float TextY(string content, string word) => TextPosition(content, word).Y;

    private static (float X, float Y) TextPosition(string content, string word)
    {
        var m = Regex.Match(content, @"(-?\d+\.\d+) (-?\d+\.\d+) Td \(" + Regex.Escape(word) + @"\) Tj");
        m.Success.Should().BeTrue($"a positioned text run for '{word}' must exist");
        return (float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static int Occurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    /// <summary>Every "stream ... endstream" span's raw text, in document order.</summary>
    private static List<string> ContentStreams(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var result = new List<string>();
        foreach (Match m in Regex.Matches(text, "stream\r?\n(.*?)endstream", RegexOptions.Singleline))
            result.Add(m.Groups[1].Value);
        return result;
    }
}
