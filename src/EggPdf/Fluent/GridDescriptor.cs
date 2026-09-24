using System.Globalization;
using EggPdf.Html.Dom;

namespace EggPdf.Fluent;

/// <summary>Builds the cells of a CSS grid container (<see cref="Container.Grid(int, System.Action{GridDescriptor})"/>).</summary>
public sealed class GridDescriptor
{
    private readonly BuilderContext _ctx;
    private readonly HtmlElement _grid;

    internal GridDescriptor(BuilderContext ctx, HtmlElement grid)
    {
        _ctx = ctx;
        _grid = grid;
    }

    /// <summary>Space between rows and columns (<c>gap</c>).</summary>
    public GridDescriptor Gap(Length gap)
    {
        _ctx.SetStyle(_grid, CssProp.Gap, gap.ToCss());
        return this;
    }

    /// <summary>Height of implicitly created rows (<c>grid-auto-rows</c>).</summary>
    public GridDescriptor AutoRows(Length height)
    {
        _ctx.SetStyle(_grid, CssProp.GridAutoRows, height.ToCss());
        return this;
    }

    /// <summary>Appends a single-column cell configured inside <paramref name="build"/> and returns the grid, so cells chain.</summary>
    public GridDescriptor Item(System.Action<Container> build) => Item(1, build);

    /// <summary>Appends a cell spanning <paramref name="columnSpan"/> columns configured inside <paramref name="build"/> and returns the grid.</summary>
    public GridDescriptor Item(int columnSpan, System.Action<Container> build)
    {
        build(Item(columnSpan));
        return this;
    }

    /// <summary>Appends a cell spanning <paramref name="columnSpan"/> columns and returns it for content/styling.</summary>
    public Container Item(int columnSpan = 1)
    {
        if (columnSpan < 1)
            throw new System.ArgumentOutOfRangeException(nameof(columnSpan), columnSpan, "A cell spans at least one column.");
        var cell = _ctx.CreateElement("div");
        if (columnSpan > 1)
            _ctx.SetStyle(cell, CssProp.GridColumn, "span " + columnSpan.ToString(CultureInfo.InvariantCulture));
        _grid.AppendChild(cell);
        return new Container(_ctx, cell);
    }
}
