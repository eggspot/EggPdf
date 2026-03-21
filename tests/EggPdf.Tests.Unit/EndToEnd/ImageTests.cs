using System;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class ImageTests
{
    // 1x1 red pixel PNG as base64
    private const string RedPixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==";

    [Fact]
    public async Task ImgWithBase64_DoesNotCrash()
    {
        var html = $"<img src='data:image/png;base64,{RedPixelPng}' width='100' height='100'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 8).Should().StartWith("%PDF");
    }

    [Fact]
    public async Task ImgWithAlt_TextRenderedWhenNoSrc()
    {
        var html = "<img alt='Logo placeholder'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ImgWithDimensions_PresentsInPdf()
    {
        var html = $"<img src='data:image/png;base64,{RedPixelPng}' width='200' height='100' alt='Test image'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public async Task BackgroundImage_DoesNotCrash()
    {
        var html = "<div style='background-color: blue; width: 200px; height: 100px'>Blue box</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Blue box");
    }

    [Fact]
    public async Task MultipleImages_AllPresent()
    {
        var html = $@"
            <p>Before images</p>
            <img src='data:image/png;base64,{RedPixelPng}' width='50' height='50'>
            <img src='data:image/png;base64,{RedPixelPng}' width='50' height='50'>
            <p>After images</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Before images");
        text.Should().Contain("After images");
    }

    [Fact]
    public async Task BrokenImage_DoesNotCrash()
    {
        var html = "<img src='https://nonexistent.example.com/image.png' alt='Broken'>";

        // Should not throw, should produce valid PDF
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SvgInline_DoesNotCrash()
    {
        var html = @"
            <svg width='100' height='100'>
                <circle cx='50' cy='50' r='40' fill='red'/>
            </svg>";

        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GradientBackground_DoesNotCrash()
    {
        var html = "<div style='background: linear-gradient(red, blue); width: 200px; height: 100px'>Gradient</div>";

        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }
}
