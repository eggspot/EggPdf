using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class TextAlignMixedInlineTests
{
    private static async Task<float> FirstWordX(string textAlign)
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"width:400px\">" +
            "<div style=\"text-align:" + textAlign + "\">No: <strong>ABC-2026-123</strong></div>" +
            "</div></body></html>");

        var content = Encoding.ASCII.GetString(pdf);
        var m = Regex.Match(content, @"(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) Td \(No:\) Tj");
        m.Success.Should().BeTrue("the first word should be painted as its own text run");
        return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task TextAlignCenter_TextWithInlineElement_PaintsLineCentered()
    {
        float left = await FirstWordX("left");
        float center = await FirstWordX("center");

        (center - left).Should().BeGreaterThan(60f,
            "the line 'No: ABC-2026-123' must be centred in the 400px container, not flush left");
    }

    [Fact]
    public async Task TextAlignRight_TextWithInlineElement_PaintsLineRightAligned()
    {
        float center = await FirstWordX("center");
        float right = await FirstWordX("right");

        (right - center).Should().BeGreaterThan(30f);
    }

    [Fact]
    public async Task TextAlignCenter_LinkInMixedLine_AnnotationCoversCenteredWords()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"width:400px;text-align:center\">" +
            "Go to <a href=\"https://example.com\">the site</a></div></body></html>");

        var content = Encoding.ASCII.GetString(pdf);
        var word = Regex.Match(content, @"(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) Td \( ?the\) Tj");
        var rect = Regex.Match(content, @"/Subtype /Link /Rect \[([\d.]+) [\d.]+ ([\d.]+) [\d.]+\]");
        word.Success.Should().BeTrue();
        rect.Success.Should().BeTrue();

        float wordX = float.Parse(word.Groups[1].Value, CultureInfo.InvariantCulture);
        float rectLeft = float.Parse(rect.Groups[1].Value, CultureInfo.InvariantCulture);

        rectLeft.Should().BeApproximately(wordX, 2f,
            "the link annotation must move with its centred words");
    }
}
