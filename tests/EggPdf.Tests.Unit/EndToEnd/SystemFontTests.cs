using System;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class SystemFontTests
{
    private static bool OnlyVerdana(string family) => string.Equals(family, "Verdana", StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("Verdana, sans-serif", "Verdana")]
    [InlineData("'Not Installed', Verdana", "Verdana")]
    [InlineData("Arial, Verdana", null)]
    [InlineData("sans-serif", null)]
    [InlineData("Unknown", null)]
    public void FirstInstalledFamily_SkipsMissingFamiliesAndStopsAtGenericOrStandardOnes(string familyList, string? expected)
    {
        HtmlToPdf.FirstInstalledFamily(familyList, OnlyVerdana).Should().Be(expected);
    }

    [Fact]
    public void InstalledFamily_GetsItsOwnPdfFontName()
    {
        TextMeasurer.InstalledFamilyResolver = list => HtmlToPdf.FirstInstalledFamily(list, family => family == "Segoe UI");
        try
        {
            StandardFontMetrics.ResolvePdfFontName("'Segoe UI', sans-serif", "700", "normal").Should().Be("SegoeUI-Bold");
            StandardFontMetrics.ResolvePdfFontName("sans-serif", "400", "normal").Should().Be("Helvetica");
        }
        finally
        {
            TextMeasurer.InstalledFamilyResolver = null;
        }
        StandardFontMetrics.ResolvePdfFontName("'Segoe UI', sans-serif", "700", "normal").Should().NotBe("SegoeUI-Bold");
    }

    [Fact]
    public void UsesInstalledSystemFamilies_FindsInlineAndStylesheetFamilies()
    {
        var inline = Html.HtmlParser.Parse("<p style='font-family: Verdana'>x</p>");
        HtmlToPdf.UsesInstalledSystemFamilies(inline, null, OnlyVerdana).Should().BeTrue();

        var standard = Html.HtmlParser.Parse("<p style='font-family: Arial'>x</p>");
        HtmlToPdf.UsesInstalledSystemFamilies(standard, null, OnlyVerdana).Should().BeFalse();
    }

    [Fact]
    public void NonEmbeddedArial_IsWrittenAsTheStandardHelveticaFont()
    {
        var text = PdfAssert.ValidPdf(HtmlToPdf.Render("<p style=\"font-family: Arial\">Hello</p>"));

        text.Should().NotContain("/BaseFont /Arial ", "Arial is not one of the 14 standard PDF fonts");
    }
}
