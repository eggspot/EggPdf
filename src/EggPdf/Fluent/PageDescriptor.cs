using System;
using System.Globalization;
using EggPdf.Html.Dom;

namespace EggPdf.Fluent;

/// <summary>
/// Configures one page group's size, margins, running header/footer/watermark and content.
/// Size/margin become an injected <c>@page</c> rule (named, for every group after the first --
/// see <see cref="DocumentDescriptor.Page"/>); content becomes the group's stacked
/// (<see cref="Container.Column"/>) body, the same way HTML's &lt;body&gt; is.
/// </summary>
public sealed class PageDescriptor
{
    private readonly BuilderContext _ctx;

    internal PageSize SizeValue { get; private set; } = PageSize.A4;
    internal Length MarginTop { get; private set; } = 20f;
    internal Length MarginRight { get; private set; } = 20f;
    internal Length MarginBottom { get; private set; } = 20f;
    internal Length MarginLeft { get; private set; } = 20f;
    internal HtmlElement RootElement { get; }

    internal PageDescriptor(BuilderContext ctx)
    {
        _ctx = ctx;
        RootElement = ctx.CreateElement("div");
    }

    /// <summary>The physical page size and orientation.</summary>
    public PageDescriptor Size(PageSize size)
    {
        SizeValue = size;
        return this;
    }

    /// <summary>Same margin on all four sides.</summary>
    public PageDescriptor Margin(Length all)
    {
        MarginTop = MarginRight = MarginBottom = MarginLeft = all;
        return this;
    }

    /// <summary>Per-side margins, in CSS shorthand order (top, right, bottom, left).</summary>
    public PageDescriptor Margin(Length top, Length right, Length bottom, Length left)
    {
        MarginTop = top;
        MarginRight = right;
        MarginBottom = bottom;
        MarginLeft = left;
        return this;
    }

    /// <summary>
    /// A running header repeated on every physical page of this group (CSS <c>position: fixed</c>
    /// pinned to the page's top edge). Reserve room for it with <see cref="Margin(Length)"/>.
    /// </summary>
    public PageDescriptor Header(Action<Container> build)
    {
        build(Header());
        return this;
    }

    /// <summary>The running header (see <see cref="Header(Action{Container})"/>) as a <see cref="Container"/> to fill in with separate statements.</summary>
    public Container Header()
    {
        var header = _ctx.CreateElement("div");
        _ctx.SetStyle(header, CssProp.Position, PositionMode.Fixed.ToCss());
        _ctx.SetStyle(header, CssProp.Top, Length.Zero.ToCss());
        _ctx.SetStyle(header, CssProp.Left, Length.Zero.ToCss());
        _ctx.SetStyle(header, CssProp.Right, Length.Zero.ToCss());
        RootElement.AppendChild(header);
        return new Container(_ctx, header);
    }

    /// <summary>A running footer repeated on every physical page of this group, pinned to the page's bottom edge.</summary>
    public PageDescriptor Footer(Action<Container> build)
    {
        build(Footer());
        return this;
    }

    /// <summary>The running footer (see <see cref="Footer(Action{Container})"/>) as a <see cref="Container"/> to fill in with separate statements.</summary>
    public Container Footer()
    {
        var footer = _ctx.CreateElement("div");
        _ctx.SetStyle(footer, CssProp.Position, PositionMode.Fixed.ToCss());
        _ctx.SetStyle(footer, CssProp.Bottom, Length.Zero.ToCss());
        _ctx.SetStyle(footer, CssProp.Left, Length.Zero.ToCss());
        _ctx.SetStyle(footer, CssProp.Right, Length.Zero.ToCss());
        RootElement.AppendChild(footer);
        return new Container(_ctx, footer);
    }

    /// <summary>
    /// A rotated, translucent text overlay ("DRAFT", "CONFIDENTIAL", ...) repeated centered on
    /// every physical page of this group. Composed from position:fixed/transform/opacity, the same
    /// primitives an HTML author would reach for -- there's no dedicated watermark keyword in the
    /// CSS engine either. <paramref name="color"/> defaults to mid gray.
    /// </summary>
    public PageDescriptor Watermark(string text, Length? fontSize = null, float rotationDegrees = -30f, float opacity = 0.12f, Color? color = null)
    {
        if (!(opacity >= 0f && opacity <= 1f))
            throw new ArgumentOutOfRangeException(nameof(opacity), opacity, "Opacity must be between 0 and 1.");

        var wm = _ctx.CreateElement("div");
        _ctx.SetStyle(wm, CssProp.Position, PositionMode.Fixed.ToCss());
        _ctx.SetStyle(wm, CssProp.Top, Length.Percent(50).ToCss());
        _ctx.SetStyle(wm, CssProp.Left, Length.Percent(50).ToCss());
        _ctx.SetStyle(wm, CssProp.Transform, CssTransform
            .Translate(Length.Percent(-50), Length.Percent(-50))
            .Then(CssTransform.Rotate(rotationDegrees)).ToCss());
        _ctx.SetStyle(wm, CssProp.FontSize, (fontSize ?? Length.Px(96)).ToCss());
        _ctx.SetStyle(wm, CssProp.Opacity, CssText.Number(opacity));
        _ctx.SetStyle(wm, CssProp.Color, (color ?? Colors.Gray).ToCss());
        _ctx.SetStyle(wm, CssProp.WhiteSpace, WhiteSpaceMode.NoWrap.ToCss());
        RootElement.AppendChild(wm);
        wm.AppendChild(new HtmlTextNode(text ?? ""));
        return this;
    }

    /// <summary>Builds the group's content, stacked top to bottom like a &lt;body&gt;.</summary>
    public PageDescriptor Content(Action<ColumnDescriptor> build)
    {
        build(Content());
        return this;
    }

    /// <summary>
    /// The group's content column, to add items to with separate statements. May be used more than
    /// once (items append in call order) -- e.g. one method per section of the report.
    /// </summary>
    public ColumnDescriptor Content() => new ColumnDescriptor(_ctx, RootElement);

    /// <summary>
    /// <paramref name="name"/> is null for the document's first (base, unnamed) page group and a
    /// generated name for every group after it -- see <see cref="DocumentDescriptor.Page"/>, which
    /// also applies the matching <c>page: &lt;name&gt;</c> declaration to <see cref="RootElement"/>.
    /// </summary>
    internal string BuildPageRuleCss(string? name)
    {
        string sizeAndMargin = "size:" + CssText.Number(SizeValue.WidthMm) + "mm " + CssText.Number(SizeValue.HeightMm) + "mm;" +
            "margin:" + MarginTop.ToCss() + " " + MarginRight.ToCss() + " " + MarginBottom.ToCss() + " " + MarginLeft.ToCss() + ";";
        return name == null ? "@page{" + sizeAndMargin + "}" : "@page " + name + "{" + sizeAndMargin + "}";
    }
}
