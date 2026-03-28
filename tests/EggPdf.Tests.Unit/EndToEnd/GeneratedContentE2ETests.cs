using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class GeneratedContentE2ETests
{
    [Fact]
    public async Task BeforeContent_AppearsInPdf()
    {
        var html = @"<html><head><style>.price::before { content: '$'; }</style></head><body><p class='price'>100</p></body></html>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().StartWith("%PDF");
        text.Should().Contain("100");
    }

    [Fact]
    public async Task AfterContent_AppearsInPdf()
    {
        var html = @"<html><head><style>.note::after { content: ' [end]'; }</style></head><body><p class='note'>Important note</p></body></html>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().StartWith("%PDF");
        text.Should().Contain("Important note");
    }

    [Fact]
    public async Task ListMarkerViaBeforeContent()
    {
        var html = @"<html><head><style>.custom-list { list-style: none; } .custom-list li::before { content: '>> '; }</style></head><body><ul class='custom-list'><li>First item</li></ul></body></html>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain(">>");
        text.Should().Contain("First item");
    }

    [Fact]
    public async Task QuoteDecorations()
    {
        var html = @"<html><head><style>.quote::before { content: open-quote; } .quote::after { content: close-quote; }</style></head><body><p class='quote'>To be or not to be</p></body></html>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("To be or not to be");
    }

    [Fact]
    public async Task BeforeContent_WithColor_AppliesStyle()
    {
        var html = @"<html><head><style>p::before { content: 'Note: '; color: red; font-weight: bold; }</style></head><body><p>This is important</p></body></html>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().StartWith("%PDF");
        text.Should().Contain("This is important");
    }

    [Fact]
    public async Task BeforeAndAfter_BothAppearInPdf()
    {
        var html = @"<html><head><style>.wrapped::before { content: '['; } .wrapped::after { content: ']'; }</style></head><body><p class='wrapped'>Content</p></body></html>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("[");
        text.Should().Contain("]");
        text.Should().Contain("Content");
    }

    [Fact]
    public async Task AttrContent_AppearsInPdf()
    {
        var html = @"<html><head><style>a::after { content: ' (' attr(href) ')'; }</style></head><body><a href='http://example.com'>Visit</a></body></html>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Visit");
        text.Should().Contain("http://example.com");
    }
}
