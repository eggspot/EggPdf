using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class HtmlCommentRenderTests
{
    private static async Task<string> RenderContent(string html) =>
        Encoding.ASCII.GetString(await HtmlToPdf.RenderAsync(html));

    [Fact]
    public async Task CommentBeforeElement_IsNotPainted()
    {
        var content = await RenderContent("<html><body><!-- Header --><div>Invoice</div></body></html>");

        content.Should().Contain("(Invoice) Tj");
        content.Should().NotContain("Header");
    }

    [Fact]
    public async Task CommentInsideParagraph_IsNotPainted()
    {
        var content = await RenderContent("<html><body><p>Total: <!-- amount -->$10</p></body></html>");

        content.Should().Contain("$10");
        content.Should().NotContain("amount");
    }

    [Fact]
    public async Task CommentBeforeHtmlElement_HeadContentIsNotPainted()
    {
        var content = await RenderContent(
            "<!-- generated --><html><head><title>Doc title</title><style>body{margin:0}</style></head>" +
            "<body><p>Body text</p></body></html>");

        content.Should().Contain("(Body text) Tj");
        content.Should().NotContain("generated");
        content.Should().NotContain("(Doc title) Tj");
        content.Should().NotContain("margin:0");
    }

    [Fact]
    public async Task XmlDeclaration_IsNotPainted()
    {
        var content = await RenderContent(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><html><body><p>Body text</p></body></html>");

        content.Should().Contain("(Body text) Tj");
        content.Should().NotContain("xml version");
    }
}
