using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class GridLayoutTests
{
    private const float PageWidth = 600f;
    private const float PageHeight = 800f;
    private const float BodyMargin = 8f;
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
    public void GridContainer_BeforePseudoElement_OccupiesMarkerColumnNotRealChild()
    {
        var html = "<html><head><style>" +
            "li { display:grid; grid-template-columns: 14px 1fr; width: 400px; }" +
            "li::before { content: '-'; }" +
            "</style></head><body>" +
            "<ul><li><span>Some marker text that needs the wide column to wrap normally</span></li></ul>" +
            "</body></html>";
        var root = LayoutTestHelper.Layout(html, 600, 800);

        var span = root.FindByTag("span");
        span.Should().NotBeNull();
        span!.ContentWidth.Should().BeGreaterThan(200f,
            "in the custom list-marker idiom the ::before is a grid item in the 14px marker column, so the real " +
            "child lands in the 1fr column instead of being squeezed one word per line");
    }

    [Fact]
    public void GridContainer_ThreeColumnsWithGap_PlacesSixItemsInTwoRows()
    {
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px'>" +
            "<div>1</div><div>2</div><div>3</div>" +
            "<div>4</div><div>5</div><div>6</div>" +
            "</div>");

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCount(7, "the container plus six items");
        var items = divs.GetRange(1, 6);

        items[0].Width.Should().BeApproximately(184f, 1f, "three tracks share 584px less two 16px gaps");
        (items[1].X - items[0].X).Should().BeApproximately(200f, 1f, "each column starts one track and one gap after the last");
        (items[2].X - items[1].X).Should().BeApproximately(200f, 1f);
        items[3].X.Should().BeApproximately(items[0].X, 0.5f, "the fourth item wraps to the first column");
        (items[3].Y - items[0].Y).Should().BeGreaterThan(16f, "the second row sits below the first, past the gap");
        items[4].Y.Should().BeApproximately(items[3].Y, 0.5f);
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
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 200px 200px'>" +
            "<div style='height: 50px'>A</div>" +
            "<div style='height: 50px'>B</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(2);

        var item1 = container.Children[0];
        var item2 = container.Children[1];

        item1.Width.Should().BeApproximately(200f, 1f);
        item2.Width.Should().BeApproximately(200f, 1f);
        item2.X.Should().BeApproximately(item1.X + 200f, 1f);
        item1.Y.Should().BeApproximately(item2.Y, 1f, "both items are on the first row");
    }

    [Fact]
    public void Grid_FrUnits_DistributeSpace()
    {
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
        item2.X.Should().BeGreaterThan(item1.X);
    }

    [Fact]
    public void Grid_Gap_AddSpacing()
    {
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

        float expectedColWidth = (ContainerWidth - 20f) / 2f;
        item1.Width.Should().BeApproximately(expectedColWidth, 1f);
        item2.Width.Should().BeApproximately(expectedColWidth, 1f);

        float columnGap = item2.X - (item1.X + item1.Width);
        columnGap.Should().BeApproximately(20f, 1f);

        float rowGap = item3.Y - (item1.Y + item1.Height);
        rowGap.Should().BeApproximately(20f, 1f);
    }

    [Fact]
    public void Grid_ExplicitPlacement_Works()
    {
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 1fr 1fr 1fr'>" +
            "<div style='grid-column: 2 / 3; grid-row: 1 / 2; height: 50px'>A</div>" +
            "<div style='height: 50px'>B</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(2);

        var itemA = container.Children[0];
        var itemB = container.Children[1];

        float colWidth = ContainerWidth / 3f;
        itemA.X.Should().BeApproximately(BodyMargin + colWidth, 2f, "grid-column: 2 / 3 places A in the second column");
        itemB.X.Should().BeApproximately(BodyMargin, 2f, "B auto-places into the first free cell, column 1 of row 1");
    }

    [Fact]
    public void Grid_SpanColumns()
    {
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

        wide.Width.Should().BeApproximately(colWidth * 2f, 2f);
        narrow.Width.Should().BeApproximately(colWidth, 2f);
        wide.Y.Should().BeApproximately(narrow.Y, 1f);
    }

    [Fact]
    public void Grid_ThreeByTwo_Layout()
    {
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

        item1.Y.Should().BeApproximately(item2.Y, 1f);
        item2.Y.Should().BeApproximately(item3.Y, 1f);
        item4.Y.Should().BeApproximately(item5.Y, 1f);
        item5.Y.Should().BeApproximately(item6.Y, 1f);
        item4.Y.Should().BeGreaterThan(item1.Y, "six items in three columns fill two rows");

        item2.X.Should().BeGreaterThan(item1.X);
        item3.X.Should().BeGreaterThan(item2.X);
        item5.X.Should().BeGreaterThan(item4.X);
        item6.X.Should().BeGreaterThan(item5.X);

        item1.Width.Should().BeApproximately(colWidth, 2f);
        item4.Width.Should().BeApproximately(colWidth, 2f);
    }

    [Fact]
    public void Grid_AutoFlow_Column()
    {
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

        item1.X.Should().BeApproximately(item2.X, 1f, "column flow fills the first column with items 1 and 2");
        item3.X.Should().BeApproximately(item4.X, 1f, "and the second with items 3 and 4");
        item3.X.Should().BeGreaterThan(item1.X);
        item2.Y.Should().BeGreaterThan(item1.Y);
    }

    [Fact]
    public void Grid_MixedUnits_PxAndFr()
    {
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 200px 1fr'>" +
            "<div style='height: 50px'>Fixed</div>" +
            "<div style='height: 50px'>Flex</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(2);

        var fixedItem = container.Children[0];
        var flexItem = container.Children[1];

        fixedItem.Width.Should().BeApproximately(200f, 1f);
        flexItem.Width.Should().BeApproximately(ContainerWidth - 200f, 2f, "1fr takes what the 200px column leaves");
        flexItem.X.Should().BeApproximately(fixedItem.X + 200f, 1f);
    }

    [Fact]
    public void Grid_AutoRows_SizedFromContent()
    {
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 1fr 1fr'>" +
            "<div style='height: 80px'>Tall</div>" +
            "<div style='height: 40px'>Short</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(2);

        var tall = container.Children[0];
        var shortItem = container.Children[1];

        tall.Y.Should().BeApproximately(shortItem.Y, 1f);
        tall.Height.Should().BeApproximately(80f, 1f, "an auto row fits its tallest item");
        shortItem.Height.Should().Be(40f, "an explicit height is kept rather than stretched");
    }

    [Fact]
    public void Grid_Repeat_ExpandsCorrectly()
    {
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

        container.Children[1].X.Should().BeGreaterThan(container.Children[0].X);
        container.Children[2].X.Should().BeGreaterThan(container.Children[1].X);
    }

    [Fact]
    public void Grid_RowGapAndColumnGap_Separate()
    {
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

        float colGap = itemB.X - (itemA.X + itemA.Width);
        colGap.Should().BeApproximately(30f, 1f);

        float rowGapActual = itemC.Y - (itemA.Y + itemA.Height);
        rowGapActual.Should().BeApproximately(10f, 1f);
    }

    [Fact]
    public void Grid_ExplicitRowHeight()
    {
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 1fr; grid-template-rows: 100px 200px'>" +
            "<div>Row1</div>" +
            "<div>Row2</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(2);

        var row1 = container.Children[0];
        var row2 = container.Children[1];

        row1.Height.Should().BeApproximately(100f, 1f);
        row2.Height.Should().BeApproximately(200f, 1f);
        row2.Y.Should().BeApproximately(row1.Y + 100f, 1f);
    }

    [Fact]
    public void Grid_ContainerHeight_ComputedFromChildren()
    {
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 1fr 1fr'>" +
            "<div style='height: 60px'>A</div>" +
            "<div style='height: 60px'>B</div>" +
            "<div style='height: 40px'>C</div>" +
            "<div style='height: 40px'>D</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];

        container.Height.Should().BeApproximately(100f, 2f, "an auto-height grid fits its 60px and 40px rows");
    }

    [Fact]
    public void Grid_PercentageWidth_Columns()
    {
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
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: repeat(auto-fill, minmax(100px, 1fr))'>" +
            "<div style='height: 50px'>1</div>" +
            "<div style='height: 50px'>2</div>" +
            "<div style='height: 50px'>3</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(3);

        float expectedColWidth = ContainerWidth / 5f;
        container.Children[0].Width.Should().BeApproximately(expectedColWidth, 2f,
            "auto-fill makes floor(584 / 100) = 5 columns and keeps the two empty ones");
        container.Children[1].Width.Should().BeApproximately(expectedColWidth, 2f);
        container.Children[2].Width.Should().BeApproximately(expectedColWidth, 2f);

        container.Children[0].Y.Should().BeApproximately(container.Children[1].Y, 1f);
        container.Children[1].Y.Should().BeApproximately(container.Children[2].Y, 1f);

        container.Children[1].X.Should().BeGreaterThan(container.Children[0].X);
        container.Children[2].X.Should().BeGreaterThan(container.Children[1].X);
    }

    [Fact]
    public void Grid_AutoFit_CollapsesEmptyTracks()
    {
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: repeat(auto-fit, minmax(100px, 1fr))'>" +
            "<div style='height: 50px'>1</div>" +
            "<div style='height: 50px'>2</div>" +
            "</div>");

        var container = root.FindAllByTag("div")[0];
        container.Children.Count.Should().Be(2);

        float expectedColWidth = ContainerWidth / 2f;
        container.Children[0].Width.Should().BeApproximately(expectedColWidth, 2f,
            "auto-fit collapses the empty tracks, so two items share the whole width");
        container.Children[1].Width.Should().BeApproximately(expectedColWidth, 2f);

        container.Children[1].X.Should().BeGreaterThan(container.Children[0].X);
    }

    [Fact]
    public void Grid_AutoFill_WithNoItems_IsAnEmptyZeroHeightContainer()
    {
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: repeat(auto-fill, minmax(80px, 1fr))'></div>");

        var grid = root.FindAllByTag("div")[0];
        grid.Children.Should().BeEmpty();
        grid.Height.Should().Be(0f, "an empty grid has no rows to size");
        grid.Width.Should().BeApproximately(ContainerWidth, 1f);
    }

    [Fact]
    public void Subgrid_Columns_InheritTheParentsTracks()
    {
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 200px 200px'>" +
            "  <div style='display: grid; grid-template-columns: subgrid; grid-column: 1 / 3'>" +
            "    <div>A</div><div>B</div>" +
            "  </div>" +
            "</div>");

        var divs = root.FindAllByTag("div");
        var sub = divs[1]; var a = divs[2]; var b = divs[3];

        sub.Width.Should().BeApproximately(400f, 1f, "the subgrid spans both parent columns");
        a.Width.Should().BeApproximately(200f, 1f, "A takes the parent's first track");
        (b.X - a.X).Should().BeApproximately(200f, 1f, "B sits in the parent's second track");
    }

    [Fact]
    public void Subgrid_ChildrenPositioned()
    {
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 200px 200px'>" +
            "  <div style='display: grid; grid-template-columns: subgrid; grid-column: 1 / 3'>" +
            "    <div>A</div><div>B</div>" +
            "  </div>" +
            "</div>");

        var allDivs = root.FindAllByTag("div");
        allDivs.Should().HaveCountGreaterOrEqualTo(2, "subgrid must produce layout boxes for its children");
    }

    [Fact]
    public void Subgrid_Rows_InheritTheParentsTracks()
    {
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-rows: 100px 100px'>" +
            "  <div style='display: grid; grid-template-rows: subgrid; grid-row: 1 / 3'>" +
            "    <div>X</div><div>Y</div>" +
            "  </div>" +
            "</div>");

        var divs = root.FindAllByTag("div");
        var x = divs[2]; var y = divs[3];

        (y.Y - x.Y).Should().BeApproximately(100f, 1f, "the subgrid's rows are the parent's 100px tracks");
    }

    [Fact]
    public void Subgrid_InheritsParentColumnWidths()
    {
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 200px 200px'>" +
            "  <div id='sub' style='display: grid; grid-template-columns: subgrid; grid-column: 1 / 3'>" +
            "    <div>A</div><div>B</div>" +
            "  </div>" +
            "</div>");

        var subgridItem = root.FindById("sub")!;
        subgridItem.Children.Should().HaveCount(2);

        subgridItem.Children[0].Width.Should().BeApproximately(200f, 10f,
            "subgrid child A should inherit parent 200px column width");
        subgridItem.Children[1].Width.Should().BeApproximately(200f, 10f,
            "subgrid child B should inherit parent 200px column width");
    }

    [Fact]
    public void Subgrid_InheritsParentColumnGap_WhenNotOverridden()
    {
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 100px 100px; column-gap: 20px'>" +
            "  <div id='sub' style='display: grid; grid-template-columns: subgrid; grid-column: 1 / 3'>" +
            "    <div>A</div><div>B</div>" +
            "  </div>" +
            "</div>");

        var subgridItem = root.FindById("sub")!;
        subgridItem.Children.Should().HaveCount(2);

        float gapBetween = subgridItem.Children[1].X - (subgridItem.Children[0].X + subgridItem.Children[0].Width);
        gapBetween.Should().BeApproximately(20f, 2f,
            "a subgrid with no gap of its own must inherit the parent's 20px column-gap");
    }

    [Fact]
    public void Subgrid_OffsetSpan_UsesCorrectParentColumnSlice()
    {
        var root = LayoutGrid(
            "<div style='display: grid; grid-template-columns: 200px 100px 50px'>" +
            "  <div id='sub' style='display: grid; grid-template-columns: subgrid; grid-column: 2 / 4'>" +
            "    <div>A</div><div>B</div>" +
            "  </div>" +
            "</div>");

        var subgridItem = root.FindById("sub")!;
        subgridItem.Children.Should().HaveCount(2);

        subgridItem.Children[0].Width.Should().BeApproximately(100f, 5f,
            "a subgrid spanning columns 2-3 slices the parent's tracks from its own offset: 100px, not the first 200px");
        subgridItem.Children[1].Width.Should().BeApproximately(50f, 5f,
            "the subgrid's second slot should take the parent's 3rd column width (50px)");
    }

    [Fact]
    public void GridAutoFlow_Dense_DoesNotCrash()
    {
        var root = LayoutGrid(
            "<div style='display:grid; grid-template-columns: repeat(3,100px); grid-auto-flow: dense'>" +
            "<div style='grid-column: span 2'>A</div><div>B</div><div>C</div><div>D</div></div>");
        var grid = root.FindAllByTag("div")[0];
        grid.Should().NotBeNull();
        grid.Children.Should().NotBeEmpty();
    }

    [Fact]
    public void GridAutoFlow_Dense_BackfillsGapLeftBySkippedFullRowItem()
    {
        var root = LayoutGrid(
            "<div style='display:grid; grid-template-columns: 100px 100px 100px; " +
            "grid-auto-flow: row dense; width:300px'>" +
            "<div id='one'>1</div>" +
            "<div style='grid-column: span 3'>full</div>" +
            "<div id='three'>3</div>" +
            "</div>");

        var one = root.FindById("one");
        var three = root.FindById("three");
        one.Should().NotBeNull();
        three.Should().NotBeNull();

        three!.Y.Should().BeApproximately(one!.Y, 1f,
            "the span-3 item skipped to row 1, and dense packing restarts from the origin, backfilling row 0 column 1");
        three.X.Should().BeGreaterThan(one.X + 50f,
            "the backfilled item lands in the second column, not the first");
    }

    [Fact]
    public void GridAutoFlow_Sparse_LeavesGapAndContinuesForward()
    {
        var root = LayoutGrid(
            "<div style='display:grid; grid-template-columns: 100px 100px 100px; width:300px'>" +
            "<div id='one'>1</div>" +
            "<div style='grid-column: span 3'>full</div>" +
            "<div id='three'>3</div>" +
            "</div>");

        var one = root.FindById("one");
        var three = root.FindById("three");
        one.Should().NotBeNull();
        three.Should().NotBeNull();

        three!.Y.Should().BeGreaterThan(one!.Y + 10f,
            "sparse (default) packing must not backfill row0 -- it continues forward past the full row");
    }

    [Fact]
    public void GridAutoRows_SizesImplicitRows()
    {
        var root = LayoutGrid(
            "<div style='display:grid; grid-template-columns: 100px; grid-auto-rows: 50px; width:100px'>" +
            "<div id='a'>A</div>" +
            "<div id='b'>B</div>" +
            "</div>");

        var a = root.FindById("a");
        var b = root.FindById("b");
        a.Should().NotBeNull();
        b.Should().NotBeNull();
        (b!.Y - a!.Y).Should().BeApproximately(50f, 2f,
            "with no grid-template-rows both rows are implicit and sized from grid-auto-rows, not content height");
    }

    [Fact]
    public void GridAutoColumns_SizesAndGrowsImplicitColumnsInColumnFlow()
    {
        var root = LayoutGrid(
            "<div style='display:grid; grid-auto-flow: column; grid-auto-columns: 80px; " +
            "grid-template-rows: 40px 40px; width:400px'>" +
            "<div id='a'>A</div>" +
            "<div id='b'>B</div>" +
            "<div id='c'>C</div>" +
            "</div>");

        var a = root.FindById("a");
        var b = root.FindById("b");
        var c = root.FindById("c");
        a.Should().NotBeNull();
        b.Should().NotBeNull();
        c.Should().NotBeNull();

        c!.X.Should().BeGreaterThan(a!.X, "A and B fill the two rows of the first column, so C grows a second column");
        c.Width.Should().BeApproximately(80f, 1f,
            "the newly-grown implicit column must be sized from grid-auto-columns");
    }
}
