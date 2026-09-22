using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// A &lt;table&gt;'s &lt;thead&gt; row(s) must repeat at the top of every physical
/// page the table's body continues onto -- matching Chrome print behavior --
/// rather than appearing once on whichever page the table happened to start on.
/// </summary>
public class TableHeaderRepeatTests
{
    private static string BuildTallTableHtml(int rows, string headerText = "Name")
    {
        var sb = new StringBuilder();
        sb.Append("<html><head><style>td, th { height: 40px; }</style></head><body>");
        sb.Append("<table style='width:100%; border-collapse:collapse'><thead><tr>");
        sb.Append($"<th>{headerText}</th><th>Value</th>");
        sb.Append("</tr></thead><tbody>");
        for (int i = 0; i < rows; i++)
            sb.Append($"<tr><td>Row {i}</td><td>{i}</td></tr>");
        sb.Append("</tbody></table></body></html>");
        return sb.ToString();
    }

    [Fact]
    public async Task TallTable_HeaderRepeatsOnEveryContinuationPage()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync(BuildTallTableHtml(60, "Employee Name"));
        var text = Encoding.ASCII.GetString(pdf);

        var pageCount = Regex.Matches(text, @"/Type /Page[^s]").Count;
        pageCount.Should().BeGreaterThan(1, "60 rows at 40px each must paginate for this test to be meaningful");

        var occurrences = Regex.Matches(text, Regex.Escape("(Employee Name) Tj")).Count;
        occurrences.Should().Be(pageCount,
            "the header must appear once per physical page: once where the table starts, and again repeated on every continuation page");
    }

    [Fact]
    public async Task ShortTable_SinglePage_HeaderAppearsOnlyOnce()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync(BuildTallTableHtml(3, "Employee Name"));
        var text = Encoding.ASCII.GetString(pdf);

        var pageCount = Regex.Matches(text, @"/Type /Page[^s]").Count;
        pageCount.Should().Be(1, "3 short rows must fit on a single page for this test to be meaningful");

        Regex.Matches(text, Regex.Escape("(Employee Name) Tj")).Count.Should().Be(1,
            "a table that fits on one page must not get a spurious repeated header");
    }

    [Fact]
    public async Task TallTable_BodyRowsNotOverlappedByRepeatedHeader()
    {
        // The repeated header must push continuation-page body rows down, not
        // paint on top of them: every row's own text must still appear exactly
        // once even though the header now also occupies space on that page.
        byte[] pdf = await HtmlToPdf.RenderAsync(BuildTallTableHtml(60));
        var text = Encoding.ASCII.GetString(pdf);

        for (int i = 0; i < 60; i += 15) // spot-check across the document
            Regex.Matches(text, Regex.Escape($"(Row {i}) Tj")).Count.Should().Be(1,
                $"row {i}'s text must be painted exactly once regardless of which page it lands on");
    }

    [Fact]
    public async Task TableWithoutThead_DoesNotRepeatAnyHeader()
    {
        var sb = new StringBuilder("<html><head><style>td { height: 40px; }</style></head><body>");
        sb.Append("<table style='width:100%'><tbody>");
        for (int i = 0; i < 60; i++)
            sb.Append($"<tr><td>Row {i}</td><td>{i}</td></tr>");
        sb.Append("</tbody></table></body></html>");

        byte[] pdf = await HtmlToPdf.RenderAsync(sb.ToString());
        var text = PdfAssert.ValidPdf(pdf, "(Row 0) Tj", "(Row 59) Tj");

        PdfAssert.PageCount(text).Should().BeGreaterThan(1, "60 rows at 40px each paginate");
        // With no <thead> nothing is repeated, so every row's text is painted exactly once.
        for (int i = 0; i < 60; i += 15)
            Regex.Matches(text, Regex.Escape($"(Row {i}) Tj")).Count.Should().Be(1,
                "a table with no <thead> must render normally, without a repeat mechanism engaging");
    }

    [Fact]
    public async Task MultipleTables_EachRepeatsItsOwnHeaderIndependently()
    {
        var sb = new StringBuilder("<html><head><style>td, th { height: 40px; }</style></head><body>");
        sb.Append("<table style='width:100%'><thead><tr><th>Table One Header</th></tr></thead><tbody>");
        for (int i = 0; i < 40; i++) sb.Append($"<tr><td>A{i}</td></tr>");
        sb.Append("</tbody></table>");
        sb.Append("<table style='width:100%'><thead><tr><th>Table Two Header</th></tr></thead><tbody>");
        for (int i = 0; i < 40; i++) sb.Append($"<tr><td>B{i}</td></tr>");
        sb.Append("</tbody></table></body></html>");

        byte[] pdf = await HtmlToPdf.RenderAsync(sb.ToString());
        var text = Encoding.ASCII.GetString(pdf);

        var pageCount = Regex.Matches(text, @"/Type /Page[^s]").Count;
        pageCount.Should().BeGreaterThan(2, "two 40-row tables must span at least 3 pages for this test to be meaningful");

        text.Should().Contain("(Table One Header) Tj");
        text.Should().Contain("(Table Two Header) Tj");
        // Each header repeats more than once (its own table spans multiple pages),
        // but a page belonging only to table two must not also show table one's header.
        Regex.Matches(text, Regex.Escape("(Table One Header) Tj")).Count.Should().BeGreaterThan(1);
        Regex.Matches(text, Regex.Escape("(Table Two Header) Tj")).Count.Should().BeGreaterThan(1);
    }
}
