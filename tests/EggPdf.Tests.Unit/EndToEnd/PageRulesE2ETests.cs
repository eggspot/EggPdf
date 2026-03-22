using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class PageRulesE2ETests
{
    [Fact]
    public async Task PageSizeLetter_ChangesDimensions()
    {
        var html = @"
            <html><head><style>
                @page { size: letter; }
            </style></head>
            <body><p>Letter sized page</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        // Letter = 612 x 792 px -> in pt: 612*0.75=459.00, 792*0.75=594.00
        text.Should().Contain("/MediaBox [0 0 459.00 594.00]");
    }

    [Fact]
    public async Task PageSizeA4Landscape_SwapsDimensions()
    {
        var html = @"
            <html><head><style>
                @page { size: A4 landscape; }
            </style></head>
            <body><p>Landscape A4</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        // A4 landscape: swap -> 841.89 x 595.28 px
        // In PDF pt: 841.89 * 0.75 = 631.42, 595.28 * 0.75 = 446.46
        text.Should().Contain("/MediaBox [0 0 631.42 446.46]");
    }

    [Fact]
    public async Task PageMargin_AffectsContentArea()
    {
        // With large margins, text should still be rendered but within the margin area
        var html = @"
            <html><head><style>
                @page { margin: 100px; }
                body { margin: 0; padding: 0; }
            </style></head>
            <body><p>Content with page margins</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Content with page margins");
        // MediaBox should remain full A4 size (margins don't change MediaBox)
        text.Should().Contain("/MediaBox [0 0 446.46 631.42]");
    }

    [Fact]
    public async Task PageSizeCustom_WorksWithPixels()
    {
        var html = @"
            <html><head><style>
                @page { size: 500px 700px; }
            </style></head>
            <body><p>Custom sized page</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        // 500px * 0.75 = 375.00pt, 700px * 0.75 = 525.00pt
        text.Should().Contain("/MediaBox [0 0 375.00 525.00]");
    }

    [Fact]
    public async Task NoPageRule_DefaultA4()
    {
        var html = @"<html><body><p>Default A4</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        // A4: 595.28px * 0.75 = 446.46pt, 841.89px * 0.75 = 631.42pt
        text.Should().Contain("/MediaBox [0 0 446.46 631.42]");
    }

    [Fact]
    public async Task MultiplePageRules_LastWins()
    {
        var html = @"
            <html><head><style>
                @page { size: letter; }
                @page { size: A5; }
            </style></head>
            <body><p>Last rule wins</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        // A5: 419.53px x 595.28px -> in pt: 314.65 x 446.46
        text.Should().Contain("/MediaBox [0 0 314.65 446.46]");
    }
}
