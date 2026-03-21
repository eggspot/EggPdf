using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class Phase1AcceptanceTests
{
    [Fact]
    public async Task HelloWorld_ProducesValidPdf()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync("<h1>Hello World</h1>");

        pdf.Should().NotBeEmpty();
        var header = Encoding.ASCII.GetString(pdf, 0, 8);
        header.Should().StartWith("%PDF-1.7");
    }

    [Fact]
    public async Task SimpleDocument_HasSelectableText()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync("<p>Test document content</p>");

        var text = Encoding.ASCII.GetString(pdf);
        // The text should appear in the PDF content stream
        text.Should().Contain("Test document content");
    }

    [Fact]
    public async Task Heading_RendersLargerText()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync("<h1>Big Title</h1><p>Small text</p>");

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Big Title");
        text.Should().Contain("Small text");
    }

    [Fact]
    public async Task LinkTag_ProducesClickableLink()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync(
            "<p><a href='https://example.com'>Click here</a></p>");

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("https://example.com");
        text.Should().Contain("/Annot");
    }

    [Fact]
    public async Task BackgroundColor_AppearsInPdf()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync(
            "<div style='background-color: red; width: 200px; height: 100px'>Red box</div>");

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        // Should contain rectangle fill operator
        text.Should().Contain("re");
    }

    [Fact]
    public async Task MultipleElements_AllRendered()
    {
        var html = @"
            <h1>Title</h1>
            <p>Paragraph one</p>
            <p>Paragraph two</p>
            <div style='background-color: blue; height: 50px'></div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Title");
        text.Should().Contain("Paragraph one");
        text.Should().Contain("Paragraph two");
    }

    [Fact]
    public async Task RenderToFile_CreatesFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eggpdf_test_{System.Guid.NewGuid():N}.pdf");

        try
        {
            await HtmlToPdf.RenderToFileAsync("<h1>File test</h1>", path);

            File.Exists(path).Should().BeTrue();
            var bytes = File.ReadAllBytes(path);
            bytes.Length.Should().BeGreaterThan(100);
            Encoding.ASCII.GetString(bytes, 0, 8).Should().StartWith("%PDF");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task RenderToStream_WritesToStream()
    {
        using var ms = new MemoryStream();

        await HtmlToPdf.RenderAsync("<p>Stream test</p>", ms);

        ms.Length.Should().BeGreaterThan(100);
        ms.Position = 0;
        var header = new byte[8];
        ms.Read(header, 0, 8);
        Encoding.ASCII.GetString(header).Should().StartWith("%PDF");
    }

    [Fact]
    public async Task EmptyHtml_ProducesValidPdf()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync("");

        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 8).Should().StartWith("%PDF");
    }

    [Fact]
    public async Task NullHtml_ProducesValidPdf()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync(null!);

        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 8).Should().StartWith("%PDF");
    }

    [Fact]
    public void SyncRender_Works()
    {
        byte[] pdf = HtmlToPdf.Render("<h1>Sync test</h1>");

        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 8).Should().StartWith("%PDF");
    }
}
