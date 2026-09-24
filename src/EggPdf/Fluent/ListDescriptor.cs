using EggPdf.Html.Dom;

namespace EggPdf.Fluent;

/// <summary>Builds the &lt;li&gt; items of a bullet (&lt;ul&gt;) or numbered (&lt;ol&gt;) list.</summary>
public sealed class ListDescriptor
{
    private readonly BuilderContext _ctx;
    private readonly HtmlElement _list;

    internal ListDescriptor(BuilderContext ctx, HtmlElement list)
    {
        _ctx = ctx;
        _list = list;
    }

    /// <summary>Appends a list item configured inside <paramref name="build"/> and returns the list, so items chain.</summary>
    public ListDescriptor Item(System.Action<Container> build)
    {
        build(Item());
        return this;
    }

    /// <summary>Appends a list item and returns it for content/styling (it may itself hold nested lists, tables, etc.).</summary>
    public Container Item()
    {
        var li = _ctx.CreateElement("li");
        _list.AppendChild(li);
        return new Container(_ctx, li);
    }
}
