using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit;

public class PdfRenderOptionsTests
{
    [Fact]
    public async Task Render_PageSizeOption_AppliesRealPageSize()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync("<h1>Hi</h1>", new PdfRenderOptions { PageSize = "Letter" });
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("/MediaBox [0 0 612.00 792.00]", "Letter = 8.5in x 11in = 612pt x 792pt");
    }

    [Fact]
    public async Task Render_MarginOption_ReservesRealMargin()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync("<h1>Hi</h1>",
            new PdfRenderOptions { MarginTop = 100, MarginLeft = 40, UserStyleSheet = "body{margin:0}" });
        var text = Encoding.ASCII.GetString(pdf);

        var m = Regex.Match(text, @"(-?\d+\.\d+) (-?\d+\.\d+) Td");
        m.Success.Should().BeTrue();
        float x = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        float y = float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);

        // 100px top margin -> text top should sit noticeably below the page's raw top edge (841.89pt)
        y.Should().BeLessThan(841.89f - 100f * 0.75f + 5f);
        // 40px left margin = 30pt
        x.Should().BeApproximately(30f, 2f);
    }

    [Fact]
    public async Task Render_TitleOption_SetsPdfMetadataTitle()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync("<h1>Hi</h1>", new PdfRenderOptions { Title = "My Report Title" });
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("My Report Title");
    }

    [Fact]
    public async Task Render_AuthorOption_SetsPdfMetadataAuthor()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync("<h1>Hi</h1>", new PdfRenderOptions { Author = "Jane Smith" });
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Jane Smith");
    }

    [Fact]
    public async Task Render_NoOptions_TitleTagInHtml_AutoSetsMetadataTitle()
    {
        // Even without PdfRenderOptions, a <title> tag in the HTML itself should become the
        // PDF's /Title metadata -- matching how a browser names a page it prints to PDF.
        byte[] pdf = await HtmlToPdf.RenderAsync("<html><head><title>Auto Title Here</title></head><body><h1>Hi</h1></body></html>");
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Auto Title Here");
    }

    [Fact]
    public async Task Render_UserStyleSheetOption_InjectsCss()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync("<h1>Hi</h1>", new PdfRenderOptions { UserStyleSheet = "h1{color:red}" });
        var text = Encoding.ASCII.GetString(pdf);
        // Red text -> 1.00 0.00 0.00 rg fill color op
        text.Should().Contain("1.00 0.00 0.00 rg");
    }
}
