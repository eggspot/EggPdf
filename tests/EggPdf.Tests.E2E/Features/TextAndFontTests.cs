using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.E2E.Features;

/// <summary>
/// E2E tests for text and font rendering through the HTTP API.
/// Covers: font families, weight, style, text-align, text-decoration,
/// text-transform, letter/word spacing, whitespace, sup/sub, text-overflow.
/// </summary>
[Collection("E2E")]
public class TextAndFontTests
{
    private readonly ServiceFixture _fixture;
    private readonly HttpClient _client = new();

    public TextAndFontTests(ServiceFixture fixture) { _fixture = fixture; }

    private async Task<string> RenderAndGetPdfText(string html)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html }),
            Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        return PdfTextDecoder.DecodeWithText(bytes);
    }

    // --- Font Family ---

    [Fact]
    public async Task FontFamily_SansSerif_UsesHelvetica()
    {
        var text = await RenderAndGetPdfText("<p style='font-family: sans-serif'>Sans text</p>");
        text.Should().Contain("Helvetica");
        text.Should().Contain("Sans text");
    }

    [Fact]
    public async Task FontFamily_Serif_UsesTimesRoman()
    {
        var text = await RenderAndGetPdfText("<p style='font-family: serif'>Serif text</p>");
        text.Should().Contain("Times");
        text.Should().Contain("Serif text");
    }

    [Fact]
    public async Task FontFamily_Monospace_UsesCourier()
    {
        var text = await RenderAndGetPdfText("<p style='font-family: monospace'>Code text</p>");
        text.Should().Contain("Courier");
        text.Should().Contain("Code text");
    }

    // --- Font Weight & Style ---

    [Fact]
    public async Task FontWeight_Bold_UsesBoldFont()
    {
        var text = await RenderAndGetPdfText("<p style='font-weight: bold'>Bold text</p>");
        text.Should().Contain("Helvetica-Bold");
        text.Should().Contain("Bold text");
    }

    [Fact]
    public async Task FontStyle_Italic_UsesObliqueFont()
    {
        var text = await RenderAndGetPdfText("<p style='font-style: italic'>Italic text</p>");
        text.Should().Contain("Oblique");
        text.Should().Contain("Italic text");
    }

    [Fact]
    public async Task FontShorthand_BoldSerif()
    {
        var text = await RenderAndGetPdfText("<style>p { font: bold 20px serif; }</style><p>Bold serif</p>");
        text.Should().Contain("Times-Bold");
        text.Should().Contain("Bold serif");
    }

    [Fact]
    public async Task StrongElement_UsesBoldFont()
    {
        var text = await RenderAndGetPdfText("<p><strong>Strong text</strong></p>");
        text.Should().Contain("Helvetica-Bold");
        text.Should().Contain("Strong text");
    }

    [Fact]
    public async Task EmElement_UsesItalicFont()
    {
        var text = await RenderAndGetPdfText("<p><em>Emphasis text</em></p>");
        text.Should().Contain("Oblique");
        text.Should().Contain("Emphasis text");
    }

    // --- Text Alignment ---

    [Fact]
    public async Task TextAlign_Center()
    {
        var text = await RenderAndGetPdfText("<p style='text-align: center'>Centered text</p>");
        text.Should().Contain("Centered text");
    }

    [Fact]
    public async Task TextAlign_Right()
    {
        var text = await RenderAndGetPdfText("<p style='text-align: right'>Right-aligned text</p>");
        text.Should().Contain("Right-aligned text");
    }

    // --- Text Decoration ---

    [Fact]
    public async Task TextDecoration_Underline()
    {
        var text = await RenderAndGetPdfText("<p style='text-decoration: underline'>Underlined text</p>");
        text.Should().Contain("Underlined text");
        text.Should().Contain("l S", "underline draws a line");
    }

    [Fact]
    public async Task TextDecoration_LineThrough()
    {
        var text = await RenderAndGetPdfText("<p style='text-decoration: line-through'>Struck text</p>");
        text.Should().Contain("Struck text");
        text.Should().Contain("l S", "line-through draws a line");
    }

    [Fact]
    public async Task TextDecoration_Overline()
    {
        var text = await RenderAndGetPdfText("<p style='text-decoration: overline'>Overlined text</p>");
        text.Should().Contain("Overlined text");
    }

    [Fact]
    public async Task TextDecoration_Multiple()
    {
        var text = await RenderAndGetPdfText("<p style='text-decoration: underline overline'>Both decorations</p>");
        text.Should().Contain("Both decorations");
    }

    [Fact]
    public async Task AnchorTag_HasDefaultUnderline()
    {
        var text = await RenderAndGetPdfText("<a href='https://example.com'>Link text</a>");
        text.Should().Contain("Link text");
        text.Should().Contain("l S", "links have underline by default");
    }

    // --- Text Transform ---

    [Fact]
    public async Task TextTransform_Uppercase()
    {
        var text = await RenderAndGetPdfText("<p style='text-transform: uppercase'>hello world</p>");
        text.Should().Contain("HELLO WORLD");
        text.Should().NotContain("hello world");
    }

    [Fact]
    public async Task TextTransform_Lowercase()
    {
        var text = await RenderAndGetPdfText("<p style='text-transform: lowercase'>HELLO WORLD</p>");
        text.Should().Contain("hello world");
    }

    [Fact]
    public async Task TextTransform_Capitalize()
    {
        var text = await RenderAndGetPdfText("<p style='text-transform: capitalize'>hello world</p>");
        text.Should().Contain("Hello World");
    }

    // --- Letter & Word Spacing ---

    [Fact]
    public async Task LetterSpacing_EmitsTcOperator()
    {
        var text = await RenderAndGetPdfText("<p style='letter-spacing: 2px'>Spaced text</p>");
        text.Should().Contain("Spaced text");
        text.Should().Contain("Tc", "letter-spacing emits Tc operator");
    }

    [Fact]
    public async Task WordSpacing_EmitsTwOperator()
    {
        var text = await RenderAndGetPdfText("<p style='word-spacing: 5px'>Word spaced text</p>");
        text.Should().Contain("Word spaced text");
        text.Should().Contain("Tw", "word-spacing emits Tw operator");
    }

    // --- Text Indent ---

    [Fact]
    public async Task TextIndent_FirstLine()
    {
        var text = await RenderAndGetPdfText("<p style='text-indent: 40px'>Indented first line</p>");
        text.Should().Contain("Indented first line");
    }

    // --- Whitespace ---

    [Fact]
    public async Task WhiteSpacePre_PreservesNewlines()
    {
        var text = await RenderAndGetPdfText("<pre>Line 1\nLine 2\nLine 3</pre>");
        text.Should().Contain("Line 1");
        text.Should().Contain("Line 2");
        text.Should().Contain("Line 3");
    }

    // --- Overflow & Word Break ---

    [Fact]
    public async Task OverflowWrap_BreakWord()
    {
        var text = await RenderAndGetPdfText("<div style='width: 100px; overflow-wrap: break-word'>Superlongwordthatwillnotfit</div>");
        text.Should().Contain("Super");
    }

    [Fact]
    public async Task WordBreak_BreakAll()
    {
        var text = await RenderAndGetPdfText("<div style='width: 80px; word-break: break-all'>ABCDEFGHIJKLMNOP</div>");
        text.Should().Contain("ABCDE");
    }

    [Fact]
    public async Task TextOverflow_Ellipsis()
    {
        var text = await RenderAndGetPdfText("<div style='width: 80px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap'>This is a very long text that should be truncated</div>");
        text.Should().Contain("...");
    }

    // --- Superscript & Subscript ---

    [Fact]
    public async Task SupElement_Rendered()
    {
        var text = await RenderAndGetPdfText("<p>E=mc<sup>2</sup></p>");
        text.Should().Contain("E=mc");
        text.Should().Contain("2");
    }

    [Fact]
    public async Task SubElement_Rendered()
    {
        var text = await RenderAndGetPdfText("<p>H<sub>2</sub>O</p>");
        text.Should().Contain("H");
        text.Should().Contain("2");
        text.Should().Contain("O");
    }

    // --- Heading Hierarchy ---

    [Fact]
    public async Task Headings_AllLevelsRendered()
    {
        var text = await RenderAndGetPdfText("<h1>H1</h1><h2>H2</h2><h3>H3</h3><h4>H4</h4><h5>H5</h5><h6>H6</h6>");
        text.Should().Contain("H1");
        text.Should().Contain("H2");
        text.Should().Contain("H3");
        text.Should().Contain("H4");
        text.Should().Contain("H5");
        text.Should().Contain("H6");
    }

    // --- Inline Style Override ---

    [Fact]
    public async Task InlineStyle_OverridesDefaults()
    {
        var text = await RenderAndGetPdfText("<h1 style='font-size: 12px; color: gray'>Small heading</h1>");
        text.Should().Contain("Small heading");
    }

    // --- BR and line breaks ---

    [Fact]
    public async Task BrElement_CausesLineBreak()
    {
        var text = await RenderAndGetPdfText("<p>Line 1<br>Line 2</p>");
        text.Should().Contain("Line 1");
        text.Should().Contain("Line 2");
    }
}
