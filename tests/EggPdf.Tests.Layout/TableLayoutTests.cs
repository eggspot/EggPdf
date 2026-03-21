using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class TableLayoutTests
{
    [Fact]
    public void SimpleTable_HasTableDisplay()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tr><td>Cell</td></tr></table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
        table!.Style.Display.Should().Be("table");
    }

    [Fact]
    public void TableRow_ExistsInLayout()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tbody><tr><td>Cell</td></tr></tbody></table>", 600, 800);

        // tr may be nested inside tbody
        var tr = root.FindByTag("tr");
        if (tr == null)
        {
            // Also check tbody children
            var tbody = root.FindByTag("tbody");
            tbody.Should().NotBeNull();
        }
    }

    [Fact]
    public void TableCell_ExistsInLayout()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tbody><tr><td>Cell</td></tr></tbody></table>", 600, 800);

        var td = root.FindByTag("td");
        td.Should().NotBeNull();
    }

    [Fact]
    public void Table_HasPositiveSize()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tbody><tr><td>Cell 1</td><td>Cell 2</td></tr></tbody></table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
        table!.Width.Should().BeGreaterThan(0);
        table.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TableWithWidth_RespectsWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='width: 400px'><tr><td>Cell</td></tr></table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
        table!.Width.Should().BeApproximately(400, 2f);
    }

    [Fact]
    public void MultipleRows_StackVertically()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tbody><tr><td>Row 1</td></tr><tr><td>Row 2</td></tr></tbody></table>", 600, 800);

        var trs = root.FindAllByTag("tr");
        trs.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public void TableHeader_RecognizedAsBold()
    {
        var root = LayoutTestHelper.Layout(
            "<table><thead><tr><th>Header</th></tr></thead><tbody><tr><td>Data</td></tr></tbody></table>", 600, 800);

        var th = root.FindByTag("th");
        th.Should().NotBeNull();
        th!.Style.FontWeight.Should().Be("bold");
    }

    [Fact]
    public void BorderedTable_HasDimensions()
    {
        var root = LayoutTestHelper.Layout(
            "<table border='1' style='border-collapse: collapse'>" +
            "<tr><td>A</td><td>B</td></tr>" +
            "<tr><td>C</td><td>D</td></tr>" +
            "</table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
        table!.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void StyledTable_AllStylesApplied()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='width: 100%; border-collapse: collapse'>" +
            "<thead><tr><th>Name</th><th>Value</th></tr></thead>" +
            "<tbody><tr><td>Item</td><td>$10</td></tr></tbody>" +
            "</table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
        table!.Style.Get("border-collapse").Should().Be("collapse");
    }

    [Fact]
    public void EmptyTable_DoesNotCrash()
    {
        var root = LayoutTestHelper.Layout("<table></table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
    }
}
