using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class StandardFontMetricsTests
{
    [Theory]
    [InlineData("Arial, sans-serif", null, null, "Arial")]
    [InlineData("Arial, sans-serif", "bold", null, "Arial-Bold")]
    [InlineData("Arial, sans-serif", null, "italic", "Arial-Italic")]
    [InlineData("Arial, sans-serif", "bold", "italic", "Arial-BoldItalic")]
    [InlineData("'Segoe UI', Arial, sans-serif", null, null, "Arial")]
    [InlineData("'Segoe UI', Arial, sans-serif", "bold", null, "Arial-Bold")]
    [InlineData("'Segoe UI', Arial, sans-serif", "700", null, "Arial-Bold")]
    public void ResolvePdfFontName_ArialAndSegoeUI_ReturnsArialVariant(
        string fontFamily, string? fontWeight, string? fontStyle, string expected)
    {
        var result = StandardFontMetrics.ResolvePdfFontName(fontFamily, fontWeight, fontStyle);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Helvetica, sans-serif", null, null, "Helvetica")]
    [InlineData("sans-serif", null, null, "Helvetica")]
    [InlineData("Times New Roman, serif", null, null, "Times-Roman")]
    [InlineData("Courier New, monospace", null, null, "Courier")]
    public void ResolvePdfFontName_OtherFamilies_StillCorrect(
        string fontFamily, string? fontWeight, string? fontStyle, string expected)
    {
        var result = StandardFontMetrics.ResolvePdfFontName(fontFamily, fontWeight, fontStyle);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Arial")]
    [InlineData("Arial-Bold")]
    [InlineData("Arial-Italic")]
    [InlineData("Arial-BoldItalic")]
    public void MeasureWidth_ArialVariants_UseHelveticaCompatibleMetrics(string fontName)
    {
        // Arial is metric-compatible with Helvetica; both should produce identical measurements
        float arialWidth = StandardFontMetrics.MeasureWidth("Hello World", 12f, fontName);
        float helveticaBase = fontName.Contains("Bold")
            ? StandardFontMetrics.MeasureWidth("Hello World", 12f, "Helvetica-Bold")
            : StandardFontMetrics.MeasureWidth("Hello World", 12f, "Helvetica");

        arialWidth.Should().BeApproximately(helveticaBase, 0.01f,
            "Arial metrics should match Helvetica (metric-compatible fonts)");
    }

    [Fact]
    public void ResolvePdfFontName_NoFontFeatureSettings_UnaffectedByOptionalParam()
    {
        var result = StandardFontMetrics.ResolvePdfFontName("Arial, sans-serif", "bold", null, null);
        result.Should().Be("Arial-Bold");
    }

    [Fact]
    public void ResolvePdfFontName_ActiveFontFeatureSettings_AppendsDeterministicSuffix()
    {
        var result = StandardFontMetrics.ResolvePdfFontName("Arial, sans-serif", null, null, "\"zero\" 1, \"smcp\" on");
        result.Should().Be("Arial-Feat-smcp-zero");
    }

    [Fact]
    public void ResolvePdfFontName_DifferentFeatureSettings_ProduceDistinctNames()
    {
        var a = StandardFontMetrics.ResolvePdfFontName("Arial, sans-serif", null, null, "\"zero\" 1");
        var b = StandardFontMetrics.ResolvePdfFontName("Arial, sans-serif", null, null, "\"smcp\" 1");
        a.Should().NotBe(b, "distinct font-feature-settings values must not collide on the same embedded-font entry");
    }

    [Fact]
    public void ResolvePdfFontName_NormalFontFeatureSettings_NoSuffixAppended()
    {
        var result = StandardFontMetrics.ResolvePdfFontName("Arial, sans-serif", "bold", null, "normal");
        result.Should().Be("Arial-Bold");
    }
}
