using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class TextAlignWithBlockSiblingTests
{
    [Fact]
    public async Task TextBeforeBlockStyledSpanSibling_StillCenters()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"width:300px\">" +
            "<div style=\"text-align:center\">Short caption" +
            "<span style=\"display:block\">a block-level sibling line</span></div>" +
            "</div></body></html>");

        var content = Encoding.ASCII.GetString(pdf);
        var m = Regex.Match(content, @"(-?\d+\.\d+) (-?\d+\.\d+) Td \(Short caption\) Tj");
        m.Success.Should().BeTrue(
            "a <span> styled display:block is a block sibling, so 'Short caption' takes the per-line text path and " +
            "is painted as one line, not the word-by-word inline path");

        float x = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        x.Should().BeGreaterThan(20f,
            "the caption (like a QR code caption above its link line) is centered in the 300px container, not flush left");
    }
}
