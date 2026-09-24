using EggPdf.Html.Dom;

namespace EggPdf.Fluent;

/// <summary>Builds one table row's cells.</summary>
public sealed class TableRowDescriptor
{
    private readonly BuilderContext _ctx;
    private readonly HtmlElement _row;
    private readonly bool _headerCell;

    internal TableRowDescriptor(BuilderContext ctx, HtmlElement row, bool headerCell)
    {
        _ctx = ctx;
        _row = row;
        _headerCell = headerCell;
    }

    /// <summary>Appends a cell configured inside <paramref name="build"/> and returns the row, so cells chain.</summary>
    public TableRowDescriptor Cell(System.Action<Container> build)
    {
        build(Cell());
        return this;
    }

    /// <summary>Appends a cell (&lt;th&gt; in a header row, &lt;td&gt; otherwise) and returns it for content/styling.</summary>
    public Container Cell()
    {
        var cell = _ctx.CreateElement(_headerCell ? "th" : "td");
        _ctx.SetStyle(cell, CssProp.Padding, Length.Px(4).ToCss() + " " + Length.Px(8).ToCss());
        _ctx.SetStyle(cell, CssProp.Border, Length.Px(1).ToCss() + " " + BorderLineStyle.Solid.ToCss() + " " + Colors.LightGray.ToCss());
        _ctx.SetStyle(cell, CssProp.TextAlign, "left");
        _row.AppendChild(cell);
        return new Container(_ctx, cell);
    }
}
