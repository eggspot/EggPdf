using System.Linq;
using EggPdf.Html;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Html;

public class ImpliedEndTagTests
{
    private static HtmlElement Body(string bodyHtml) => HtmlParser.Parse("<html><body>" + bodyHtml + "</body></html>").Body!;

    private static HtmlElement[] Elements(HtmlNode parent, string tag) =>
        parent.ChildNodes.OfType<HtmlElement>().Where(e => e.TagName == tag).ToArray();

    private static string Text(HtmlNode node) =>
        string.Concat(node.ChildNodes.Select(c => c is HtmlTextNode t ? t.Data : Text(c)));

    [Fact]
    public void TableCellsWithoutEndTags_AreSiblings()
    {
        var body = Body("<table><tr><td>A1<td>B1<tr><td>A2<td>B2</table>");

        var table = Elements(body, "table").Single();
        var tbody = Elements(table, "tbody").Single();
        var rows = Elements(tbody, "tr");
        rows.Should().HaveCount(2);
        Elements(rows[0], "td").Select(Text).Should().Equal("A1", "B1");
        Elements(rows[1], "td").Select(Text).Should().Equal("A2", "B2");
    }

    [Fact]
    public void HeaderCellsWithoutEndTags_AreSiblings()
    {
        var body = Body("<table><thead><tr><th>Name<th>Qty<tbody><tr><td>Pen<td>2</table>");

        var table = Elements(body, "table").Single();
        Elements(table, "thead").Should().ContainSingle();
        Elements(table, "tbody").Should().ContainSingle();
        Elements(Elements(Elements(table, "thead")[0], "tr")[0], "th").Select(Text).Should().Equal("Name", "Qty");
    }

    [Fact]
    public void NestedTableInsideCell_DoesNotCloseOuterCell()
    {
        var body = Body("<table><tr><td>Outer<table><tr><td>Inner</table>After</td><td>Second</table>");

        var outerRow = Elements(Elements(Elements(body, "table")[0], "tbody")[0], "tr")[0];
        var cells = Elements(outerRow, "td");
        cells.Should().HaveCount(2);
        Text(cells[0]).Should().Contain("Inner").And.Contain("After");
        Text(cells[1]).Should().Be("Second");
    }

    [Fact]
    public void ListItemsWithoutEndTags_AreSiblings()
    {
        var body = Body("<ul><li>one<li>two<li>three</ul>");

        Elements(Elements(body, "ul")[0], "li").Select(Text).Should().Equal("one", "two", "three");
    }

    [Fact]
    public void NestedListItem_InsideNestedList_StaysNested()
    {
        var body = Body("<ul><li>one<ul><li>inner</ul><li>two</ul>");

        var outerItems = Elements(Elements(body, "ul")[0], "li");
        outerItems.Should().HaveCount(2);
        Elements(Elements(outerItems[0], "ul")[0], "li").Select(Text).Should().Equal("inner");
    }

    [Fact]
    public void DefinitionTermsAndDescriptionsWithoutEndTags_AreSiblings()
    {
        var body = Body("<dl><dt>Invoice no.<dd>INV-001<dt>Date<dd>2026-11-07</dl>");

        var dl = Elements(body, "dl").Single();
        dl.ChildNodes.OfType<HtmlElement>().Select(e => e.TagName + ":" + Text(e))
            .Should().Equal("dt:Invoice no.", "dd:INV-001", "dt:Date", "dd:2026-11-07");
    }

    [Fact]
    public void OptionsWithoutEndTags_AreSiblings()
    {
        var body = Body("<select><option>One<option>Two<optgroup label='g'><option>Three</select>");

        var select = Elements(body, "select").Single();
        Elements(select, "option").Select(Text).Should().Equal("One", "Two");
        Elements(Elements(select, "optgroup")[0], "option").Select(Text).Should().Equal("Three");
    }
}
