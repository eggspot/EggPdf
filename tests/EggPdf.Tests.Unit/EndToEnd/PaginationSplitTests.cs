using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class PaginationSplitTests
{
    private static List<string> PageStreams(string pdfText) =>
        Regex.Matches(pdfText, @"stream\n(.*?)endstream", RegexOptions.Singleline).Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .Where(s => s.StartsWith("1.00 1.00 1.00 rg", System.StringComparison.Ordinal))
            .ToList();

    private static int PageOf(List<string> pages, string word) =>
        pages.FindIndex(p => p.Contains("(" + word + ") Tj"));

    [Fact]
    public void BackgroundContainerShorterThanAPage_SplitsAtTheBreakInsteadOfMovingWhole()
    {
        var body = new StringBuilder("<div style='height:950px'></div><div style='background:#eeeeee'><p>First</p>");
        for (int i = 0; i < 10; i++) body.Append("<p>Middle</p>");
        body.Append("<p>Last</p></div>");

        var pages = PageStreams(PdfAssert.ValidPdf(HtmlToPdf.Render(body.ToString())));

        PageOf(pages, "First").Should().Be(0, "the container starts on page 1 and continues on page 2");
        PageOf(pages, "Last").Should().Be(1);
    }

    [Fact]
    public void RepeatedTableHeader_LeavesRoomSoNoRowIsPushedOffThePage()
    {
        var html = new StringBuilder("<table><thead><tr><th>Head</th></tr></thead><tbody>");
        for (int i = 0; i < 120; i++) html.Append("<tr><td>Row").Append(i).Append("</td></tr>");
        html.Append("</tbody></table>");

        var text = PdfAssert.ValidPdf(HtmlToPdf.Render(html.ToString()));

        for (int i = 0; i < 120; i++)
        {
            var (_, y) = PdfAssert.TextPosition(text, "Row" + i);
            y.Should().BeGreaterThan(0f, $"Row{i} must be painted inside the page");
        }
    }

    [Fact]
    public void TableHeader_IsNotLeftAloneAtThePageBottom()
    {
        for (int spacer = 960; spacer <= 1110; spacer += 6)
        {
            var html = $"<div style='height:{spacer}px'></div><table><thead><tr><th>Head</th></tr></thead><tbody>" +
                       "<tr><td>RowA</td></tr><tr><td>RowB</td></tr></tbody></table>";

            var pages = PageStreams(PdfAssert.ValidPdf(HtmlToPdf.Render(html)));

            PageOf(pages, "Head").Should().Be(PageOf(pages, "RowA"), $"spacer {spacer}px: the header stays with the first row");
        }
    }
}
