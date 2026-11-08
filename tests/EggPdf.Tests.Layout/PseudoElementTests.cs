using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class PseudoElementTests
{
    [Fact]
    public void Before_StringContent_RendersBeforeElementText()
    {
        var root = LayoutTestHelper.Layout(
            "<style>p::before { content: 'PRE: '; }</style>" +
            "<p>Hello</p>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        var textBoxes = p!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        textBoxes.Should().NotBeEmpty();
        textBoxes[0].Text!.Trim().Should().Be("PRE:", "::before content should appear first");
    }

    [Fact]
    public void After_StringContent_RendersAfterElementText()
    {
        var root = LayoutTestHelper.Layout(
            "<style>p::after { content: ' AFTER'; }</style>" +
            "<p>Hello</p>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        var textBoxes = p!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        textBoxes.Should().NotBeEmpty();
        textBoxes[textBoxes.Count - 1].Text!.Trim().Should().Be("AFTER", "::after content should appear last");
    }

    [Fact]
    public void Before_None_DoesNotRenderContent()
    {
        var root = LayoutTestHelper.Layout(
            "<style>p::before { content: none; }</style>" +
            "<p>Hello</p>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        // Only the actual text "Hello" should be there — no extra box from ::before
        var textBoxes = p!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        textBoxes.Should().HaveCount(1, "content:none should produce no ::before box");
        textBoxes[0].Text.Should().Be("Hello");
    }

    [Fact]
    public void CssCounter_IncrementAndReset_ProducesIncrementingPrefix()
    {
        // h2 elements increment a "section" counter; ::before shows the count
        var root = LayoutTestHelper.Layout(
            "<style>" +
            "body { counter-reset: section; }" +
            "h2 { counter-increment: section; }" +
            "h2::before { content: counter(section) '. '; }" +
            "</style>" +
            "<h2>First</h2>" +
            "<h2>Second</h2>" +
            "<h2>Third</h2>",
            600, 800);

        var headings = root.FindAllByTag("h2");
        headings.Should().HaveCount(3);

        // First h2 should have ::before text starting with "1"
        var first = headings[0].Children.Where(c => !string.IsNullOrEmpty(c.Text)).FirstOrDefault();
        first.Should().NotBeNull("first h2 should have a ::before counter text");
        first!.Text.Should().StartWith("1");
    }

    [Fact]
    public void Before_AttrContent_InjectsAttributeValue()
    {
        var root = LayoutTestHelper.Layout(
            "<style>a::before { content: '[' attr(href) '] '; }</style>" +
            "<a href='https://example.com'>Example</a>", 600, 800);

        var a = root.FindByTag("a");
        a.Should().NotBeNull();

        // For inline elements the ::before box is the first LayoutBox tagged with the element.
        // Its Text holds the resolved content (attr() value).
        a!.Text.Should().Contain("https://example.com",
            "attr(href) should inject the href attribute value");
    }

    [Fact]
    public void FirstLine_StyleAppliedToFirstTextBox()
    {
        // Wrap a long paragraph so it produces at least 2 lines.
        // ::first-line { color: red } should make the first text box red.
        var root = LayoutTestHelper.Layout(
            "<style>p::first-line { color: red; }</style>" +
            "<p>The quick brown fox jumps over the lazy dog and keeps on running through the forest</p>",
            200, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        var textBoxes = p!.Children.FindAll(c => !string.IsNullOrEmpty(c.Text));
        textBoxes.Should().HaveCountGreaterThan(1, "long paragraph should wrap to multiple lines at 200px width");

        // First line box should have color:red from ::first-line
        textBoxes[0].Style.Get("color").Should().Be("red",
            "::first-line style should be applied to the first line text box");

        // Subsequent line boxes should NOT have color:red (use element's own style)
        textBoxes[1].Style.Get("color").Should().NotBe("red",
            "::first-line style must not bleed to second line");
    }

    [Fact]
    public void FirstLetter_StyleAppliedToFirstCharBox()
    {
        // ::first-letter { font-size: 32px } creates a drop-cap style.
        // The first character should be in its own text box with the overridden font-size.
        var root = LayoutTestHelper.Layout(
            "<style>p::first-letter { font-size: 32px; }</style>" +
            "<p>Hello world</p>",
            400, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        var textBoxes = p!.Children.FindAll(c => !string.IsNullOrEmpty(c.Text));
        textBoxes.Should().NotBeEmpty();

        // First box should contain only the first character
        textBoxes[0].Text.Should().Be("H",
            "::first-letter box should contain only the first character");

        // First box should use the first-letter font-size
        textBoxes[0].Style.FontSize.Should().Be("32px",
            "::first-letter style should be applied to the first character box");
    }

    [Fact]
    public void Marker_CustomContent_ReplacesDefaultBullet()
    {
        // li::marker { content: '>> '; } should replace the default bullet with '>>'
        var root = LayoutTestHelper.Layout(
            "<style>li::marker { content: '>> '; }</style>" +
            "<ul><li>Item one</li><li>Item two</li></ul>",
            400, 800);

        var listItems = root.FindAllByTag("li");
        listItems.Should().HaveCountGreaterOrEqualTo(1);

        var firstItem = listItems[0];
        var markerBox = firstItem.Children.Find(c => c.IsListMarker);
        markerBox.Should().NotBeNull("li should have a marker box");
        markerBox!.Text.Should().Be(">> ", "::marker content should replace the default bullet");
    }

    [Fact]
    public void Marker_CustomColor_AppliedToMarkerBox()
    {
        // li::marker { color: red; } should apply red to the marker box style
        var root = LayoutTestHelper.Layout(
            "<style>li::marker { color: red; }</style>" +
            "<ul><li>Item</li></ul>",
            400, 800);

        var li = root.FindByTag("li");
        li.Should().NotBeNull();

        var markerBox = li!.Children.Find(c => c.IsListMarker);
        markerBox.Should().NotBeNull("li should have a marker box");
        markerBox!.Style.Get("color").Should().Be("red",
            "::marker color should be applied to the marker box");
    }

    [Fact]
    public void Marker_NoRule_DefaultBulletStillRenders()
    {
        // Without any ::marker rule, the default disc bullet should still appear
        var root = LayoutTestHelper.Layout("<ul><li>Item</li></ul>", 400, 800);

        var li = root.FindByTag("li");
        li.Should().NotBeNull();

        var markerBox = li!.Children.Find(c => c.IsListMarker);
        markerBox.Should().NotBeNull("default marker should exist");
        markerBox!.Text.Should().NotBeEmpty("default marker should have text");
    }

    [Fact]
    public void FirstLine_SingleLineParagraph_TakesTheFirstLineStyle()
    {
        var root = LayoutTestHelper.Layout(
            "<style>p::first-line { color: blue; font-weight: bold; }</style>" +
            "<p>Simple paragraph text</p>", 400, 800);

        var textBoxes = root.FindByTag("p")!.Children.FindAll(c => !string.IsNullOrEmpty(c.Text));
        textBoxes.Should().NotBeEmpty();
        textBoxes.Should().OnlyContain(c => c.Style.Get("color") == "blue" && c.Style.FontWeight == "bold",
            "the paragraph's only line is its first line");
    }

    [Fact]
    public void FirstLetter_DropCapParagraph_SplitsOffAStyledFirstCharacter()
    {
        var root = LayoutTestHelper.Layout(
            "<style>p::first-letter { font-size: 24px; color: navy; }</style>" +
            "<p>Drop cap paragraph.</p>", 400, 800);

        var textBoxes = root.FindByTag("p")!.Children.FindAll(c => !string.IsNullOrEmpty(c.Text));
        textBoxes[0].Text.Should().Be("D");
        textBoxes[0].Style.FontSize.Should().Be("24px");
        textBoxes[0].Style.Get("color").Should().Be("navy");
        textBoxes[1].Style.Get("color").Should().NotBe("navy", "only the first letter is styled");
    }

    // ── quotes property ──────────────────────────────────────────────────────

    [Fact]
    public void Quotes_OpenQuote_DefaultsToSmartQuote()
    {
        // Default open-quote should be U+201C "
        var root = LayoutTestHelper.Layout(
            "<style>q::before { content: open-quote; } q::after { content: close-quote; }</style>" +
            "<p><q>hello</q></p>", 400, 600);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        var allText = string.Concat(p!.Children
            .SelectMany(b => new[] { b }.Concat(b.Children))
            .Where(b => !string.IsNullOrEmpty(b.Text))
            .Select(b => b.Text));
        allText.Should().Contain("\u201C",
            "default open-quote should be the left double quotation mark");
    }

    [Fact]
    public void Quotes_CustomCharacters_Applied()
    {
        // quotes: "«" "»" should use guillemets instead of curly quotes
        var root = LayoutTestHelper.Layout(
            "<style>" +
            "q { quotes: '«' '»'; }" +
            "q::before { content: open-quote; }" +
            "q::after  { content: close-quote; }" +
            "</style>" +
            "<p><q>bonjour</q></p>", 400, 600);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        var allText = string.Concat(p!.Children
            .SelectMany(b => new[] { b }.Concat(b.Children))
            .Where(b => !string.IsNullOrEmpty(b.Text))
            .Select(b => b.Text));
        allText.Should().Contain("«",
            "open-quote should use the custom quote character from the quotes property");
        allText.Should().Contain("»",
            "close-quote should use the custom quote character from the quotes property");
    }

    private static LayoutBox LayoutWithStyle(string css, string body) =>
        LayoutTestHelper.Layout("<html><head><style>body{margin:0}" + css + "</style></head><body>" + body + "</body></html>", 600, 800);

    private static LayoutBox TextBox(LayoutBox root, string text) =>
        root.FindAll(b => b.Text != null && b.Text.Trim() == text).First();

    [Fact]
    public void BlockBefore_InlineByDefault_SharesTheLineWithTheText()
    {
        var root = LayoutWithStyle("p::before{content:'Note:'}", "<p>Hello world</p>");

        var note = TextBox(root, "Note:");
        var hello = TextBox(root, "Hello");
        hello.Y.Should().BeApproximately(note.Y, 0.5f);
        hello.X.Should().BeGreaterThan(note.X);
    }

    [Fact]
    public void BlockAfter_InlineByDefault_FollowsTheLastWordOnItsLine()
    {
        var root = LayoutWithStyle("p::after{content:'!'}", "<p>Hello world</p>");

        var world = TextBox(root, "world");
        var bang = TextBox(root, "!");
        bang.Y.Should().BeApproximately(world.Y, 0.5f);
        bang.X.Should().BeGreaterThanOrEqualTo(world.X + world.ContentWidth - 0.5f);
    }

    [Theory]
    [InlineData("p::after{content:' [end]'}", "<p>Important note</p>", "note", "[end]")]
    [InlineData("b::after{content:' x'}", "<p><b>bold</b></p>", "bold", "x")]
    public void InlineAfter_LeadingSpaceInContent_KeepsAGap(string css, string body, string previousWord, string afterWord)
    {
        var root = LayoutWithStyle(css, body);

        var previous = TextBox(root, previousWord);
        var after = TextBox(root, afterWord);
        bool separated = after.Text!.StartsWith(" ") || after.X > previous.X + previous.ContentWidth + 1;
        separated.Should().BeTrue("the leading space in the content must separate it from the text");
    }

    [Fact]
    public void BlockAfter_AfterInlineElement_StaysOnTheSameLine()
    {
        var root = LayoutWithStyle("p::after{content:'!'}", "<p>Hi <b>there</b></p>");

        TextBox(root, "!").Y.Should().BeApproximately(TextBox(root, "there").Y, 0.5f);
    }

    [Fact]
    public void ListItemBefore_CustomBullet_SharesTheLineWithTheItemText()
    {
        var root = LayoutWithStyle("ul{list-style:none;padding:0} li::before{content:'• '}", "<ul><li>Item one</li></ul>");

        var bullet = TextBox(root, "•");
        var item = TextBox(root, "Item");
        item.Y.Should().BeApproximately(bullet.Y, 0.5f);
        item.X.Should().BeGreaterThan(bullet.X);
    }

    [Fact]
    public void BlockBefore_DisplayBlock_StaysOnItsOwnLine()
    {
        var root = LayoutWithStyle("p::before{content:'Title';display:block}", "<p>Body</p>");

        TextBox(root, "Body").Y.Should().BeGreaterThan(TextBox(root, "Title").Y + 1);
    }

    [Fact]
    public void BlockBefore_CenteredText_CentersTheWholeLine()
    {
        var root = LayoutWithStyle("p{text-align:center;width:600px} p::before{content:'>> '}", "<p>Hi</p>");

        var marker = TextBox(root, ">>");
        var hi = TextBox(root, "Hi");
        float left = marker.X;
        float right = hi.X + hi.ContentWidth;
        (left - 0).Should().BeApproximately(600 - right, 1.5f, "the line containing ::before and the text is centered as one");
    }

    [Fact]
    public void BlockAfter_EmptyContent_AddsNoHeight()
    {
        var plain = LayoutWithStyle("", "<p id='p'>Hello</p>").FindById("p")!;
        var withAfter = LayoutWithStyle("p::after{content:''}", "<p id='p'>Hello</p>").FindById("p")!;

        withAfter.Height.Should().BeApproximately(plain.Height, 0.5f);
    }

    [Fact]
    public void BlockBefore_WithOnlyBlockChildren_FormsItsOwnLineAboveThem()
    {
        var root = LayoutWithStyle("div::before{content:'Head'}", "<div><p style='margin:0'>Para</p></div>");

        TextBox(root, "Para").Y.Should().BeGreaterThan(TextBox(root, "Head").Y + 1);
    }
}
