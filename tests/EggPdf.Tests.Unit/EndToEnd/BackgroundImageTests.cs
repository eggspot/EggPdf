using System;
using System.Text;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class BackgroundImageTests
{
    private static string BmpDataUri(int width, int height)
    {
        int rowBytes = (width * 3 + 3) / 4 * 4;
        int pixelBytes = rowBytes * height;
        var bmp = new byte[54 + pixelBytes];
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes(54).CopyTo(bmp, 10);
        BitConverter.GetBytes(40).CopyTo(bmp, 14);
        BitConverter.GetBytes(width).CopyTo(bmp, 18);
        BitConverter.GetBytes(height).CopyTo(bmp, 22);
        BitConverter.GetBytes((short)1).CopyTo(bmp, 26);
        BitConverter.GetBytes((short)24).CopyTo(bmp, 28);
        BitConverter.GetBytes(pixelBytes).CopyTo(bmp, 34);
        return "data:image/bmp;base64," + Convert.ToBase64String(bmp);
    }

    private static string Render(string backgroundCss) =>
        PdfAssert.ValidPdf(HtmlToPdf.Render(
            "<div style='width:200px;height:100px;background-image:url(" + BmpDataUri(4, 2) + ");" + backgroundCss + "'></div>"));

    [Fact]
    public void NoRepeatWithoutSize_UsesTheNaturalSizeAtTheTopLeft()
    {
        var text = Render("background-repeat:no-repeat");

        text.Should().Contain("3.00 0 0 1.50 ", "a 4x2 image keeps its natural 4x2px size instead of stretching to the box");
    }

    [Fact]
    public void SizeWithOneValue_KeepsTheRatioAndPercentagePositionAlignsTheImage()
    {
        var text = Render("background-repeat:no-repeat;background-size:50px;background-position:100% 50%");

        text.Should().Contain("37.50 0 0 18.75 ", "50px wide keeps the 2:1 ratio");
    }

    [Fact]
    public void RepeatedTiles_AreClippedToTheBox()
    {
        var text = Render("background-size:30px 30px");

        text.Should().Contain("re W n", "tiles that overhang the box are clipped");
    }

    [Fact]
    public void SvgBackgroundImage_IsPaintedAsVectors()
    {
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='10' height='10' fill='#ff0000'/></svg>";
        var uri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));

        var text = PdfAssert.ValidPdf(HtmlToPdf.Render("<div style=\"width:40px;height:20px;background-image:url(" + uri + ")\"></div>"));

        text.Should().Contain("1.00 0.00 0.00 rg");
    }
}
