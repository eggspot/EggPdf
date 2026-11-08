using System;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class RoundedClipTests
{
    private static string BmpDataUri(int width, int height)
    {
        int rowBytes = (width * 3 + 3) / 4 * 4;
        var bmp = new byte[54 + rowBytes * height];
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes(54).CopyTo(bmp, 10);
        BitConverter.GetBytes(40).CopyTo(bmp, 14);
        BitConverter.GetBytes(width).CopyTo(bmp, 18);
        BitConverter.GetBytes(height).CopyTo(bmp, 22);
        BitConverter.GetBytes((short)1).CopyTo(bmp, 26);
        BitConverter.GetBytes((short)24).CopyTo(bmp, 28);
        return "data:image/bmp;base64," + Convert.ToBase64String(bmp);
    }

    [Fact]
    public void RoundedImage_IsClippedToItsCorners()
    {
        var text = PdfAssert.ValidPdf(HtmlToPdf.Render($"<img src='{BmpDataUri(4, 4)}' width='40' height='40' style='border-radius:50%'>"));

        int clip = text.IndexOf("h W n", StringComparison.Ordinal);
        int image = text.IndexOf("/Img", StringComparison.Ordinal);
        clip.Should().BeGreaterThan(-1, "a rounded clip path is set");
        clip.Should().BeLessThan(image);
    }

    [Fact]
    public void ObjectFitCover_IsClippedToTheBox()
    {
        var text = PdfAssert.ValidPdf(HtmlToPdf.Render($"<img src='{BmpDataUri(8, 2)}' width='40' height='40' style='object-fit:cover'>"));

        int clip = text.IndexOf("30.00 30.00 re W n", StringComparison.Ordinal);
        int image = text.IndexOf("/Img", StringComparison.Ordinal);
        clip.Should().BeGreaterThan(-1);
        clip.Should().BeLessThan(image);
    }

    [Fact]
    public void OverflowHiddenWithRadius_ClipsChildrenToTheRoundedShape()
    {
        var text = PdfAssert.ValidPdf(HtmlToPdf.Render("<div style='width:100px;height:100px;overflow:hidden;border-radius:20px'><p style='margin:0;background:red'>child</p></div>"));

        text.Should().Contain("h W n");
    }
}
