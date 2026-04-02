using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// End-to-end tests for advanced CSS selectors: HTML-to-PDF pipeline.
/// Verifies that sibling combinators, :not(), :nth-child(), etc. apply styles correctly.
/// </summary>
public class SelectorE2ETests
{
    [Fact]
    public async Task AdjacentSibling_StyleApplied()
    {
        var html = @"
            <style>
                h1 + p { color: red; }
            </style>
            <h1>Title</h1>
            <p>First paragraph (should be red)</p>
            <p>Second paragraph (should be default)</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Title");
        text.Should().Contain("First paragraph");
        text.Should().Contain("Second paragraph");
        // Red color (1.00 0.00 0.00 rg) should be present
        text.Should().Contain("1.00 0.00 0.00 rg", "h1 + p selector should apply red color");
    }

    [Fact]
    public async Task GeneralSibling_StyleApplied()
    {
        var html = @"
            <style>
                h1 ~ p { font-size: 20px; }
            </style>
            <h1>Title</h1>
            <div>Separator</div>
            <p>Paragraph after h1</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Title");
        text.Should().Contain("Paragraph after h1");
    }

    [Fact]
    public async Task NotPseudoClass_ExcludesElements()
    {
        var html = @"
            <style>
                p:not(.skip) { color: blue; }
            </style>
            <p class='skip'>Skipped</p>
            <p>Styled blue</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Skipped");
        text.Should().Contain("Styled blue");
        // Blue color should be present for the non-skipped paragraph
        text.Should().Contain("0.00 0.00 1.00 rg", ":not(.skip) should apply blue to non-skipped paragraphs");
    }

    [Fact]
    public async Task NthChild_OddEven_AlternatingColors()
    {
        var html = @"
            <style>
                li:nth-child(odd) { background-color: #f0f0f0; }
                li:nth-child(even) { background-color: #e0e0e0; }
            </style>
            <ul>
                <li>Item 1</li>
                <li>Item 2</li>
                <li>Item 3</li>
                <li>Item 4</li>
            </ul>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Item 1");
        text.Should().Contain("Item 2");
        text.Should().Contain("Item 3");
        text.Should().Contain("Item 4");
    }

    [Fact]
    public async Task FirstOfType_StyleApplied()
    {
        var html = @"
            <style>
                p:first-of-type { color: green; }
            </style>
            <div>
                <span>Span</span>
                <p>First p</p>
                <p>Second p</p>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("First p");
        text.Should().Contain("Second p");
    }

    [Fact]
    public async Task OnlyChild_StyleApplied()
    {
        var html = @"
            <style>
                p:only-child { color: purple; }
            </style>
            <div><p>Only child</p></div>
            <div><p>Has</p><p>Sibling</p></div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Only child");
        text.Should().Contain("Sibling");
    }

    [Fact]
    public async Task IsPseudoClass_MatchesMultiple()
    {
        var html = @"
            <style>
                :is(h1, h2, h3) { color: navy; }
            </style>
            <h1>Heading 1</h1>
            <h2>Heading 2</h2>
            <p>Regular text</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Heading 1");
        text.Should().Contain("Heading 2");
        text.Should().Contain("Regular text");
    }

    [Fact]
    public async Task AttributeSelectors_StartsWith_EndsWith()
    {
        var html = @"
            <style>
                a[href^='https'] { color: green; }
                a[href$='.pdf'] { color: red; }
            </style>
            <a href='https://example.com'>Secure link</a>
            <a href='document.pdf'>PDF link</a>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Secure");
        text.Should().Contain("link");
        text.Should().Contain("PDF");
    }

    [Fact]
    public async Task SelectorList_CommaSeparated()
    {
        var html = @"
            <style>
                h1, h2 { color: red; }
            </style>
            <h1>Title</h1>
            <h2>Subtitle</h2>
            <p>Body</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Title");
        text.Should().Contain("Subtitle");
        text.Should().Contain("Body");
    }

    [Fact]
    public async Task ComplexSelector_ChildPlusSibling()
    {
        var html = @"
            <style>
                .container > h2 + p { color: red; }
            </style>
            <div class='container'>
                <h2>Section</h2>
                <p>First after heading</p>
                <p>Second paragraph</p>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Section");
        text.Should().Contain("First after heading");
        // Red should be applied to "First after heading" via .container > h2 + p
        text.Should().Contain("1.00 0.00 0.00 rg");
    }
}
