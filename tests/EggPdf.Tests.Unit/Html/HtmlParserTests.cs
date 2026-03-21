using EggPdf.Html;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Html;

public class HtmlParserTests
{
    private static HtmlDocument Parse(string html) => HtmlParser.Parse(html);

    [Fact]
    public void EmptyString_ProducesValidDocument()
    {
        var doc = Parse("");

        doc.Should().NotBeNull();
        doc.DocumentElement.Should().NotBeNull();
        doc.DocumentElement!.TagName.Should().Be("html");
        doc.Head.Should().NotBeNull();
        doc.Body.Should().NotBeNull();
    }

    [Fact]
    public void SimpleHtml_CreatesCorrectStructure()
    {
        var doc = Parse("<html><head></head><body><p>Hello</p></body></html>");

        doc.DocumentElement!.TagName.Should().Be("html");
        doc.Head!.TagName.Should().Be("head");
        doc.Body!.TagName.Should().Be("body");
        doc.Body.ChildNodes.Should().HaveCount(1);

        var p = doc.Body.ChildNodes[0] as HtmlElement;
        p.Should().NotBeNull();
        p!.TagName.Should().Be("p");
        p.ChildNodes.Should().HaveCount(1);

        var text = p.ChildNodes[0] as HtmlTextNode;
        text.Should().NotBeNull();
        text!.Data.Should().Be("Hello");
    }

    [Fact]
    public void MissingHtmlHeadBody_ImplicitlyCreated()
    {
        var doc = Parse("<p>Hello</p>");

        doc.DocumentElement.Should().NotBeNull();
        doc.Head.Should().NotBeNull();
        doc.Body.Should().NotBeNull();

        // <p> should be inside <body>
        doc.Body!.ChildNodes.OfType<HtmlElement>().Should().Contain(e => e.TagName == "p");
    }

    [Fact]
    public void NestedDivs_CorrectHierarchy()
    {
        var doc = Parse("<div><div><span>inner</span></div></div>");

        var body = doc.Body!;
        var outerDiv = body.ChildNodes[0] as HtmlElement;
        outerDiv!.TagName.Should().Be("div");

        var innerDiv = outerDiv.ChildNodes[0] as HtmlElement;
        innerDiv!.TagName.Should().Be("div");

        var span = innerDiv.ChildNodes[0] as HtmlElement;
        span!.TagName.Should().Be("span");

        var text = span.ChildNodes[0] as HtmlTextNode;
        text!.Data.Should().Be("inner");
    }

    [Fact]
    public void VoidElements_NoChildren()
    {
        var doc = Parse("<br><hr><img src='test.png'>");

        var body = doc.Body!;
        body.ChildNodes.Should().HaveCount(3);

        foreach (var child in body.ChildNodes)
        {
            var elem = child as HtmlElement;
            elem.Should().NotBeNull();
            elem!.ChildNodes.Should().BeEmpty();
        }
    }

    [Fact]
    public void ImgAttributes_Preserved()
    {
        var doc = Parse("<img src='logo.png' alt='Logo' width='100'>");

        var body = doc.Body!;
        var img = body.ChildNodes[0] as HtmlElement;
        img!.TagName.Should().Be("img");
        img.GetAttribute("src").Should().Be("logo.png");
        img.GetAttribute("alt").Should().Be("Logo");
        img.GetAttribute("width").Should().Be("100");
    }

    [Fact]
    public void ParagraphsImplicitlyClose()
    {
        // <p> is implicitly closed by another <p>
        var doc = Parse("<p>First<p>Second");

        var body = doc.Body!;
        var paragraphs = body.ChildNodes.OfType<HtmlElement>().Where(e => e.TagName == "p").ToList();
        paragraphs.Should().HaveCount(2);
        (paragraphs[0].ChildNodes[0] as HtmlTextNode)!.Data.Should().Be("First");
        (paragraphs[1].ChildNodes[0] as HtmlTextNode)!.Data.Should().Be("Second");
    }

    [Fact]
    public void Table_BasicStructure()
    {
        var doc = Parse("<table><tr><td>Cell</td></tr></table>");

        var body = doc.Body!;
        var table = body.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "table");
        table.Should().NotBeNull();

        // Should have tbody (implicitly created) containing tr
        var tbody = table.ChildNodes.OfType<HtmlElement>().FirstOrDefault(e => e.TagName == "tbody");
        tbody.Should().NotBeNull();

        var tr = tbody!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "tr");
        var td = tr.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "td");
        (td.ChildNodes[0] as HtmlTextNode)!.Data.Should().Be("Cell");
    }

    [Fact]
    public void StyleTag_ContainsRawText()
    {
        var doc = Parse("<style>body { color: red; }</style>");

        var head = doc.Head!;
        var style = head.ChildNodes.OfType<HtmlElement>().FirstOrDefault(e => e.TagName == "style");
        style.Should().NotBeNull();

        var text = style!.ChildNodes[0] as HtmlTextNode;
        text!.Data.Should().Be("body { color: red; }");
    }

    [Fact]
    public void TitleTag_ContainsText()
    {
        var doc = Parse("<title>My Page</title>");

        var head = doc.Head!;
        var title = head.ChildNodes.OfType<HtmlElement>().FirstOrDefault(e => e.TagName == "title");
        title.Should().NotBeNull();

        var text = title!.ChildNodes[0] as HtmlTextNode;
        text!.Data.Should().Be("My Page");
    }

    [Fact]
    public void Doctype_Recognized()
    {
        var doc = Parse("<!DOCTYPE html><html><body></body></html>");

        doc.DocumentElement.Should().NotBeNull();
    }

    [Fact]
    public void Comment_PreservedInDom()
    {
        var doc = Parse("<body><!-- hello --><p>text</p></body>");

        var body = doc.Body!;
        body.ChildNodes.OfType<HtmlComment>().Should().NotBeEmpty();
    }

    [Fact]
    public void HiddenAttribute_Preserved()
    {
        var doc = Parse("<div hidden>secret</div>");

        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First();
        div.HasAttribute("hidden").Should().BeTrue();
    }

    [Fact]
    public void NoscriptContent_Present()
    {
        // We don't execute JS, so noscript content should be in the DOM
        var doc = Parse("<body><noscript>JS disabled</noscript></body>");

        var body = doc.Body!;
        var noscript = body.ChildNodes.OfType<HtmlElement>().FirstOrDefault(e => e.TagName == "noscript");
        noscript.Should().NotBeNull();
    }

    [Fact]
    public void IdAndClassAccessors_Work()
    {
        var doc = Parse("<div id='main' class='container flex'></div>");

        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First();
        div.Id.Should().Be("main");
        div.ClassList.Should().BeEquivalentTo(new[] { "container", "flex" });
    }

    [Fact]
    public void NeverThrows_OnMalformedInput()
    {
        // The parser must be infallible
        var malformedInputs = new[]
        {
            "<",
            "<<>>",
            "<//>",
            "<div><span></div></span>",
            "<p><p><p>",
            "<<<>>><<<",
            "&;&#;&#x;",
            "<div attr='unclosed",
            "<div attr=\"unclosed",
            "",
            "   ",
            "<script><div></div></script>",
            "<!-broken comment-->",
            "<!DOCTYPE>",
        };

        foreach (var input in malformedInputs)
        {
            var act = () => Parse(input);
            act.Should().NotThrow($"input '{input}' should not cause an exception");
        }
    }
}
