using System;
using System.Linq;
using System.Text;
using EggPdf.Core;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class UndecodableImageTests
{
    private const string RedPixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==";
    private const string RedGreenBmp = "Qk0+AAAAAAAAADYAAAAoAAAAAgAAAAEAAAABABgAAAAAAAgAAAATCwAAEwsAAAAAAAAAAAAAAAD/AP8AAAA=";

    private static string SvgDataUri() =>
        "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(
            "<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='10' height='10' fill='red'/></svg>"));

    [Theory]
    [InlineData("data:image/png;base64,AAAAAAAAAAAAAAAA")]
    [InlineData("data:image/png;base64,iVBORw0KGgoAAAAA")]
    [InlineData("data:text/plain;base64,aGVsbG8=")]
    public void ImgWithUndecodableData_ProducesNoDanglingImageDraw(string src)
    {
        var pdf = HtmlToPdf.Render($"<p>before</p><img src='{src}' width='20' height='20'><p>after</p>");

        var text = PdfAssert.ValidPdf(pdf, "before", "after");
        PdfAssert.EveryDrawnXObjectIsDefined(text).Should().Be(0);
    }

    [Fact]
    public void ImgWithSvgDataUri_IsDrawnAsVectorGraphics()
    {
        var warnings = new WarningCollector();

        var pdf = HtmlToPdf.Render($"<img src='{SvgDataUri()}' width='20' height='20'><p>after</p>",
            new PdfRenderOptions { Warnings = warnings });

        var text = PdfAssert.ValidPdf(pdf, "after");
        PdfAssert.EveryDrawnXObjectIsDefined(text).Should().Be(0);
        text.Should().Contain("1.00 0.00 0.00 rg", "the SVG's red rect is painted as a vector fill");
        warnings.Warnings.Should().NotContain(w => w.Code == WarningCodes.ImageLoadFailed);
    }

    [Fact]
    public void ImgWithSvgFile_IsDrawnAsVectorGraphics()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "eggpdf-svg-img-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "logo.svg"),
            "<?xml version='1.0'?><svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 10 10'><rect width='10' height='10' fill='#0000ff'/></svg>");

        var pdf = HtmlToPdf.Render("<img src='logo.svg' width='30' height='30'>", dir);

        PdfAssert.ValidPdf(pdf).Should().Contain("0.00 0.00 1.00 rg");
    }

    [Fact]
    public void ImgWithSvgDataUri_RepeatedUse_IsDrawnEachTime()
    {
        var src = SvgDataUri();

        var pdf = HtmlToPdf.Render($"<img src='{src}' width='20' height='20'><img src='{src}' width='20' height='20'>");

        PdfAssert.Count(PdfAssert.ValidPdf(pdf), "1.00 0.00 0.00 rg").Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void ImgWithUndecodableData_RaisesImageLoadFailedWarning()
    {
        var warnings = new WarningCollector();

        HtmlToPdf.Render("<img src='data:image/png;base64,AAAAAAAAAAAAAAAA'>", new PdfRenderOptions { Warnings = warnings });

        var warning = warnings.Warnings.Should().ContainSingle(w => w.Code == WarningCodes.ImageLoadFailed).Which;
        warning.Message.Should().Contain("data:image/png;base64,...").And.NotContain("AAAAAAAA");
    }

    [Fact]
    public void ImgDecodableAndUndecodableTogether_OnlyTheDecodableOneIsDrawn()
    {
        var pdf = HtmlToPdf.Render(
            $"<img src='data:image/png;base64,{RedPixelPng}' width='20' height='20'>" +
            "<img src='data:image/png;base64,AAAAAAAAAAAAAAAA' width='20' height='20'>");

        PdfAssert.EveryDrawnXObjectIsDefined(PdfAssert.ValidPdf(pdf)).Should().Be(1);
    }

    [Fact]
    public void BackgroundImageBmp_IsPainted()
    {
        var pdf = HtmlToPdf.Render(
            $"<div style=\"width:40px;height:20px;background-image:url('data:image/bmp;base64,{RedGreenBmp}')\"></div>");

        var text = PdfAssert.ValidPdf(pdf);
        text.Should().Contain("/Subtype /Image");
        PdfAssert.EveryDrawnXObjectIsDefined(text).Should().BeGreaterThan(0);
    }

    [Fact]
    public void ImgBmp_IsDrawn()
    {
        var pdf = HtmlToPdf.Render($"<img src='data:image/bmp;base64,{RedGreenBmp}' width='20' height='10'>");

        PdfAssert.EveryDrawnXObjectIsDefined(PdfAssert.ValidPdf(pdf)).Should().Be(1);
    }
}
