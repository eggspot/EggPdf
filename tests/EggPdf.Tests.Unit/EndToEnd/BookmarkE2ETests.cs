using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class BookmarkE2ETests
{
    [Fact]
    public async Task H1H2H3_ProducesPdfWithOutlines()
    {
        var html = @"
            <h1>Chapter 1</h1>
            <p>Some content here.</p>
            <h2>Section 1.1</h2>
            <p>More content.</p>
            <h3>Subsection 1.1.1</h3>
            <p>Details.</p>
            <h1>Chapter 2</h1>
            <p>Another chapter.</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("/Outlines");
        text.Should().Contain("/Type /Outlines");
        text.Should().Contain("Chapter 1");
        text.Should().Contain("Section 1.1");
        text.Should().Contain("Subsection 1.1.1");
        text.Should().Contain("Chapter 2");
        text.Should().Contain("/XYZ");
    }

    [Fact]
    public async Task LongDocument_BookmarksNavigateCorrectly()
    {
        // Create a document with enough content to span multiple pages
        var sb = new StringBuilder();
        sb.Append("<h1>Introduction</h1>");
        sb.Append("<p>Intro text.</p>");

        // Add enough content to push past a page boundary
        for (int i = 0; i < 50; i++)
            sb.Append($"<p>Filler paragraph {i} to push content to next page.</p>");

        sb.Append("<h1>Conclusion</h1>");
        sb.Append("<p>Final text.</p>");

        byte[] pdf = await HtmlToPdf.RenderAsync(sb.ToString());

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("/Outlines");
        text.Should().Contain("Introduction");
        text.Should().Contain("Conclusion");

        // Should have /Dest entries pointing to pages
        text.Should().Contain("/Dest");
        text.Should().Contain("/XYZ");

        // Should have multiple pages
        text.Should().Contain("/Count 2", because: "there are 2 top-level bookmarks");
    }

    [Fact]
    public async Task NoHeadings_NoPdfOutlines()
    {
        var html = @"<p>Just a paragraph with no headings.</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().NotContain("/Outlines");
        text.Should().NotContain("/Type /Outlines");
    }

    [Fact]
    public async Task SingleH1_ProducesBookmark()
    {
        var html = @"<h1>My Title</h1><p>Content</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("/Outlines");
        text.Should().Contain("My Title");
        text.Should().Contain("/Dest");
    }
}
