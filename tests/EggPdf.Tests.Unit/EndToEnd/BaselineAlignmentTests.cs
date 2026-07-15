using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// Mixed-size inline text shares one baseline (CSS default vertical-align:
/// baseline). Small subtitles must not float at the line top, and flex
/// align-items:baseline must align item baselines, not item tops.
/// </summary>
public class BaselineAlignmentTests
{
    private static (float x, float y) FindTextPos(byte[] pdf, string text)
    {
        var content = Encoding.ASCII.GetString(pdf);
        var m = Regex.Match(content, @"(-?\d+\.\d+) (-?\d+\.\d+) Td \(" + Regex.Escape(text) + @"\) Tj");
        m.Success.Should().BeTrue($"'{text}' should be in the content stream");
        return (float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task SmallerInlineSpan_SitsOnParentBaseline()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"font-size:20px\">BIGTEXT <span style=\"font-size:10px\">SMALLTEXT</span></div></body></html>");

        var big = FindTextPos(pdf, "BIGTEXT");
        var small = FindTextPos(pdf, " SMALLTEXT");

        // Baselines aligned: pdfY(big) = top+20, pdfY(small) ≈ top+shift+10 where
        // shift ≈ (20-10)*0.8 = 8px → Td y difference ≈ 2px = 1.5pt (was 7.5pt top-aligned)
        (small.y - big.y).Should().BeLessThan(4f,
            "the small span must sit near the big text's baseline, not the line top");
    }

    [Fact]
    public async Task FlexBaseline_SmallItemAlignsToBigItemBaseline()
    {
        string html(string align) =>
            "<html><body><div style=\"display:flex;align-items:" + align + "\">" +
            "<div style=\"font-size:24px\">NUM</div>" +
            "<div style=\"font-size:12px\">TITLE</div></div></body></html>";

        var baseline = await HtmlToPdf.RenderAsync(html("baseline"));
        var flexStart = await HtmlToPdf.RenderAsync(html("flex-start"));

        float titleBaselineY = FindTextPos(baseline, "TITLE").y;
        float titleFlexStartY = FindTextPos(flexStart, "TITLE").y;

        // With baseline alignment the smaller item shifts DOWN (smaller PDF y)
        titleBaselineY.Should().BeLessThan(titleFlexStartY - 3f,
            "align-items:baseline must move the smaller item down to the shared baseline");
    }
}
