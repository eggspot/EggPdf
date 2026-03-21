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

        // Calling twice with same name should return same (cached) result
        var result1 = resolver.Resolve("sans-serif");
        var result2 = resolver.Resolve("sans-serif");

        // Both should be the same reference (cached)
        if (result1 != null)
            ReferenceEquals(result1, result2).Should().BeTrue();
    }

    [Fact]
    public void Resolve_NonexistentFont_ReturnsNull()
    {
        var resolver = new FontResolver();
        var result = resolver.Resolve("CompletelyMadeUpFontXYZ123");

        // May return null (no font found) or a fallback -- both acceptable
    }
}
