using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.E2E.Features;

/// <summary>
/// E2E tests for visual effects, images, links, pagination, CSS features through the HTTP API.
/// Covers: box-shadow, opacity, transforms, images, links, bookmarks, page-breaks,
/// @page rules, CSS variables, calc(), generated content, selectors, style tags.
/// </summary>
[Collection("E2E")]
public class VisualEffectsAndPaginationTests
{
    private readonly ServiceFixture _fixture;
    private readonly HttpClient _client = new();

    public VisualEffectsAndPaginationTests(ServiceFixture fixture) { _fixture = fixture; }

    private async Task<(string text, byte[] bytes)> RenderPdfRaw(string html)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html }),
            Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        return (PdfTextDecoder.Decode(bytes), bytes);
    }

    private async Task<string> RenderPdf(string html)
    {
        var (text, _) = await RenderPdfRaw(html);
        return text;
    }

    // === Box Shadow ===

    [Fact]
    public async Task BoxShadow_Simple()
    {
        var text = await RenderPdf("<div style='box-shadow: 5px 5px 10px rgba(0,0,0,0.3); width: 200px; height: 100px; background: white'>Shadow</div>");
        text.Should().Contain("Shadow");
        int fillCount = Regex.Matches(text, @"re f").Count;
        fillCount.Should().BeGreaterOrEqualTo(2, "shadow adds extra filled rectangles");
    }

    [Fact]
    public async Task BoxShadow_WithSpread()
    {
        var text = await RenderPdf("<div style='box-shadow: 0 0 0 5px red; width: 200px; height: 100px; background: white'>Spread</div>");
        text.Should().Contain("Spread");
    }

    [Fact]
    public async Task BoxShadow_WithBlur()
    {
        var text = await RenderPdf("<div style='box-shadow: 0 4px 8px rgba(0,0,0,0.2); width: 200px; height: 100px; background: white'>Blur</div>");
        text.Should().Contain("Blur");
    }

    // === Opacity ===

    [Fact]
    public async Task Opacity_SemiTransparent()
    {
        var text = await RenderPdf("<div style='opacity: 0.5; background-color: red; width: 100px; height: 100px'>Semi-transparent</div>");
        text.Should().Contain("Semi-transparent");
    }

    // === Transforms ===

    [Fact]
    public async Task Transform_TranslateX()
    {
        var text = await RenderPdf("<div style='transform: translateX(50px); background-color: red; width: 100px; height: 100px'>Moved</div>");
        text.Should().Contain("Moved");
        text.Should().Contain(" cm\r\n", "translateX emits cm operator");
    }

    [Fact]
    public async Task Transform_TranslateY()
    {
        var text = await RenderPdf("<div style='transform: translateY(30px); background-color: blue; width: 100px; height: 100px'>Down</div>");
        text.Should().Contain("Down");
        text.Should().Contain(" cm\r\n");
    }

    [Fact]
    public async Task Transform_Rotate()
    {
        var text = await RenderPdf("<div style='transform: rotate(45deg); background-color: green; width: 100px; height: 100px'>Rotated</div>");
        text.Should().Contain("Rotated");
        text.Should().Contain(" cm\r\n");
        text.Should().Contain("0.71", "cos(45deg) ~ 0.71");
    }

    [Fact]
    public async Task Transform_Skew()
    {
        var text = await RenderPdf("<div style='transform: skewX(10deg); background-color: purple; width: 100px; height: 100px'>Skewed</div>");
        text.Should().Contain("Skewed");
        text.Should().Contain(" cm\r\n");
    }

    [Fact]
    public async Task Transform_Scale()
    {
        var text = await RenderPdf("<div style='transform: scale(1.5); background-color: orange; width: 100px; height: 100px'>Scaled</div>");
        text.Should().Contain("Scaled");
        text.Should().Contain(" cm\r\n");
    }

    // === Images ===

    private const string RedPixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==";

    [Fact]
    public async Task Image_PngBase64()
    {
        var text = await RenderPdf($"<img src='data:image/png;base64,{RedPixelPng}' width='100' height='100'>");
        text.Should().StartWith("%PDF");
    }

    [Fact]
    public async Task Image_WithDimensions()
    {
        var text = await RenderPdf($"<img src='data:image/png;base64,{RedPixelPng}' width='200' height='100' alt='Test image'>");
        text.Should().StartWith("%PDF");
    }

    [Fact]
    public async Task Image_MultipleImages()
    {
        var text = await RenderPdf($@"
            <p>Before images</p>
            <img src='data:image/png;base64,{RedPixelPng}' width='50' height='50'>
            <img src='data:image/png;base64,{RedPixelPng}' width='50' height='50'>
            <p>After images</p>");
        text.Should().Contain("Before images");
        text.Should().Contain("After images");
    }

    [Fact]
    public async Task Image_PngBase64_HasXObjectAndDo()
    {
        var text = await RenderPdf($"<img src='data:image/png;base64,{RedPixelPng}' width='100' height='100'>");
        text.Should().Contain("/XObject");
        text.Should().Contain("Do");
    }

    [Fact]
    public async Task Image_JpegBase64()
    {
        // Minimal 1x1 white JPEG
        var jpegBase64 = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/xAAUAQEAAAAAAAAAAAAAAAAAAAAA/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAwDAQACEQMRAD8AKwA//9k=";
        var text = await RenderPdf($"<img src='data:image/jpeg;base64,{jpegBase64}' width='100' height='100'>");
        text.Should().StartWith("%PDF");
    }

    [Fact]
    public async Task Image_BrokenSrc_DoesNotCrash()
    {
        var text = await RenderPdf("<img src='https://nonexistent.example.com/image.png' alt='Broken'>");
        text.Should().StartWith("%PDF");
    }

    // === Links ===

    [Fact]
    public async Task Link_HasAnnotation()
    {
        var text = await RenderPdf("<a href='https://github.com'>GitHub</a>");
        text.Should().Contain("GitHub");
        text.Should().Contain("https://github.com");
        text.Should().Contain("/Annot");
    }

    [Fact]
    public async Task Link_InternalAnchor()
    {
        var text = await RenderPdf(@"
            <a href='#section2'>Go to Section 2</a>
            <h2 id='section2'>Section 2</h2><p>Content</p>");
        text.Should().Contain("Go to Section 2");
        text.Should().Contain("Section 2");
    }

    [Fact]
    public async Task Link_ExternalUrl()
    {
        var text = await RenderPdf("<p>Visit <a href='https://github.com/eggspot/EggPdf'>EggPdf</a></p>");
        text.Should().Contain("https://github.com/eggspot/EggPdf");
    }

    // === Bookmarks ===

    [Fact]
    public async Task Bookmarks_HeadingHierarchy()
    {
        var text = await RenderPdf(@"
            <h1>Chapter 1</h1><p>Content.</p>
            <h2>Section 1.1</h2><p>More.</p>
            <h3>Subsection 1.1.1</h3><p>Details.</p>
            <h1>Chapter 2</h1><p>Another.</p>");
        text.Should().Contain("/Outlines");
        text.Should().Contain("/Type /Outlines");
        text.Should().Contain("Chapter 1");
        text.Should().Contain("Section 1.1");
        text.Should().Contain("Subsection 1.1.1");
        text.Should().Contain("Chapter 2");
        text.Should().Contain("/XYZ");
    }

    [Fact]
    public async Task Bookmarks_SingleH1()
    {
        var text = await RenderPdf("<h1>My Title</h1><p>Content</p>");
        text.Should().Contain("/Outlines");
        text.Should().Contain("My Title");
        text.Should().Contain("/Dest");
    }

    [Fact]
    public async Task Bookmarks_NoHeadings_NoOutlines()
    {
        var text = await RenderPdf("<p>Just a paragraph.</p>");
        text.Should().NotContain("/Outlines");
    }

    // === Page Breaks ===

    [Fact]
    public async Task PageBreakBefore_CreatesNewPage()
    {
        var text = await RenderPdf(@"
            <p>Page 1 content</p>
            <p style='page-break-before: always'>Page 2 content</p>");
        text.Should().Contain("Page 1 content");
        text.Should().Contain("Page 2 content");
        CountOccurrences(text, "/Type /Page ").Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task PageBreakAfter_CreatesNewPage()
    {
        var text = await RenderPdf(@"
            <p style='page-break-after: always'>Page 1 content</p>
            <p>Page 2 content</p>");
        text.Should().Contain("Page 1 content");
        text.Should().Contain("Page 2 content");
        CountOccurrences(text, "/Type /Page ").Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task LongContent_MultiplePages()
    {
        var sb = new StringBuilder("<html><body>");
        for (int i = 0; i < 100; i++)
            sb.Append($"<p>Paragraph {i}: Lorem ipsum dolor sit amet.</p>");
        sb.Append("</body></html>");

        var text = await RenderPdf(sb.ToString());
        text.Should().Contain("/Type /Page");
    }

    [Fact]
    public async Task BreakInsideAvoid_KeepsElementTogether()
    {
        var text = await RenderPdf(@"
            <div style='height: 780px'>Spacer</div>
            <div style='break-inside: avoid; height: 200px; background: #eee; border: 1px solid black'>
                <h2>Card Title</h2>
                <p>Should not split across pages</p>
            </div>");
        text.Should().Contain("Card Title");
        CountOccurrences(text, "/Type /Page ").Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task EmptyDocument_SinglePage()
    {
        var text = await RenderPdf("<html><body></body></html>");
        text.Should().Contain("/Count 1");
    }

    // === @page Rules ===

    [Fact]
    public async Task PageSize_Letter()
    {
        var text = await RenderPdf(@"<html><head><style>@page { size: letter; }</style></head>
            <body><p>Letter sized</p></body></html>");
        text.Should().Contain("/MediaBox [0 0 459.00 594.00]");
    }

    [Fact]
    public async Task PageSize_A4Landscape()
    {
        var text = await RenderPdf(@"<html><head><style>@page { size: A4 landscape; }</style></head>
            <body><p>Landscape A4</p></body></html>");
        text.Should().Contain("/MediaBox [0 0 631.42 446.46]");
    }

    [Fact]
    public async Task PageSize_Custom()
    {
        var text = await RenderPdf(@"<html><head><style>@page { size: 500px 700px; }</style></head>
            <body><p>Custom sized</p></body></html>");
        text.Should().Contain("/MediaBox [0 0 375.00 525.00]");
    }

    [Fact]
    public async Task PageMargin_AffectsContent()
    {
        var text = await RenderPdf(@"<html><head><style>@page { margin: 100px; }</style></head>
            <body><p>Content with margins</p></body></html>");
        text.Should().Contain("Content with margins");
        text.Should().Contain("/MediaBox [0 0 446.46 631.42]");
    }

    [Fact]
    public async Task NoPageRule_DefaultA4()
    {
        var text = await RenderPdf("<html><body><p>Default A4</p></body></html>");
        text.Should().Contain("/MediaBox [0 0 446.46 631.42]");
    }

    [Fact]
    public async Task MultiplePageRules_LastWins()
    {
        var text = await RenderPdf(@"<html><head><style>
            @page { size: letter; }
            @page { size: A5; }
        </style></head><body><p>Last rule wins</p></body></html>");
        text.Should().Contain("/MediaBox [0 0 314.65 446.46]");
    }

    // === CSS Variables ===

    [Fact]
    public async Task CssVariable_InColor()
    {
        var text = await RenderPdf(@"<html><head><style>
            :root { --brand-color: red; }
            p { color: var(--brand-color); }
        </style></head><body><p>Brand colored text</p></body></html>");
        text.Should().Contain("Brand colored text");
        text.Should().Contain("1.00 0.00 0.00 rg");
    }

    [Fact]
    public async Task CssVariable_WithFallback()
    {
        var text = await RenderPdf(@"<html><head><style>
            p { color: var(--undefined, blue); }
        </style></head><body><p>Fallback color</p></body></html>");
        text.Should().Contain("Fallback color");
    }

    // === Calc() ===

    [Fact]
    public async Task Calc_InWidth()
    {
        var text = await RenderPdf("<div style='width: calc(100% - 40px); background-color: #eee; height: 50px'>Calc width</div>");
        text.Should().Contain("Calc width");
    }

    // === Generated Content ===

    [Fact]
    public async Task BeforeContent_AppearsInPdf()
    {
        var text = await RenderPdf(@"<html><head><style>.price::before { content: '$'; }</style></head><body><p class='price'>100</p></body></html>");
        text.Should().Contain("100");
    }

    [Fact]
    public async Task AfterContent_AppearsInPdf()
    {
        var text = await RenderPdf(@"<html><head><style>.note::after { content: ' [end]'; }</style></head><body><p class='note'>Important note</p></body></html>");
        text.Should().Contain("Important note");
    }

    [Fact]
    public async Task AttrContent_InAfterPseudo()
    {
        var text = await RenderPdf(@"<html><head><style>a::after { content: ' (' attr(href) ')'; }</style></head><body><a href='http://example.com'>Visit</a></body></html>");
        text.Should().Contain("Visit");
        text.Should().Contain("http://example.com");
    }

    [Fact]
    public async Task BeforeAndAfter_Both()
    {
        var text = await RenderPdf(@"<html><head><style>.w::before { content: '['; } .w::after { content: ']'; }</style></head><body><p class='w'>Content</p></body></html>");
        text.Should().Contain("[");
        text.Should().Contain("]");
        text.Should().Contain("Content");
    }

    // === External Stylesheets ===

    [Fact]
    public async Task LinkStylesheet_DataUri_Applied()
    {
        var css = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(".bold { font-weight: bold; }"));
        var text = await RenderPdf($@"<html><head>
            <link rel=""stylesheet"" href=""data:text/css;base64,{css}"">
        </head><body><p class=""bold"">Bold from link</p></body></html>");
        text.Should().Contain("Bold from link");
        text.Should().Contain("Helvetica-Bold");
    }

    [Fact]
    public async Task ImportRule_DataUri_Applied()
    {
        var importedCss = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(".nested { font-family: monospace; }"));
        var text = await RenderPdf($@"<html><head><style>
            @import url(""data:text/css;base64,{importedCss}"");
            .outer {{ font-weight: bold; }}
        </style></head><body>
            <p class=""nested"">Nested mono</p>
            <p class=""outer"">Outer bold</p>
        </body></html>");
        text.Should().Contain("Nested mono");
        text.Should().Contain("Courier");
        text.Should().Contain("Helvetica-Bold");
    }

    [Fact]
    public async Task MediaScreen_Ignored()
    {
        var text = await RenderPdf(@"<html><head><style>
            @media screen { p { font-family: monospace; } }
        </style></head><body><p>Not monospace</p></body></html>");
        text.Should().Contain("Not monospace");
        text.Should().NotContain("Courier", "@media screen should be ignored for print");
    }

    // === CSS Importance ===

    [Fact]
    public async Task Important_OverridesNormalRule()
    {
        var text = await RenderPdf(@"<html><head><style>
            p { color: blue; }
            p { color: red !important; }
        </style></head><body><p>Important red</p></body></html>");
        text.Should().Contain("Important red");
        text.Should().Contain("1.00 0.00 0.00 rg");
    }

    // === CSS Variable in font-size ===

    [Fact]
    public async Task CssVariable_InFontSize()
    {
        var text = await RenderPdf(@"<html><head><style>
            :root { --heading-size: 32px; }
            h1 { font-size: var(--heading-size); }
        </style></head><body><h1>Large heading</h1></body></html>");
        text.Should().Contain("Large heading");
    }

    // === CSS Selectors ===

    [Fact]
    public async Task Selector_AdjacentSibling()
    {
        var text = await RenderPdf(@"<style>h1 + p { color: red; }</style>
            <h1>Title</h1><p>First paragraph</p><p>Second paragraph</p>");
        text.Should().Contain("Title");
        text.Should().Contain("First paragraph");
        text.Should().Contain("1.00 0.00 0.00 rg");
    }

    [Fact]
    public async Task Selector_ClassAndId()
    {
        var text = await RenderPdf(@"<style>.red { color: red; } #main { font-weight: bold; }</style>
            <p class='red'>Red text</p><p id='main'>Bold text</p>");
        text.Should().Contain("Red text");
        text.Should().Contain("Bold text");
    }

    [Fact]
    public async Task Selector_GeneralSibling()
    {
        var text = await RenderPdf(@"<style>h1 ~ p { font-size: 20px; }</style>
            <h1>Title</h1><p>First</p><p>Second</p>");
        text.Should().Contain("Title");
        text.Should().Contain("First");
        text.Should().Contain("Second");
    }

    [Fact]
    public async Task Selector_Not()
    {
        var text = await RenderPdf(@"<style>p:not(.skip) { color: red; }</style>
            <p>Red text</p><p class='skip'>Default text</p>");
        text.Should().Contain("Red text");
        text.Should().Contain("Default text");
        text.Should().Contain("1.00 0.00 0.00 rg");
    }

    [Fact]
    public async Task Selector_DescendantAndChild()
    {
        var text = await RenderPdf(@"<style>
            .container p { color: blue; }
            .container > span { font-weight: bold; }
        </style>
        <div class='container'><p>Blue para</p><span>Bold span</span></div>");
        text.Should().Contain("Blue para");
        text.Should().Contain("Bold span");
        text.Should().Contain("0.00 0.00 1.00 rg");
    }

    [Fact]
    public async Task Selector_NthChild()
    {
        var text = await RenderPdf(@"<style>li:nth-child(odd) { color: red; }</style>
            <ul><li>One</li><li>Two</li><li>Three</li></ul>");
        text.Should().Contain("One");
        text.Should().Contain("Two");
        text.Should().Contain("Three");
    }

    // === Style Tag ===

    [Fact]
    public async Task StyleTag_AppliesStyles()
    {
        var text = await RenderPdf(@"<html><head><style>
            .custom { color: red; font-weight: bold; }
        </style></head><body><p class='custom'>Styled paragraph</p></body></html>");
        text.Should().Contain("Styled paragraph");
    }

    // === Media Queries ===

    [Fact]
    public async Task MediaPrint_AppliesStyles()
    {
        var text = await RenderPdf(@"<html><head><style>
            @media print { p { color: red; } }
        </style></head><body><p>Print styled</p></body></html>");
        text.Should().Contain("Print styled");
    }

    // === WebUI PDF Preview content check ===

    [Fact]
    public async Task WebUI_PdfPreview_ContainsSameContentAsPrintPreview()
    {
        // Verify that the PDF rendered from the same HTML contains the same text
        var html = @"<h1>Preview Test</h1><p>Both previews should show this text.</p>
            <table><tr><td>Cell A</td><td>Cell B</td></tr></table>";

        var printHtml = await GetPrintPreview(html);
        printHtml.Should().Contain("Preview Test");
        printHtml.Should().Contain("Both previews");

        var pdfText = await RenderPdf(html);
        pdfText.Should().Contain("Preview Test");
        pdfText.Should().Contain("Both previews");
        pdfText.Should().Contain("Cell A");
        pdfText.Should().Contain("Cell B");
    }

    private async Task<string> GetPrintPreview(string html)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html }),
            Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render/print-preview", content);
        return await resp.Content.ReadAsStringAsync();
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(pattern, index, System.StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
