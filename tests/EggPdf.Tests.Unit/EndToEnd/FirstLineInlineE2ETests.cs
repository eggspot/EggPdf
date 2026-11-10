using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class FirstLineInlineE2ETests
{
    private const string Red = "1.00 0.00 0.00 rg";

    [Fact]
    public void FirstLineBesideABoldElement_IsPaintedRed_AndTheNextLineIsNot()
    {
        var html = "<style>body{margin:0} p::first-line { color: red }</style>" +
                   "<p style='width:300px'>Opening words with a <b>bold</b> word and then many more words " +
                   "until the paragraph wraps onto a second line ending in Closing</p>";

        var text = PdfAssert.ValidPdf(HtmlToPdf.Render(html), "(Opening) Tj", "( bold) Tj");

        PdfAssert.TextPaintedWith(text, "Opening", Red).Should().BeTrue();
        PdfAssert.TextPaintedWith(text, " bold", Red).Should().BeTrue("the inline element inherits the first line's colour");
        text.Should().Contain("Closing) Tj");
        (PdfAssert.TextPaintedWith(text, "Closing", Red) || PdfAssert.TextPaintedWith(text, " Closing", Red))
            .Should().BeFalse("the last line is not the first line");
    }
}
