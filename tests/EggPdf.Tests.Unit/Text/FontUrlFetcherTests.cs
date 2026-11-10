using EggPdf.Text;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class FontUrlFetcherTests
{
    [Fact]
    public void ParseFontSrcUrl_PlainUrl_ReturnsUrl()
    {
        FontUrlFetcher.ParseFontSrcUrl("url(https://example.com/font.ttf)")
            .Should().Be("https://example.com/font.ttf");
    }

    [Fact]
    public void ParseFontSrcUrl_UrlWithFormatSuffix_ReturnsUrlOnly()
    {
        FontUrlFetcher.ParseFontSrcUrl("url(https://fonts.gstatic.com/s/x.ttf) format('truetype')")
            .Should().Be("https://fonts.gstatic.com/s/x.ttf", "this is the src form Google Fonts CSS emits");
    }

    [Fact]
    public void ParseFontSrcUrl_QuotedUrlWithFormat_ReturnsUrlOnly()
    {
        FontUrlFetcher.ParseFontSrcUrl("url(\"https://example.com/font.woff2\") format(\"woff2\")")
            .Should().Be("https://example.com/font.woff2");
    }

    [Fact]
    public void ParseFontSrcUrl_Local_ReturnsLocalPrefix()
    {
        FontUrlFetcher.ParseFontSrcUrl("local(\"Segoe UI\")")
            .Should().Be("local:Segoe UI");
    }

    [Fact]
    public void ParseFontSrcUrl_Invalid_ReturnsNull()
    {
        FontUrlFetcher.ParseFontSrcUrl("format('truetype')").Should().BeNull();
    }

    [Fact]
    public void ParseFontSrcUrl_BareUrlWithFormat_ReturnsUrl()
    {
        FontUrlFetcher.ParseFontSrcUrl("https://fonts.gstatic.com/s/x.ttf format('truetype')")
            .Should().Be("https://fonts.gstatic.com/s/x.ttf", "the CSS parser unwraps url(...), leaving a bare URL and format(...)");
    }

    [Fact]
    public void ParseFontSrcUrl_BareUrlAlone_ReturnsUrl()
    {
        FontUrlFetcher.ParseFontSrcUrl("https://example.com/font.ttf")
            .Should().Be("https://example.com/font.ttf");
    }
}
