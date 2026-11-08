using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class SmallCapsAndSuperscriptTests
{
    [Fact]
    public void SmallCaps_PaintsLowercaseAsSmallerCapitals()
    {
        var text = PdfAssert.ValidPdf(HtmlToPdf.Render("<p style='font-variant:small-caps;font-size:20px'>Ab</p>"));

        text.Should().Contain(" 15.00 Tf", "the capital A keeps the full 20px size");
        text.Should().Contain(" 10.50 Tf", "the lowercase b becomes a capital at 70% size");
    }

    [Fact]
    public void Superscript_IsPaintedAtTheSmallerSize()
    {
        var text = PdfAssert.ValidPdf(HtmlToPdf.Render("<p style='font-size:12px'>x<sup>2</sup></p>"));

        text.Should().Contain(" 7.50 Tf", "sup is font-size: smaller, 10px");
    }
}
