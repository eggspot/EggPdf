using System.Text;
using System.Threading.Tasks;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit;

/// <summary>
/// Tests for HtmlToPdf.RenderDocument(HtmlDocument): the DOM-direct entry point used by document
/// builders (e.g. EggPdf.Fluent) that construct the DOM themselves instead of an HTML string.
/// </summary>
public class HtmlToPdfDomEntryPointTests
{
    private static HtmlDocument BuildDocument(HtmlElement bodyContent)
    {
        var document = new HtmlDocument();
        var html = new HtmlElement("html");
        var head = new HtmlElement("head");
        var body = new HtmlElement("body");
        body.AppendChild(bodyContent);
        html.AppendChild(head);
        html.AppendChild(body);
        document.AppendChild(html);
        return document;
    }

    [Fact]
    public void Render_FromDom_ProducesValidPdfWithText()
    {
        var p = new HtmlElement("p");
        p.AppendChild(new HtmlTextNode("Hello from the DOM"));
        var document = BuildDocument(p);

        byte[] pdf = HtmlToPdf.RenderDocument(document);
        var text = Encoding.Latin1.GetString(pdf);

        text.Should().StartWith("%PDF-");
        text.Should().Contain("Hello from the DOM");
    }

    [Fact]
    public async Task RenderAsync_FromDom_ProducesValidPdf()
    {
        var p = new HtmlElement("p");
        p.AppendChild(new HtmlTextNode("Async DOM render"));
        var document = BuildDocument(p);

        byte[] pdf = await HtmlToPdf.RenderDocumentAsync(document);
        Encoding.Latin1.GetString(pdf).Should().Contain("Async DOM render");
    }

    [Fact]
    public void Render_FromDom_MatchesHtmlStringPipeline()
    {
        var p = new HtmlElement("p");
        p.SetAttribute("style", "color: red");
        p.AppendChild(new HtmlTextNode("Parity check"));
        var domDoc = BuildDocument(p);

        byte[] fromDom = HtmlToPdf.RenderDocument(domDoc);
        byte[] fromHtml = HtmlToPdf.Render("<html><head></head><body><p style=\"color: red\">Parity check</p></body></html>");

        var domText = Encoding.Latin1.GetString(fromDom);
        var htmlText = Encoding.Latin1.GetString(fromHtml);

        domText.Should().Contain("Parity check");
        htmlText.Should().Contain("Parity check");
        // same layout engine, same input tree -> same page geometry
        System.Text.RegularExpressions.Regex.Match(domText, @"/MediaBox \[[^\]]+\]").Value
            .Should().Be(System.Text.RegularExpressions.Regex.Match(htmlText, @"/MediaBox \[[^\]]+\]").Value);
    }

    [Fact]
    public void UsesFontVariations_DetectsInlineFontStretchNotCapturedByStylesheets()
    {
        var div = new HtmlElement("div");
        div.SetAttribute("style", "font-stretch: condensed");
        div.AppendChild(new HtmlTextNode("x"));
        var document = BuildDocument(div);

        HtmlToPdf.UsesFontVariations(document, null).Should().BeTrue();
    }

    [Fact]
    public void UsesFontVariations_DetectsMentionInsideStyleElementText_EvenInsideAtRules()
    {
        var style = new HtmlElement("style");
        style.AppendChild(new HtmlTextNode("@supports (display:flex) { p { Font-Stretch: 75% } }"));
        var document = BuildDocument(style);

        HtmlToPdf.UsesFontVariations(document, null).Should().BeTrue();
    }

    [Fact]
    public void UsesFontVariations_DetectsSvgPresentationAttribute()
    {
        var text = new HtmlElement("text");
        text.SetAttribute("font-stretch", "condensed");
        text.AppendChild(new HtmlTextNode("x"));
        var svg = new HtmlElement("svg");
        svg.AppendChild(text);

        HtmlToPdf.UsesFontVariations(BuildDocument(svg), null).Should().BeTrue();
    }

    [Fact]
    public void UsesFontVariations_VeryDeepNesting_DoesNotOverflowTheStack()
    {
        var root = new HtmlElement("div");
        var current = root;
        for (int i = 0; i < 100_000; i++)
        {
            var child = new HtmlElement("div");
            current.AppendChild(child);
            current = child;
        }
        current.SetAttribute("style", "font-stretch: condensed");

        HtmlToPdf.UsesFontVariations(BuildDocument(root), null).Should().BeTrue();
    }

    [Fact]
    public void UsesFontVariations_FalseForOrdinaryInlineStyle()
    {
        var div = new HtmlElement("div");
        div.SetAttribute("style", "color: blue; font-weight: bold");
        div.AppendChild(new HtmlTextNode("x"));
        var document = BuildDocument(div);

        HtmlToPdf.UsesFontVariations(document, null).Should().BeFalse();
    }
}
