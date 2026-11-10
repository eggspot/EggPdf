using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class InlineBlockShrinkToFitTests
{
    private const string RedPixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==";

    private static (float X, float Width) FrameBackground(string pdfText)
    {
        foreach (Match r in Regex.Matches(pdfText, @"(-?[\d.]+) (-?[\d.]+) ([\d.]+) ([\d.]+) re f"))
        {
            float x = float.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture);
            float w = float.Parse(r.Groups[3].Value, CultureInfo.InvariantCulture);
            float h = float.Parse(r.Groups[4].Value, CultureInfo.InvariantCulture);
            if (h > 80 && h < 100 && w > 80) return (x, w);
        }

        throw new Xunit.Sdk.XunitException("the inline-block frame's ~87pt-tall background rectangle should be painted");
    }

    [Fact]
    public async Task AutoWidthInlineBlock_ShrinksToContent()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"width:260px\">" +
            "<div style=\"display:inline-block;padding:8px;background:#112233\">" +
            $"<img src='data:image/png;base64,{RedPixelPng}' style='display:block;width:100px;height:100px'>" +
            "</div></div></body></html>");

        FrameBackground(Encoding.ASCII.GetString(pdf)).Width.Should().BeApproximately(87f, 2f,
            "an auto-width inline-block shrink-wraps its content (CSS 2.1 §10.3.9): the 100px image plus 16px padding " +
            "is 116px = 87pt, not the full 260px");
    }

    [Fact]
    public async Task InlineBlock_InCenteredParent_IsCentered()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body style=\"margin:0\"><div style=\"width:260px;text-align:center\">" +
            "<div style=\"display:inline-block;padding:8px;background:#112233\">" +
            $"<img src='data:image/png;base64,{RedPixelPng}' style='display:block;width:100px;height:100px'>" +
            "</div></div></body></html>");

        FrameBackground(Encoding.ASCII.GetString(pdf)).X.Should().BeApproximately(54f, 3f,
            "text-align:center centers the 87pt frame in the 195pt (260px) parent: (195 - 87) / 2 = 54pt");
    }
}
