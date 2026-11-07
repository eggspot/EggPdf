using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class RootFontSizeRenderTests
{
    private const string RemMarginDocument =
        "<html><head><style>@page{size:400px 400px;margin:1rem}</style></head>" +
        "<body style='margin:0'><p style='margin:0'>Marker</p></body></html>";

    private static float MarkerX(byte[] pdf)
    {
        var m = Regex.Match(Encoding.ASCII.GetString(pdf), @"(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) Td \(Marker\) Tj");
        m.Success.Should().BeTrue();
        return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void PageMarginInRem_AfterRenderWithLargeRootFont_UsesInitialRootSize()
    {
        HtmlToPdf.Render("<html style='font-size:40px'><body><p>Big root</p></body></html>");

        float x = MarkerX(HtmlToPdf.Render(RemMarginDocument));

        x.Should().BeApproximately(12f, 0.5f, "1rem of @page margin is 16px = 12pt, not the previous render's 40px root");
    }
}
