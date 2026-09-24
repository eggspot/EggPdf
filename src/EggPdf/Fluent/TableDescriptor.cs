using System;
using EggPdf.Html.Dom;

namespace EggPdf.Fluent;

/// <summary>Builds a table's &lt;thead&gt; and &lt;tbody&gt; rows.</summary>
public sealed class TableDescriptor
{
    private readonly BuilderContext _ctx;
    private readonly HtmlElement _table;
    private HtmlElement? _thead;
    private HtmlElement? _tbody;

    internal TableDescriptor(BuilderContext ctx, HtmlElement table)
    {
        _ctx = ctx;
        _table = table;
    }

    /// <summary>
    /// Adds a header row inside &lt;thead&gt;, which repeats on every continuation page for
    /// tables spanning more than one page. Returns the row for styling (e.g. a header background).
    /// </summary>
    public Container Header(Action<TableRowDescriptor> build)
    {
        if (_thead == null)
        {
            _thead = _ctx.CreateElement("thead");
            _table.AppendChild(_thead);
        }
        var tr = _ctx.CreateElement("tr");
        _thead.AppendChild(tr);
        build(new TableRowDescriptor(_ctx, tr, headerCell: true));
        return new Container(_ctx, tr);
    }

    /// <summary>Adds a header row whose cells are built by <paramref name="cells"/> and whose row box is styled by <paramref name="rowStyle"/>; returns the table, so rows chain.</summary>
    public TableDescriptor Header(Action<TableRowDescriptor> cells, Action<Container> rowStyle)
    {
        rowStyle(Header(cells));
        return this;
    }

    /// <summary>Adds a body row whose cells are built by <paramref name="cells"/> and whose row box is styled by <paramref name="rowStyle"/>; returns the table, so rows chain.</summary>
    public TableDescriptor Row(Action<TableRowDescriptor> cells, Action<Container> rowStyle)
    {
        rowStyle(Row(cells));
        return this;
    }

    /// <summary>Adds a body row inside &lt;tbody&gt;. Returns the row for styling (e.g. striping, <see cref="Container.AvoidBreakInside"/>).</summary>
    public Container Row(Action<TableRowDescriptor> build)
    {
        if (_tbody == null)
        {
            _tbody = _ctx.CreateElement("tbody");
            _table.AppendChild(_tbody);
        }
        var tr = _ctx.CreateElement("tr");
        _tbody.AppendChild(tr);
        build(new TableRowDescriptor(_ctx, tr, headerCell: false));
        return new Container(_ctx, tr);
    }
}
