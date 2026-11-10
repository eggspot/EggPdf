using EggPdf.Text;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class FontResolverTests
{
    [Theory]
    [InlineData("Helvetica", false, false, "Helvetica")]
    [InlineData("Helvetica", true, false, "Helvetica-Bold")]
    [InlineData("Helvetica", false, true, "Helvetica-Oblique")]
    [InlineData("Helvetica", true, true, "Helvetica-BoldOblique")]
    [InlineData("Arial", false, false, "Helvetica")]
    [InlineData("sans-serif", true, false, "Helvetica-Bold")]
    [InlineData("Courier", false, false, "Courier")]
    [InlineData("monospace", true, false, "Courier-Bold")]
    [InlineData("Times New Roman", false, false, "Times-Roman")]
    [InlineData("serif", false, true, "Times-Italic")]
    public void GetPdfStandardFontName_CorrectMapping(string family, bool bold, bool italic, string expected)
    {
        var result = FontResolver.GetPdfStandardFontName(family, bold, italic);
        result.Should().Be(expected);
    }

    [Fact]
    public void GetPdfStandardFontName_NullFamily_ReturnsHelvetica()
    {
        FontResolver.GetPdfStandardFontName(null, false, false).Should().Be("Helvetica");
    }

    [Fact]
    public void GetPdfStandardFontName_EmptyFamily_ReturnsHelvetica()
    {
        FontResolver.GetPdfStandardFontName("", false, false).Should().Be("Helvetica");
    }

    [Fact]
    public void Resolve_SystemFont_CachesResult()
    {
        var resolver = new FontResolver();

        var result1 = resolver.Resolve("sans-serif");
        var result2 = resolver.Resolve("sans-serif");

        ReferenceEquals(result1, result2).Should().BeTrue("the second lookup of the same family returns the cached result");
    }

    [Fact]
    public void Resolve_NonexistentFont_ReturnsNull()
    {
        var resolver = new FontResolver();

        resolver.Resolve("CompletelyMadeUpFontXYZ123").Should().BeNull(
            "no installed font file is named like it and it is no generic family, so the caller falls back");
    }
}
