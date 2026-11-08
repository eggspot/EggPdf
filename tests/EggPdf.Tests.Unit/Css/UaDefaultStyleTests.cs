using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EggPdf.Css;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class UaDefaultStyleTests
{
    private readonly BasicStyleResolver _resolver = new();

    private ComputedStyle Resolve(string tag) => _resolver.Resolve(new HtmlElement(tag), null);

    [Theory]
    [InlineData("dl", "display", "block")]
    [InlineData("dt", "display", "block")]
    [InlineData("dd", "display", "block")]
    [InlineData("dd", "margin-left", "40px")]
    [InlineData("menu", "list-style-type", "disc")]
    [InlineData("dir", "padding-left", "40px")]
    [InlineData("xmp", "white-space", "pre")]
    [InlineData("listing", "font-family", "monospace")]
    [InlineData("hgroup", "display", "block")]
    [InlineData("search", "display", "block")]
    [InlineData("strike", "text-decoration", "line-through")]
    [InlineData("tt", "font-family", "monospace")]
    [InlineData("nobr", "white-space", "nowrap")]
    [InlineData("th", "text-align", "center")]
    public void Element_HasHtmlRenderingDefault(string tag, string property, string expected)
    {
        Resolve(tag).Get(property).Should().Be(expected);
    }

    [Fact]
    public void Big_IsLargerThanParent()
    {
        var parent = new ComputedStyle();
        parent.Set("font-size", "10px");

        _resolver.Resolve(new HtmlElement("big"), parent).FontSize.Should().Be("12px");
    }

    [Fact]
    public async Task DefinitionList_TermsAndDescriptionsOnOwnLinesWithIndent()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body style='margin:0'><dl><dt>Invoice</dt><dd>INV-001</dd></dl></body></html>");

        var content = Encoding.ASCII.GetString(pdf);
        var term = Regex.Match(content, @"(-?[\d.]+) (-?[\d.]+) Td \(Invoice\) Tj");
        var description = Regex.Match(content, @"(-?[\d.]+) (-?[\d.]+) Td \(INV-001\) Tj");
        term.Success.Should().BeTrue();
        description.Success.Should().BeTrue();

        float termX = float.Parse(term.Groups[1].Value, CultureInfo.InvariantCulture);
        float termY = float.Parse(term.Groups[2].Value, CultureInfo.InvariantCulture);
        float descX = float.Parse(description.Groups[1].Value, CultureInfo.InvariantCulture);
        float descY = float.Parse(description.Groups[2].Value, CultureInfo.InvariantCulture);

        descY.Should().BeLessThan(termY - 5f, "the description goes on the line below the term");
        (descX - termX).Should().BeApproximately(30f, 1f, "dd is indented 40px = 30pt");
    }
}
