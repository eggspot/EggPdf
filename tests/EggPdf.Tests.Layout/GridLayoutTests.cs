using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class GridLayoutTests
{
    private const float PageWidth = 600f;
    private const float PageHeight = 800f;
    private const float BodyMargin = 8f;
    // Container content width = 600 - 2*8 = 584
    private const float ContainerWidth = PageWidth - 2 * BodyMargin;

    private LayoutBox LayoutGrid(string html)
        => LayoutTestHelper.Layout(html, PageWidth, PageHeight);

    [Fact]
    public void GridContainer_HasGridDisplay()
    {
        var root = LayoutGrid(
            "<div style='display: grid'><div>Item</div></div>");

        var grid = root.FindAllByTag("div")[0];
        grid.Should().NotBeNull();
        grid.Style.Display.Should().Be("grid");
    }

    [Fact]
    public void GridContainer_ChildrenLaidOut()
    {
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 1fr 1fr'>" +
            "<div>Item 1</div><div>Item 2</div>" +
            "</div>");

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCountGreaterOrEqualTo(3);
    }

    [Fact]
    public void GridContainer_DoesNotCrash()
    {
        var act = () => LayoutGrid(
            "<div style='display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px'>" +
            "<div>1</div><div>2</div><div>3</div>" +
            "<div>4</div><div>5</div><div>6</div>" +
            "</div>");

        act.Should().NotThrow();
    }

    [Fact]
    public void GridTemplateColumns_RecognizedAsProperty()
    {
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 200px 1fr'>" +
            "<div>A</div><div>B</div>" +
            "</div>");

        var grid = root.FindAllByTag("div")[0];
        grid.Style.Get("grid-template-columns").Should().Be("200px 1fr");
    }

    [Fact]
    public void InlineGrid_Recognized()
    {
        var root = LayoutGrid(
            "<span style='display: inline-grid'><span>A</span></span>");

        var span = root.FindAllByTag("span")[0];
        span.Should().NotBeNull();
    }

    [Fact]
    public void Grid_TwoColumns_FixedWidth()
    {
        // Two items in a 2-column grid, each 200px wide
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 200px 200px'>" +
            "<div style='height: 50px'>A</div>" +
            "<div style='height: 50px'>B</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(2);

        var item1 = container.Children[0];
        var item2 = container.Children[1];

        // Items should be side by side
        item1.Width.Should().BeApproximately(200f, 1f);
        item2.Width.Should().BeApproximately(200f, 1f);

        // Item2 should be to the right of item1
        item2.X.Should().BeApproximately(item1.X + 200f, 1f);

        // Same Y position
        item1.Y.Should().BeApproximately(item2.Y, 1f);
    }

    [Fact]
    public void Grid_FrUnits_DistributeSpace()
    {
        // 1fr 2fr should distribute container width proportionally (1:2)
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 1fr 2fr'>" +
            "<div style='height: 50px'>A</div>" +
            "<div style='height: 50px'>B</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(2);

        var item1 = container.Children[0];
        var item2 = container.Children[1];

        float totalFr = 3f;
        float expected1 = ContainerWidth / totalFr;
        float expected2 = ContainerWidth * 2f / totalFr;

        item1.Width.Should().BeApproximately(expected1, 1f);
        item2.Width.Should().BeApproximately(expected2, 1f);

        // Item2 should be to the right
        item2.X.Should().BeGreaterThan(item1.X);
    }

    [Fact]
    public void Grid_Gap_AddSpacing()
    {
        // 2 columns with 20px gap
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 1fr 1fr; gap: 20px'>" +
            "<div style='height: 50px'>A</div>" +
            "<div style='height: 50px'>B</div>" +
            "<div style='height: 50px'>C</div>" +
            "<div style='height: 50px'>D</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(4);

        var item1 = container.Children[0];
        var item2 = container.Children[1];
        var item3 = container.Children[2];

        // Column width = (584 - 20) / 2 = 282
        float expectedColWidth = (ContainerWidth - 20f) / 2f;
        item1.Width.Should().BeApproximately(expectedColWidth, 1f);
        item2.Width.Should().BeApproximately(expectedColWidth, 1f);

        // Gap between columns
        float gapBetween = item2.X - (item1.X + item1.Width);
        gapBetween.Should().BeApproximately(20f, 1f);

        // Gap between rows
        float rowGap = item3.Y - (item1.Y + item1.Height);
        rowGap.Should().BeApproximately(20f, 1f);
    }

    [Fact]
    public void Grid_ExplicitPlacement_Works()
    {
        // Place item at specific grid position using grid-column/grid-row
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 1fr 1fr 1fr'>" +
            "<div style='grid-column: 2 / 3; grid-row: 1 / 2; height: 50px'>A</div>" +
            "<div style='height: 50px'>B</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(2);

        var itemA = container.Children[0];
        var itemB = container.Children[1];

        // Item A should be in column 2 (0-based index 1)
        float colWidth = ContainerWidth / 3f;
        itemA.X.Should().BeApproximately(BodyMargin + colWidth, 2f);

        // Item B should auto-place to the first available cell (column 1 or 3)
        // Since column 2 row 1 is taken, B goes to column 1 row 1
        itemB.X.Should().BeApproximately(BodyMargin, 2f);
    }

    [Fact]
    public void Grid_SpanColumns()
    {
        // Item spanning 2 columns
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 1fr 1fr 1fr'>" +
            "<div style='grid-column: span 2; height: 50px'>Wide</div>" +
            "<div style='height: 50px'>Narrow</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(2);

        var wide = container.Children[0];
        var narrow = container.Children[1];

        float colWidth = ContainerWidth / 3f;

        // Spanning item should be ~2/3 of container width
        wide.Width.Should().BeApproximately(colWidth * 2f, 2f);

        // Narrow should be ~1/3
        narrow.Width.Should().BeApproximately(colWidth, 2f);

        // Both on the same row
        wide.Y.Should().BeApproximately(narrow.Y, 1f);
    }

    [Fact]
    public void Grid_ThreeByTwo_Layout()
    {
        // 3 columns, 6 items = 2 rows auto-placed
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 1fr 1fr 1fr'>" +
            "<div style='height: 40px'>1</div>" +
            "<div style='height: 40px'>2</div>" +
            "<div style='height: 40px'>3</div>" +
            "<div style='height: 40px'>4</div>" +
            "<div style='height: 40px'>5</div>" +
            "<div style='height: 40px'>6</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(6);

        var item1 = container.Children[0];
        var item2 = container.Children[1];
        var item3 = container.Children[2];
        var item4 = container.Children[3];
        var item5 = container.Children[4];
        var item6 = container.Children[5];

        float colWidth = ContainerWidth / 3f;

        // Row 1: items 1, 2, 3 should be side by side
        item1.Y.Should().BeApproximately(item2.Y, 1f);
        item2.Y.Should().BeApproximately(item3.Y, 1f);

        // Row 2: items 4, 5, 6 should be side by side
        item4.Y.Should().BeApproximately(item5.Y, 1f);
        item5.Y.Should().BeApproximately(item6.Y, 1f);

        // Row 2 should be below row 1
        item4.Y.Should().BeGreaterThan(item1.Y);

        // Items in each row at correct horizontal positions
        item2.X.Should().BeGreaterThan(item1.X);
        item3.X.Should().BeGreaterThan(item2.X);
        item5.X.Should().BeGreaterThan(item4.X);
        item6.X.Should().BeGreaterThan(item5.X);

        // Each item should be approximately one-third width
        item1.Width.Should().BeApproximately(colWidth, 2f);
        item4.Width.Should().BeApproximately(colWidth, 2f);
    }

    [Fact]
    public void Grid_AutoFlow_Column()
    {
        // grid-auto-flow: column should fill columns first
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 1fr 1fr; grid-template-rows: 50px 50px; grid-auto-flow: column'>" +
            "<div>1</div><div>2</div><div>3</div><div>4</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(4);

        var item1 = container.Children[0];
        var item2 = container.Children[1];
        var item3 = container.Children[2];
        var item4 = container.Children[3];

        // Column-first flow: items 1,2 in column 1, items 3,4 in column 2
        // Item1 and item2 should be in the same column (same X)
        item1.X.Should().BeApproximately(item2.X, 1f);

        // Item3 and item4 should be in the same column (same X)
        item3.X.Should().BeApproximately(item4.X, 1f);

        // Column 2 should be to the right of column 1
        item3.X.Should().BeGreaterThan(item1.X);

        // Items 1 and 2 should be at different Y positions (stacked)
        item2.Y.Should().BeGreaterThan(item1.Y);
    }

    [Fact]
    public void Grid_MixedUnits_PxAndFr()
    {
        // 200px fixed + 1fr flexible
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 200px 1fr'>" +
            "<div style='height: 50px'>Fixed</div>" +
            "<div style='height: 50px'>Flex</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(2);

        var fixedItem = container.Children[0];
        var flexItem = container.Children[1];

        // Fixed column should be 200px
        fixedItem.Width.Should().BeApproximately(200f, 1f);

        // Flex column should take remaining space: 584 - 200 = 384
        flexItem.Width.Should().BeApproximately(ContainerWidth - 200f, 2f);

        // Side by side
        flexItem.X.Should().BeApproximately(fixedItem.X + 200f, 1f);
    }

    [Fact]
    public void Grid_AutoRows_SizedFromContent()
    {
        // No explicit row heights, should size from content
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 1fr 1fr'>" +
            "<div style='height: 80px'>Tall</div>" +
            "<div style='height: 40px'>Short</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(2);

        var tall = container.Children[0];
        var short_ = container.Children[1];

        // Both in the same row - the row height should accommodate the tallest item
        tall.Y.Should().BeApproximately(short_.Y, 1f);

        // The tall item retains its height
        tall.Height.Should().BeApproximately(80f, 1f);

        // The short item has explicit height=40px so it keeps that
        short_.Height.Should().Be(40f);
    }

    [Fact]
    public void Grid_Repeat_ExpandsCorrectly()
    {
        // repeat(3, 1fr) should create 3 equal columns
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: repeat(3, 1fr)'>" +
            "<div style='height: 50px'>1</div>" +
            "<div style='height: 50px'>2</div>" +
            "<div style='height: 50px'>3</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(3);

        float colWidth = ContainerWidth / 3f;

        for (int i = 0; i < 3; i++)
        {
            container.Children[i].Width.Should().BeApproximately(colWidth, 2f);
        }

        // Side by side
        container.Children[1].X.Should().BeGreaterThan(container.Children[0].X);
        container.Children[2].X.Should().BeGreaterThan(container.Children[1].X);
    }

    [Fact]
    public void Grid_RowGapAndColumnGap_Separate()
    {
        // row-gap: 10px; column-gap: 30px
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 1fr 1fr; row-gap: 10px; column-gap: 30px'>" +
            "<div style='height: 50px'>A</div>" +
            "<div style='height: 50px'>B</div>" +
            "<div style='height: 50px'>C</div>" +
            "<div style='height: 50px'>D</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        var itemA = container.Children[0];
        var itemB = container.Children[1];
        var itemC = container.Children[2];

        // Column gap
        float colGap = itemB.X - (itemA.X + itemA.Width);
        colGap.Should().BeApproximately(30f, 1f);

        // Row gap
        float rowGapActual = itemC.Y - (itemA.Y + itemA.Height);
        rowGapActual.Should().BeApproximately(10f, 1f);
    }

    [Fact]
    public void Grid_ExplicitRowHeight()
    {
        // grid-template-rows: 100px 200px
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 1fr; grid-template-rows: 100px 200px'>" +
            "<div>Row1</div>" +
            "<div>Row2</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(2);

        var row1 = container.Children[0];
        var row2 = container.Children[1];

        // Row heights from template
        row1.Height.Should().BeApproximately(100f, 1f);
        row2.Height.Should().BeApproximately(200f, 1f);

        // Row2 below row1
        row2.Y.Should().BeApproximately(row1.Y + 100f, 1f);
    }

    [Fact]
    public void Grid_ContainerHeight_ComputedFromChildren()
    {
        // Container with auto height should size to fit children
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 1fr 1fr'>" +
            "<div style='height: 60px'>A</div>" +
            "<div style='height: 60px'>B</div>" +
            "<div style='height: 40px'>C</div>" +
            "<div style='height: 40px'>D</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];

        // 2 rows: first row 60px, second row 40px = 100px total content
        container.Height.Should().BeApproximately(100f, 2f);
    }

    [Fact]
    public void Grid_PercentageWidth_Columns()
    {
        // 50% + 50% columns
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 50% 50%'>" +
            "<div style='height: 50px'>A</div>" +
            "<div style='height: 50px'>B</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(2);

        var itemA = container.Children[0];
        var itemB = container.Children[1];

        float halfWidth = ContainerWidth * 0.5f;
        itemA.Width.Should().BeApproximately(halfWidth, 1f);
        itemB.Width.Should().BeApproximately(halfWidth, 1f);
    }

    [Fact]
    public void Grid_AutoFill_CreatesCorrectColumnCount()
    {
        // With 584px container and 100px min, auto-fill should create floor(584/100)=5 columns
        // 3 items fill columns 1-3; columns 4-5 exist but are empty
        // Each column = 584/5 = 116.8px
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: repeat(auto-fill, minmax(100px, 1fr))'>" +
            "<div style='height: 50px'>1</div>" +
            "<div style='height: 50px'>2</div>" +
            "<div style='height: 50px'>3</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(3);

        float expectedColWidth = ContainerWidth / 5f;
        container.Children[0].Width.Should().BeApproximately(expectedColWidth, 2f);
        container.Children[1].Width.Should().BeApproximately(expectedColWidth, 2f);
        container.Children[2].Width.Should().BeApproximately(expectedColWidth, 2f);

        // All items on the same row (row 1)
        container.Children[0].Y.Should().BeApproximately(container.Children[1].Y, 1f);
        container.Children[1].Y.Should().BeApproximately(container.Children[2].Y, 1f);

        // Items are side by side
        container.Children[1].X.Should().BeGreaterThan(container.Children[0].X);
        container.Children[2].X.Should().BeGreaterThan(container.Children[1].X);
    }

    [Fact]
    public void Grid_AutoFit_CollapsesEmptyTracks()
    {
        // auto-fit: empty tracks collapse, so 2 items share full container width
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: repeat(auto-fit, minmax(100px, 1fr))'>" +
            "<div style='height: 50px'>1</div>" +
            "<div style='height: 50px'>2</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(2);

        // 2 items share full width: 584/2 = 292px each
        float expectedColWidth = ContainerWidth / 2f;
        container.Children[0].Width.Should().BeApproximately(expectedColWidth, 2f);
        container.Children[1].Width.Should().BeApproximately(expectedColWidth, 2f);

        // Side by side
        container.Children[1].X.Should().BeGreaterThan(container.Children[0].X);
    }

    [Fact]
    public void Grid_AutoFill_DoesNotCrash_WithNoItems()
    {
        var act = () => LayoutGrid(
            "<div style='display: grid; grid-template-columns: repeat(auto-fill, minmax(80px, 1fr))'></div>");
        act.Should().NotThrow();
    }
}
