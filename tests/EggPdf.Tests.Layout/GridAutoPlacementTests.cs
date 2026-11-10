using System;
using System.Text;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class GridAutoPlacementTests
{
    private static LayoutBox Layout(string body) =>
        LayoutTestHelper.Layout("<html><body style='margin:0'>" + body + "</body></html>", 600, 800);

    private static string Items(int count, string style = "")
    {
        var html = new StringBuilder();
        for (int i = 0; i < count; i++)
            html.Append("<div id='i").Append(i).Append("' style='height:10px;").Append(style).Append("'>x</div>");
        return html.ToString();
    }

    [Fact]
    public void ColumnFlow_WithoutTemplateRows_PutsEachItemInANewColumn()
    {
        var root = Layout("<div style='display:grid;grid-auto-flow:column;width:300px'>" + Items(3) + "</div>");

        var a = root.FindById("i0")!;
        var b = root.FindById("i1")!;
        var c = root.FindById("i2")!;
        b.Y.Should().BeApproximately(a.Y, 0.5f);
        c.Y.Should().BeApproximately(a.Y, 0.5f);
        b.X.Should().BeGreaterThan(a.X);
        c.X.Should().BeGreaterThan(b.X);
    }

    [Fact]
    public void ColumnFlow_FirstImplicitColumn_IsSizedFromGridAutoColumns()
    {
        var root = Layout("<div style='display:grid;grid-auto-flow:column;grid-auto-columns:80px;width:400px'>" + Items(2) + "</div>");

        root.FindById("i0")!.Width.Should().BeApproximately(80f, 1f);
        root.FindById("i1")!.X.Should().BeApproximately(80f, 1f);
    }

    [Fact]
    public void RowSpanningItems_InOneColumn_NeverOverlap()
    {
        var root = Layout("<div style='display:grid;grid-template-columns:100px'>" + Items(30, "grid-row:span 3") + "</div>");

        for (int i = 1; i < 30; i++)
            root.FindById("i" + i)!.Y.Should().BeGreaterThan(root.FindById("i" + (i - 1))!.Y);
    }

    [Fact]
    public void ColumnSpanWiderThanTheGrid_AddsImplicitColumnsInTheFirstRow()
    {
        var root = Layout("<div style='display:grid;grid-template-columns:100px 100px;grid-auto-columns:100px;row-gap:10px'>" +
                          "<div id='wide' style='grid-column:span 3'>x</div><div id='next'>y</div></div>");

        var wide = root.FindById("wide")!;
        wide.Width.Should().BeApproximately(300f, 1f);
        root.FindById("next")!.Y.Should().BeGreaterThan(wide.Y);
    }

    [Fact]
    public void ColumnFlow_WithExplicitColumnsFull_GrowsImplicitColumns()
    {
        var root = Layout("<div style='display:grid;grid-auto-flow:column;grid-template-columns:100px;grid-template-rows:20px 20px;grid-auto-columns:50px'>" +
                          Items(3) + "</div>");

        var third = root.FindById("i2")!;
        third.X.Should().BeApproximately(100f, 1f);
        third.Width.Should().BeApproximately(50f, 1f);
    }

    [Fact]
    public void ItemsLockedToOneRow_TakeSuccessiveColumns()
    {
        var root = Layout("<div style='display:grid;grid-template-columns:100px 100px 100px'>" +
                          "<div id='a' style='grid-row:2'>a</div><div id='b' style='grid-row:2'>b</div><div id='c'>c</div></div>");

        var a = root.FindById("a")!;
        var b = root.FindById("b")!;
        b.Y.Should().BeApproximately(a.Y, 0.5f);
        b.X.Should().BeApproximately(100f, 1f);
        root.FindById("c")!.Y.Should().BeLessThan(a.Y);
    }

    [Fact]
    public void DenseFlow_BackfillsAnEarlierGap()
    {
        var root = Layout("<div style='display:grid;grid-template-columns:100px 100px 100px;grid-auto-flow:row dense'>" +
                          "<div id='a'>a</div><div id='wide' style='grid-column:span 3'>w</div><div id='b'>b</div></div>");

        var a = root.FindById("a")!;
        var b = root.FindById("b")!;
        b.Y.Should().BeApproximately(a.Y, 0.5f);
        b.X.Should().BeApproximately(100f, 1f);
    }

    [Fact]
    public void ColumnFlow_ThousandsOfItems_PlacementMemoryStaysLinear()
    {
        const int count = 6000;
        var body = "<div style='display:grid;grid-auto-flow:column;grid-template-rows:10px 10px'>" + Items(count) + "</div>";
        Layout("<div style='display:grid'>" + Items(10) + "</div>");

        long before = GC.GetAllocatedBytesForCurrentThread();
        var root = Layout(body);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        root.FindById("i" + (count - 1))!.Should().NotBeNull();
        allocated.Should().BeLessThan(48L * 1024 * 1024,
            "occupancy is stored per used column, not as an items-by-items matrix");
    }
}
