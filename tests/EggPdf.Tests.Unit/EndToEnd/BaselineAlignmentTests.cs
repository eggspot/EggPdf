using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

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

        (small.y - big.y).Should().BeLessThan(4f,
            "with vertical-align: baseline the 10px span shifts down by about (20-10) * 0.8 = 8px, leaving the runs " +
            "~1.5pt apart; top-aligned they would be 7.5pt apart");
    }

    [Fact]
    public async Task LargerInlineSpan_SitsOnParentBaseline()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p>Preceding paragraph</p>" +
            "<div style=\"font-size:10px\">SMALLTEXT <span style=\"font-size:20px\">BIGTEXT</span></div></body></html>");

        var small = FindTextPos(pdf, "SMALLTEXT");
        var big = FindTextPos(pdf, " BIGTEXT");

        (small.y - big.y).Should().BeLessThan(4f,
            "a larger nested span (like a certificate's 22px field value on a 12px label line) must sit on the label's " +
            "baseline too, not hang below it at its unshifted top-referenced Y");
    }

    [Fact]
    public async Task LargerInlineSpan_AsVeryFirstPageContent_StillRenders()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"font-size:10px\">SMALLTEXT <span style=\"font-size:20px\">BIGTEXT</span></div></body></html>");

        var content = Encoding.ASCII.GetString(pdf);
        content.Should().Contain("BIGTEXT",
            "at the very top of page 1 the large run's upward baseline shift can give it a slightly negative Y, and " +
            "page assignment must keep it (there is no earlier page) instead of dropping it");
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

        titleBaselineY.Should().BeLessThan(titleFlexStartY - 3f,
            "align-items:baseline must move the smaller item down (a smaller PDF y) to the shared baseline");
    }
}
