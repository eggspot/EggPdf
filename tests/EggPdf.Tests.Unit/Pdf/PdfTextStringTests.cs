using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfTextStringTests
{
    private static string DecodeHexTextString(string hex)
    {
        hex.Should().StartWith("<FEFF").And.EndWith(">");
        var digits = hex.Substring(5, hex.Length - 6);
        var bytes = new byte[digits.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(digits.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Encoding.BigEndianUnicode.GetString(bytes);
    }

    [Theory]
    [InlineData("Report", "(Report)")]
    [InlineData("a (b) c\\d", "(a \\(b\\) c\\\\d)")]
    public void Encode_PrintableAscii_IsAnEscapedLiteral(string text, string expected)
    {
        PdfTextStrings.Encode(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("Báo cáo tài chính")]
    [InlineData("Ünïcödé — “quotes”")]
    [InlineData("日本語のタイトル")]
    [InlineData("emoji 😀 inside")]
    public void Encode_NonAscii_IsUtf16BigEndianHexWithByteOrderMark(string text)
    {
        DecodeHexTextString(PdfTextStrings.Encode(text)).Should().Be(text);
    }

    [Theory]
    [InlineData("https://example.com/a?b=c#d", "https://example.com/a?b=c#d")]
    [InlineData("https://example.com/ö", "https://example.com/%C3%B6")]
    [InlineData("https://vi.wikipedia.org/wiki/Việt Nam", "https://vi.wikipedia.org/wiki/Vi%E1%BB%87t%20Nam")]
    public void UriToAscii_NonAsciiAndSpaces_ArePercentEncodedUtf8(string uri, string expected)
    {
        PdfTextStrings.UriToAscii(uri).Should().Be(expected);
    }

    private static string Latin1(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    [Fact]
    public void Render_NonAsciiTitle_IsWrittenAsUnicodeTextString()
    {
        var text = Latin1(HtmlToPdf.Render("<html><head><title>Báo cáo quý 3</title></head><body><p>x</p></body></html>"));

        var match = Regex.Match(text, @"/Title (<FEFF[0-9A-F]+>)");
        match.Success.Should().BeTrue("a non-ASCII title must not be written as a literal with '?' in it");
        DecodeHexTextString(match.Groups[1].Value).Should().Be("Báo cáo quý 3");
    }

    [Fact]
    public void Render_NonAsciiLink_UriIsPercentEncoded()
    {
        var text = Latin1(HtmlToPdf.Render("<a href='https://example.com/café'>link</a>"));

        text.Should().Contain("/URI (https://example.com/caf%C3%A9)");
    }

    [Fact]
    public void Render_NonAsciiHeading_BookmarkTitleIsUnicode()
    {
        var text = Latin1(HtmlToPdf.Render("<h1>Tổng quan</h1><p>x</p>"));

        var titles = Regex.Matches(text, @"/Title (<FEFF[0-9A-F]+>)");
        titles.Should().Contain(m => DecodeHexTextString(m.Groups[1].Value) == "Tổng quan",
            "the outline entry for the heading keeps its Vietnamese text");
    }
}
