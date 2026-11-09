using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class JustifiedLineTests
{
    private static int NonZeroWordSpacingCount(string html)
    {
        var content = PdfAssert.ValidPdf(HtmlToPdf.Render(html));
        return Regex.Matches(content, @"(?<![\d.])(?:[1-9]\d*\.\d+|0\.\d*[1-9]\d*) Tw").Count;
    }

    [Fact]
    public void Justify_TextOnlyLongLastLine_IsNotStretched()
    {
        var html = "<p style='margin:0;width:200px;text-align:justify;font-family:Helvetica;font-size:12px'>" +
                   "word word word word word word word word word word word</p>";

        NonZeroWordSpacingCount(html).Should().Be(1, "only the first of the two lines is justified, however long the last one is");
    }

    [Fact]
    public void Justify_TextOnlyShortMiddleLine_IsStretched()
    {
        var html = "<p style='margin:0;width:200px;text-align:justify;font-family:Helvetica;font-size:12px'>" +
                   "aa bb cc Pneumonoultramicroscopicsilicovolcanoconiosisxx dd ee</p>";

        NonZeroWordSpacingCount(html).Should().BeGreaterThan(0, "a short line that is not the last line is still justified");
    }
}
