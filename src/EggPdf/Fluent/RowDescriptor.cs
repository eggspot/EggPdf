using System.Globalization;
using EggPdf.Html.Dom;

namespace EggPdf.Fluent;

/// <summary>Builds the side-by-side (flex) children of a <see cref="Container.Row"/>.</summary>
public sealed class RowDescriptor
{
    private readonly BuilderContext _ctx;
    private readonly HtmlElement _parent;

    internal RowDescriptor(BuilderContext ctx, HtmlElement parent)
    {
        _ctx = ctx;
        _parent = parent;
    }

    /// <summary>Space between items along the row (<c>gap</c>). It applies to the row itself, so it can be set before or after adding items.</summary>
    public RowDescriptor Gap(Length gap)
    {
        _ctx.SetStyle(_parent, CssProp.Gap, gap.ToCss());
        return this;
    }

    /// <summary>How items distribute along the row (<c>justify-content</c>).</summary>
    public RowDescriptor JustifyContent(FlexJustify justify)
    {
        _ctx.SetStyle(_parent, CssProp.JustifyContent, justify.ToCss());
        return this;
    }

    /// <summary>How items align across the row's height (<c>align-items</c>).</summary>
    public RowDescriptor AlignItems(FlexAlign align)
    {
        _ctx.SetStyle(_parent, CssProp.AlignItems, align.ToCss());
        return this;
    }

    /// <summary>Lets items wrap onto additional lines instead of shrinking to fit one row (<c>flex-wrap: wrap</c>).</summary>
    public RowDescriptor Wrap()
    {
        _ctx.SetStyle(_parent, CssProp.FlexWrap, "wrap");
        return this;
    }

    /// <summary>Appends an item that shares the row's remaining space by <paramref name="weight"/> (flex-grow).</summary>
    public Container RelativeItem(float weight = 1)
    {
        var element = _ctx.CreateElement("div");
        _ctx.SetStyle(element, CssProp.Flex, weight.ToString(CultureInfo.InvariantCulture) + " 1 0");
        _parent.AppendChild(element);
        return new Container(_ctx, element);
    }

    /// <summary>Appends a flexible item configured inside <paramref name="build"/> and returns the row, so items chain.</summary>
    public RowDescriptor RelativeItem(System.Action<Container> build) => RelativeItem(1, build);

    /// <summary>Appends a flexible item of the given <paramref name="weight"/> configured inside <paramref name="build"/> and returns the row.</summary>
    public RowDescriptor RelativeItem(float weight, System.Action<Container> build)
    {
        build(RelativeItem(weight));
        return this;
    }

    /// <summary>Appends a fixed-width item configured inside <paramref name="build"/> and returns the row, so items chain.</summary>
    public RowDescriptor ConstantItem(Length width, System.Action<Container> build)
    {
        build(ConstantItem(width));
        return this;
    }

    /// <summary>Appends an item with a fixed width that does not grow or shrink.</summary>
    public Container ConstantItem(Length width)
    {
        var element = _ctx.CreateElement("div");
        _ctx.SetStyle(element, CssProp.Flex, "0 0 " + width.ToCss());
        _parent.AppendChild(element);
        return new Container(_ctx, element);
    }
}
