using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class LinkAnnotationPositionTests
{
    private const float PageHeightPt = 841.89f;

    private static async Task<string> RenderAscii(string body) =>
        Encoding.ASCII.GetString(await HtmlToPdf.RenderAsync("<html><body>" + body + "</body></html>"));

    private static float Number(Group g) => float.Parse(g.Value, CultureInfo.InvariantCulture);

    private static (float X, float Y) WordPosition(string content, string word)
    {
        var m = Regex.Match(content, @"(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) Td \( ?" + Regex.Escape(word) + @"\) Tj");
        m.Success.Should().BeTrue("the word '" + word + "' must be painted as its own run");
        return (Number(m.Groups[1]), Number(m.Groups[2]));
    }

    private static (float Left, float Bottom, float Right, float Top) LinkRect(string content)
    {
        var m = Regex.Match(content, @"/Subtype /Link /Rect \[(-?[\d.]+) (-?[\d.]+) (-?[\d.]+) (-?[\d.]+)\]");
        m.Success.Should().BeTrue("a link annotation must be written");
        return (Number(m.Groups[1]), Number(m.Groups[2]), Number(m.Groups[3]), Number(m.Groups[4]));
    }

    private static void RectShouldCoverWord(string content, string word)
    {
        var (x, baselineY) = WordPosition(content, word);
        var rect = LinkRect(content);

        rect.Left.Should().BeApproximately(x, 4f, "the annotation must start at the link text");
        baselineY.Should().BeInRange(rect.Bottom, rect.Top, "the link text baseline must sit inside the annotation");
        rect.Top.Should().BeLessThanOrEqualTo(PageHeightPt + 0.01f);
    }

    [Fact]
    public async Task LinkBelowTallBlock_RectFollowsText()
    {
        var content = await RenderAscii(
            "<div style=\"height:300px\"></div><p>Contact: <a href=\"https://example.com\">Website here</a></p>");

        RectShouldCoverWord(content, "Website");
    }

    [Fact]
    public async Task LinkInSecondTableColumn_RectFollowsText()
    {
        var content = await RenderAscii(
            "<table style=\"width:500px\"><tr><td>Name</td><td><a href=\"https://example.com\">Go there</a></td></tr></table>");

        RectShouldCoverWord(content, "Go");
    }

    [Fact]
    public async Task LinkInFlexItem_RectFollowsText()
    {
        var content = await RenderAscii(
            "<div style=\"display:flex\"><div style=\"width:200px\">Left</div>" +
            "<div><a href=\"https://example.com\">Right link</a></div></div>");

        RectShouldCoverWord(content, "Right");
    }

    [Fact]
    public async Task LinkOnSecondPage_RectIsOnThePage()
    {
        var content = await RenderAscii(
            "<div style=\"height:1300px\"></div><p>See <a href=\"https://example.com\">later page</a></p>");

        RectShouldCoverWord(content, "later");
    }

    [Theory]
    [InlineData("mailto:sales@example.com")]
    [InlineData("tel:+84123456789")]
    [InlineData("HTTPS://EXAMPLE.COM")]
    public async Task NonHttpSchemes_GetALinkAnnotation(string href)
    {
        var content = await RenderAscii("<p><a href=\"" + href + "\">Contact us</a></p>");

        content.Should().Contain("/URI (" + href + ")");
    }

    [Fact]
    public async Task HrefWithSurroundingWhitespace_AnnotationUsesTrimmedUri()
    {
        var content = await RenderAscii("<p><a href=\"  https://example.com/x  \">Go</a></p>");

        content.Should().Contain("/URI (https://example.com/x)");
    }

    [Fact]
    public async Task JavascriptHref_GetsNoAnnotation()
    {
        var content = await RenderAscii("<p><a href=\"javascript:alert(1)\">Click</a></p>");

        content.Should().NotContain("/Subtype /Link");
    }
}
