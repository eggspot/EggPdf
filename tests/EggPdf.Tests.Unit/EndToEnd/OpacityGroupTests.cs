using System;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class OpacityGroupTests
{
    [Fact]
    public void ParentOpacity_FadesChildText()
    {
        var text = PdfAssert.ValidPdf(HtmlToPdf.Render("<div style='opacity:0.5'><p>Faded</p></div>"));

        int gs = text.IndexOf("/GS50 gs", StringComparison.Ordinal);
        int tj = text.IndexOf("Tj", StringComparison.Ordinal);
        gs.Should().BeGreaterThan(-1, "the child text is painted at the parent's opacity");
        gs.Should().BeLessThan(tj);
    }

    [Fact]
    public void NestedOpacity_Multiplies()
    {
        var text = PdfAssert.ValidPdf(HtmlToPdf.Render("<div style='opacity:0.5'><div style='opacity:0.5;background:red;height:10px'></div></div>"));

        text.Should().Contain("/GS25 gs");
    }

    [Fact]
    public void TranslucentBackgroundInsideAnOpacityGroup_CombinesBothAlphas()
    {
        var text = PdfAssert.ValidPdf(HtmlToPdf.Render("<div style='opacity:0.5;background:rgba(255,0,0,0.5);height:10px'></div>"));

        text.Should().Contain("/GS25 gs");
        text.Should().Contain("/GS50 gs", "after the background the box's own opacity is restored, not 1");
    }

    [Fact]
    public void OpacityOnABorderedBox_AppliesToTheBorder()
    {
        var text = PdfAssert.ValidPdf(HtmlToPdf.Render("<div style='opacity:0.3;border:2px solid red;height:10px'></div>"));

        int gs = text.IndexOf("/GS30 gs", StringComparison.Ordinal);
        int red = text.IndexOf("1.00 0.00 0.00", StringComparison.Ordinal);
        gs.Should().BeGreaterThan(-1);
        gs.Should().BeLessThan(red);
    }
}
