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
        // Letter = 816px × 1056px at 96dpi → ×0.75 = 612pt × 792pt
        text.Should().Contain("/MediaBox [0 0 612.00 792.00]");
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
        // A4 landscape: swap width/height → 1122.52px × 793.70px at 96dpi
        // In PDF pt: 1122.52 × 0.75 = 841.89pt, 793.70 × 0.75 = 595.28pt
        text.Should().Contain("/MediaBox [0 0 841.89 595.28]");
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
        text.Should().Contain("/MediaBox [0 0 595.28 841.89]");
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
        // A4 default: 793.70px × 1122.52px at 96dpi → ×0.75 = 595.28pt × 841.89pt
        text.Should().Contain("/MediaBox [0 0 595.28 841.89]");
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
        // A5: 559.37px × 793.70px at 96dpi → ×0.75 = 419.53pt × 595.28pt
        text.Should().Contain("/MediaBox [0 0 419.53 595.28]");
    }
}
