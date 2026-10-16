using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfDocumentFallbackFontTests
{
    [Fact]
    public void GetFallbackFonts_NoneRegistered_IsEmpty()
    {
        new PdfDocument().GetFallbackFonts("Arial").Should().BeEmpty();
    }

    [Fact]
    public void AddFallbackFont_KeepsRegistrationOrder_PerPrimaryFont()
    {
        var doc = new PdfDocument();
        doc.AddFallbackFont("Arial", "Arial-FB");
        doc.AddFallbackFont("Arial", "Arial-FB2");
        doc.AddFallbackFont("Arial-Bold", "Arial-Bold-FB");

        doc.GetFallbackFonts("Arial").Should().Equal("Arial-FB", "Arial-FB2");
        doc.GetFallbackFonts("Arial-Bold").Should().Equal("Arial-Bold-FB");
    }

    [Fact]
    public void AddFallbackFont_SameFontTwice_IsRegisteredOnce()
    {
        var doc = new PdfDocument();
        doc.AddFallbackFont("Arial", "Arial-FB");
        doc.AddFallbackFont("Arial", "Arial-FB");

        doc.GetFallbackFonts("Arial").Should().ContainSingle();
    }
}
