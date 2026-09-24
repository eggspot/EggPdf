using EggPdf.Html.Dom;

namespace EggPdf.Fluent;

/// <summary>Builds the stacked (top-to-bottom) children of a <see cref="Container.Column"/>.</summary>
public sealed class ColumnDescriptor
{
    private readonly BuilderContext _ctx;
    private readonly HtmlElement _parent;

    internal ColumnDescriptor(BuilderContext ctx, HtmlElement parent)
    {
        _ctx = ctx;
        _parent = parent;
    }

    /// <summary>Appends a new block-level item to the column and returns it for styling/content.</summary>
    public Container Item()
    {
        var element = _ctx.CreateElement("div");
        _parent.AppendChild(element);
        return new Container(_ctx, element);
    }

    /// <summary>Appends an item configured inside <paramref name="build"/> and returns the column, so items chain: <c>col.Item(i =&gt; ...).Item(i =&gt; ...)</c>.</summary>
    public ColumnDescriptor Item(System.Action<Container> build)
    {
        build(Item());
        return this;
    }

    /// <summary>Appends a heading styled inside <paramref name="style"/> and returns the column, so it chains with <see cref="Item(System.Action{Container})"/>.</summary>
    public ColumnDescriptor Heading(HeadingLevel level, string text, System.Action<Container> style)
    {
        style(Heading(level, text));
        return this;
    }

    /// <summary>
    /// Appends a real &lt;h1&gt;-&lt;h6&gt; (not a styled div): headings are what the PDF/UA
    /// structure tree and auto-generated PDF bookmarks key off, so this is the only way to get a
    /// document's table of contents/outline from the fluent API.
    /// </summary>
    public Container Heading(HeadingLevel level, string text)
    {
        if (!System.Enum.IsDefined(typeof(HeadingLevel), level))
            throw new System.ArgumentOutOfRangeException(nameof(level), level, null);

        var element = _ctx.CreateElement("h" + ((int)level).ToString(System.Globalization.CultureInfo.InvariantCulture));
        _parent.AppendChild(element);
        return new Container(_ctx, element).Text(text);
    }
}
