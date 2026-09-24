using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>HTML <c>&lt;a href="#id"&gt;</c>: a clickable link whose /Dest points at the page/position of the element with that id.</summary>
public class InternalLinkE2ETests
{
    private const string Css = "@page { size: 400px 800px; margin: 0 } body { margin: 0 } .filler { height: 1650px }";

    private static List<int> PageObjectNumbers(string pdf)
    {
        var kids = Regex.Match(pdf, @"/Kids \[([^\]]+)\]").Groups[1].Value;
        var result = new List<int>();
        foreach (Match m in Regex.Matches(kids, @"(\d+) 0 R"))
            result.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        return result;
    }

    private const string LinkAnnot = @"/Subtype /Link /Rect \[[^\]]+\] /Border \[0 0 0\] /Dest \[(\d+) 0 R /XYZ 0 ([\d.]+) 0\]";

    [Fact]
    public async Task InternalLink_ToElementOnLaterPage_EmitsDestPointingAtThatPage()
    {
        var html = "<html><head><style>" + Css + "</style></head><body>" +
                   "<a href='#target'>Jump</a><div class='filler'></div><div id='target'>Target</div></body></html>";

        var pdf = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(html));

        var pages = PageObjectNumbers(pdf);
        pages.Count.Should().Be(3);
        var m = Regex.Match(pdf, LinkAnnot);
        m.Success.Should().BeTrue("the #target link must become a /Dest annotation, not a URI action");
        int destPage = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        destPage.Should().Be(pages[2], "the target div sits after 1650px of filler, on the third page");
        pdf.Should().NotContain("/S /URI");
    }

    [Fact]
    public async Task InternalLink_TopOfDestination_IsWithinThePageHeight()
    {
        var html = "<html><head><style>" + Css + "</style></head><body>" +
                   "<a href='#t'>Jump</a><div class='filler'></div><div id='t'>Target</div></body></html>";

        var pdf = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(html));

        float top = float.Parse(Regex.Match(pdf, LinkAnnot).Groups[2].Value, CultureInfo.InvariantCulture);
        top.Should().BeInRange(0f, 600f, "an XYZ top must be a position on the 600pt-tall (800px) page");
    }

    [Fact]
    public async Task InternalLink_ToSamePage_AlsoResolves()
    {
        var html = "<html><body><a href='#here'>Go</a><p id='here'>Here</p></body></html>";

        var pdf = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(html));

        Regex.IsMatch(pdf, LinkAnnot).Should().BeTrue();
    }

    [Fact]
    public async Task InternalLink_ToMissingId_IsDroppedNotWrittenDead()
    {
        var html = "<html><body><a href='#nope'>Broken</a><p id='other'>x</p></body></html>";

        var pdf = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(html));

        pdf.Should().NotContain("/Subtype /Link");
        pdf.Should().Contain("Broken", "the link text still renders");
    }

    [Fact]
    public async Task InternalLink_DuplicateIds_FirstOneWins()
    {
        var html = "<html><head><style>" + Css + "</style></head><body>" +
                   "<div id='dup'>First</div><div class='filler'></div><div id='dup'>Second</div>" +
                   "<a href='#dup'>Jump</a></body></html>";

        var pdf = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(html));

        int destPage = int.Parse(Regex.Match(pdf, LinkAnnot).Groups[1].Value, CultureInfo.InvariantCulture);
        destPage.Should().Be(PageObjectNumbers(pdf)[0], "browsers resolve #id to the first element carrying it");
    }

    [Fact]
    public async Task InternalLink_PercentEncodedFragment_MatchesTheDecodedId()
    {
        var html = "<html><body><a href='#sec%20one'>Go</a><a href='#%C3%A9t%C3%A9'>Summer</a>" +
                   "<p id='sec one'>A</p><p id='&eacute;t&eacute;'>B</p></body></html>";

        var pdf = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(html));

        Regex.Matches(pdf, LinkAnnot).Count.Should().Be(2, "browsers percent-decode the fragment before matching ids");
    }

    [Fact]
    public async Task InternalLink_ToVisibilityHiddenElement_StillResolves()
    {
        var html = "<html><body><a href='#ghost'>Go</a><div id='ghost' style='visibility:hidden'>Hidden but present</div></body></html>";

        var pdf = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(html));

        Regex.IsMatch(pdf, LinkAnnot).Should().BeTrue("a browser still scrolls to a visibility:hidden element");
    }

    [Theory]
    [InlineData("#")]
    [InlineData("#top")]
    [InlineData("#TOP")]
    public async Task InternalLink_ToTopWithoutMatchingId_JumpsToTheFirstPage(string href)
    {
        var html = "<html><head><style>" + Css + "</style></head><body><div class='filler'></div><a href='" + href + "'>Back to top</a></body></html>";

        var pdf = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(html));

        var m = Regex.Match(pdf, @"/Subtype /Link /Rect \[[^\]]+\] /Border \[0 0 0\] /Dest \[(\d+) 0 R /Fit\]");
        m.Success.Should().BeTrue("'#' / '#top' scroll to the top of the document when no element has that id");
        int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture).Should().Be(PageObjectNumbers(pdf)[0]);
    }

    [Fact]
    public async Task InternalLink_TopWithARealTopId_PrefersTheElement()
    {
        var html = "<html><head><style>" + Css + "</style></head><body><a href='#top'>Go</a><div class='filler'></div><div id='top'>Real</div></body></html>";

        var pdf = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(html));

        int destPage = int.Parse(Regex.Match(pdf, LinkAnnot).Groups[1].Value, CultureInfo.InvariantCulture);
        destPage.Should().Be(PageObjectNumbers(pdf)[2], "an element with id=top wins over the implicit document top");
    }

    [Fact]
    public async Task ExternalLink_StillUsesUriAction()
    {
        var pdf = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(
            "<html><body><a href='https://example.com'>Web</a></body></html>"));

        pdf.Should().Contain("/S /URI /URI (https://example.com)");
        pdf.Should().NotContain("/Dest");
    }

    [Fact]
    public async Task InternalLink_InTaggedPdf_StillEmitsDest()
    {
        var html = "<html><body><a href='#t'>Jump</a><p id='t'>Target</p></body></html>";

        var pdf = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(html, tagged: true));

        Regex.IsMatch(pdf, @"/Subtype /Link[^>]*/Dest \[\d+ 0 R /XYZ").Should().BeTrue();
        pdf.Should().Contain("/Type /StructTreeRoot");
    }
}
