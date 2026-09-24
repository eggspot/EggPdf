using System.Globalization;
using EggPdf.Html;
using EggPdf.Html.Dom;

namespace EggPdf.Fluent;

/// <summary>
/// A single box in the document tree (a &lt;div&gt; under the hood). Every content-producing
/// fluent call (<see cref="Column"/>, <see cref="Row"/>, page content, row/column items) hands
/// back a <see cref="Container"/>, so text and styling always apply to "whatever was placed last".
/// Styling takes typed values (<see cref="Color"/>, <see cref="Length"/>, keyword enums); the
/// <c>Raw*</c> methods are the explicit, unchecked escape hatches.
/// </summary>
public class Container
{
    private readonly BuilderContext _ctx;
    internal HtmlElement Element { get; }

    internal Container(BuilderContext ctx, HtmlElement element)
    {
        _ctx = ctx;
        Element = element;
    }

    private Container SetStyle(string property, string value)
    {
        _ctx.SetStyle(Element, property, value);
        return this;
    }

    // ── content ──────────────────────────────────────────────────────────────

    /// <summary>Sets this container's text content (equivalent to a text node child).</summary>
    public Container Text(string text)
    {
        _ctx.EnsureMutable();
        Element.AppendChild(new HtmlTextNode(text ?? ""));
        return this;
    }

    /// <summary>Stacks children top to bottom (normal block flow -- CSS's default).</summary>
    public Container Column(System.Action<ColumnDescriptor> build)
    {
        build(Column());
        return this;
    }

    /// <summary>The children column of this container, to add items to with separate statements.</summary>
    public ColumnDescriptor Column() => new ColumnDescriptor(_ctx, Element);

    /// <summary>Lays out children left to right (<c>display: flex</c>).</summary>
    public Container Row(System.Action<RowDescriptor> build)
    {
        build(Row());
        return this;
    }

    /// <summary>Starts a row (flex container) appended here and returns it to fill in with separate statements.</summary>
    public RowDescriptor Row()
    {
        var rowElement = _ctx.CreateElement("div");
        _ctx.SetStyle(rowElement, CssProp.Display, DisplayMode.Flex.ToCss());
        Element.AppendChild(rowElement);
        return new RowDescriptor(_ctx, rowElement);
    }

    /// <summary>
    /// Builds a &lt;table&gt; -- &lt;thead&gt; rows added via <see cref="TableDescriptor.Header"/>
    /// repeat on every continuation page for tables that span more than one page, the same as a
    /// hand-written &lt;thead&gt; does.
    /// </summary>
    public Container Table(System.Action<TableDescriptor> build)
    {
        build(Table());
        return this;
    }

    /// <summary>Starts a table appended here and returns it to fill in with separate statements (see <see cref="Table(System.Action{TableDescriptor})"/>).</summary>
    public TableDescriptor Table()
    {
        var table = _ctx.CreateElement("table");
        _ctx.SetStyle(table, CssProp.Width, Length.Percent(100).ToCss());
        _ctx.SetStyle(table, CssProp.BorderCollapse, "collapse");
        Element.AppendChild(table);
        return new TableDescriptor(_ctx, table);
    }

    /// <summary>Appends a bulleted list (&lt;ul&gt;).</summary>
    public Container BulletList(System.Action<ListDescriptor> build)
    {
        build(BulletList());
        return this;
    }

    /// <summary>Starts a bulleted list and returns it to add items to with separate statements.</summary>
    public ListDescriptor BulletList() => List("ul");

    /// <summary>Appends a numbered list (&lt;ol&gt;).</summary>
    public Container NumberedList(System.Action<ListDescriptor> build)
    {
        build(NumberedList());
        return this;
    }

    /// <summary>Starts a numbered list and returns it to add items to with separate statements.</summary>
    public ListDescriptor NumberedList() => List("ol");

    private ListDescriptor List(string tag)
    {
        var list = _ctx.CreateElement(tag);
        Element.AppendChild(list);
        return new ListDescriptor(_ctx, list);
    }

    /// <summary>Lays out children in a CSS grid with the given column tracks.</summary>
    public Container Grid(GridTrack[] columns, System.Action<GridDescriptor> build)
    {
        build(Grid(columns));
        return this;
    }

    /// <summary>Starts a grid with the given column tracks and returns it to add cells to with separate statements.</summary>
    public GridDescriptor Grid(GridTrack[] columns)
    {
        var parts = new string[columns.Length];
        for (int i = 0; i < columns.Length; i++) parts[i] = columns[i].ToCss();
        return GridWithTemplate(string.Join(" ", parts));
    }

    /// <summary>Lays out children in a CSS grid of <paramref name="columnCount"/> equal columns.</summary>
    public Container Grid(int columnCount, System.Action<GridDescriptor> build)
    {
        build(Grid(columnCount));
        return this;
    }

    /// <summary>Starts a grid of <paramref name="columnCount"/> equal columns and returns it to add cells to with separate statements.</summary>
    public GridDescriptor Grid(int columnCount)
    {
        if (columnCount < 1) throw new System.ArgumentOutOfRangeException(nameof(columnCount), columnCount, "A grid needs at least one column.");
        return GridWithTemplate("repeat(" + columnCount.ToString(CultureInfo.InvariantCulture) + ", 1fr)");
    }

    private GridDescriptor GridWithTemplate(string template)
    {
        var grid = _ctx.CreateElement("div");
        _ctx.SetStyle(grid, CssProp.Display, DisplayMode.Grid.ToCss());
        _ctx.SetStyle(grid, CssProp.GridTemplateColumns, template);
        Element.AppendChild(grid);
        return new GridDescriptor(_ctx, grid);
    }

    /// <summary>Appends an &lt;img&gt;. Width/height (in px) are HTML attributes, matching plain &lt;img width height&gt; sizing.</summary>
    public Container Image(string src, float? widthPx = null, float? heightPx = null)
    {
        var img = _ctx.CreateElement("img");
        img.SetAttribute("src", src ?? "");
        if (widthPx.HasValue) img.SetAttribute("width", widthPx.Value.ToString(CultureInfo.InvariantCulture));
        if (heightPx.HasValue) img.SetAttribute("height", heightPx.Value.ToString(CultureInfo.InvariantCulture));
        Element.AppendChild(img);
        return this;
    }

    /// <summary>Appends a clickable &lt;a href&gt; link.</summary>
    public Container Hyperlink(string url, string text)
    {
        var a = _ctx.CreateElement("a");
        a.SetAttribute("href", url ?? "");
        a.AppendChild(new HtmlTextNode(text ?? ""));
        Element.AppendChild(a);
        return this;
    }

    // ── attributes ───────────────────────────────────────────────────────────

    /// <summary>Sets this box's <c>id</c>, the target of internal links (<c>Hyperlink("#id", ...)</c>) and of <see cref="DocumentDescriptor.Css"/> selectors.</summary>
    public Container Id(string id) => RawAttribute("id", id);

    /// <summary>Adds a CSS class (repeatable; classes accumulate) for <see cref="DocumentDescriptor.Css"/> selectors to target.</summary>
    public Container Class(string className)
    {
        _ctx.AddClass(Element, className);
        return this;
    }

    /// <summary>Text direction of this box (<c>dir</c>).</summary>
    public Container Direction(TextDirection direction) => RawAttribute("dir", direction.ToCss());

    // ── pagination ───────────────────────────────────────────────────────────

    /// <summary>Pins this box to the bottom of whichever physical page its surrounding dynamic content ends on (e.g. a signature block).</summary>
    public Container PinBottom() => SetStyle(CssProp.PinBottom, "page");

    /// <summary>Keeps this box from being split across a page break (<c>break-inside: avoid</c>) -- e.g. a table row that must not straddle two pages.</summary>
    public Container AvoidBreakInside() => SetStyle(CssProp.BreakInside, "avoid");

    /// <summary>
    /// Renders as the current physical page number, e.g. inside a <see cref="PageDescriptor.Footer"/>.
    /// Generated content (CSS counter()) can only be expressed via a real ::after rule, not an
    /// inline style="" attribute, so this registers one under a fresh class name.
    /// </summary>
    public Container CurrentPageNumber() => GeneratedContent("counter(page)");

    /// <summary>Renders as the document's total physical page count.</summary>
    public Container TotalPages() => GeneratedContent("counter(pages)");

    /// <summary>Renders as e.g. "Page 3 of 12".</summary>
    public Container PageNumberOfTotal(string prefix = "Page ", string separator = " of ", string suffix = "")
        => GeneratedContent(CssString(prefix) + " counter(page) " + CssString(separator) + " counter(pages) " + CssString(suffix));

    private Container GeneratedContent(string cssContentValue)
    {
        if (!_ctx.TryMarkGeneratedContentUsed(Element))
            throw new System.InvalidOperationException(
                "This container already has generated content from CurrentPageNumber()/TotalPages()/PageNumberOfTotal() -- " +
                "CSS allows only one ::after per element, so a second call would silently replace the first instead of " +
                "combining with it. Use a separate Container (e.g. another Row item) for each piece.");

        var cls = _ctx.NextClassName("gc");
        _ctx.AddClass(Element, cls);
        _ctx.AddGlobalCss("." + cls + "::after{content:" + cssContentValue + ";}");
        return this;
    }

    private static string CssString(string s)
        => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    // ── typography ───────────────────────────────────────────────────────────

    /// <summary>Font size (a bare number is px).</summary>
    public Container FontSize(Length size) => SetStyle(CssProp.FontSize, size.ToCss());

    /// <summary>Font family name or comma-separated fallback list, e.g. <c>"Inter, Arial"</c> (free text -- a family, not a CSS keyword).</summary>
    public Container FontFamily(string family) => SetStyle(CssProp.FontFamily, family);

    /// <summary>Bold text (<c>font-weight: bold</c>).</summary>
    public Container Bold() => SetStyle(CssProp.FontWeight, "bold");

    /// <summary>Italic (slanted) text.</summary>
    public Container Italic() => SetStyle(CssProp.FontStyle, "italic");

    /// <summary>Numeric weight, 1-1000 (400 = normal, 700 = bold).</summary>
    public Container FontWeight(int weight)
    {
        if (weight < 1 || weight > 1000)
            throw new System.ArgumentOutOfRangeException(nameof(weight), weight, "Font weight must be 1-1000.");
        return SetStyle(CssProp.FontWeight, weight.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Text color.</summary>
    public Container FontColor(Color color) => SetStyle(CssProp.Color, color.ToCss());

    /// <summary>Unitless line-height multiplier (1.4 = 140% of the font size).</summary>
    public Container LineHeight(float multiplier)
    {
        if (!(multiplier > 0) || float.IsInfinity(multiplier))
            throw new System.ArgumentOutOfRangeException(nameof(multiplier), multiplier, "Line height must be a positive finite number.");
        return SetStyle(CssProp.LineHeight, multiplier.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Absolute line-height.</summary>
    public Container LineHeight(Length height) => SetStyle(CssProp.LineHeight, height.ToCss());

    /// <summary>Extra space between characters.</summary>
    public Container LetterSpacing(Length spacing) => SetStyle(CssProp.LetterSpacing, spacing.ToCss());

    /// <summary>Underlined text.</summary>
    public Container Underline() => SetStyle(CssProp.TextDecoration, "underline");

    /// <summary>Struck-through text.</summary>
    public Container StrikeThrough() => SetStyle(CssProp.TextDecoration, "line-through");

    /// <summary>Removes text decoration (e.g. the underline links get by default).</summary>
    public Container NoTextDecoration() => SetStyle(CssProp.TextDecoration, "none");

    /// <summary>Upper/lower/capitalize case transformation of the rendered text.</summary>
    public Container TextTransform(TextCase textCase) => SetStyle(CssProp.TextTransform, textCase.ToCss());

    /// <summary>How white space and line breaks in the text are handled.</summary>
    public Container WhiteSpace(WhiteSpaceMode mode) => SetStyle(CssProp.WhiteSpace, mode.ToCss());

    /// <summary>Aligns text to the left edge.</summary>
    public Container AlignLeft() => SetStyle(CssProp.TextAlign, "left");

    /// <summary>Centers text.</summary>
    public Container AlignCenter() => SetStyle(CssProp.TextAlign, "center");

    /// <summary>Aligns text to the right edge.</summary>
    public Container AlignRight() => SetStyle(CssProp.TextAlign, "right");

    // ── box model ────────────────────────────────────────────────────────────

    /// <summary>Inner spacing on all four sides.</summary>
    public Container Padding(Length all) => SetStyle(CssProp.Padding, all.ToCss());

    /// <summary>Inner spacing: vertical (top and bottom), then horizontal (left and right).</summary>
    public Container Padding(Length vertical, Length horizontal) => SetStyle(CssProp.Padding, vertical.ToCss() + " " + horizontal.ToCss());

    /// <summary>Inner spacing per side, in CSS order: top, right, bottom, left.</summary>
    public Container Padding(Length top, Length right, Length bottom, Length left)
        => SetStyle(CssProp.Padding, top.ToCss() + " " + right.ToCss() + " " + bottom.ToCss() + " " + left.ToCss());

    /// <summary>Outer spacing on all four sides.</summary>
    public Container Margin(Length all) => SetStyle(CssProp.Margin, all.ToCss());

    /// <summary>Outer spacing: vertical (top and bottom), then horizontal (left and right).</summary>
    public Container Margin(Length vertical, Length horizontal) => SetStyle(CssProp.Margin, vertical.ToCss() + " " + horizontal.ToCss());

    /// <summary>Outer spacing per side, in CSS order: top, right, bottom, left.</summary>
    public Container Margin(Length top, Length right, Length bottom, Length left)
        => SetStyle(CssProp.Margin, top.ToCss() + " " + right.ToCss() + " " + bottom.ToCss() + " " + left.ToCss());

    /// <summary>Box width.</summary>
    public Container Width(Length width) => SetStyle(CssProp.Width, width.ToCss());

    /// <summary>Box height.</summary>
    public Container Height(Length height) => SetStyle(CssProp.Height, height.ToCss());

    /// <summary>Minimum box width.</summary>
    public Container MinWidth(Length width) => SetStyle(CssProp.MinWidth, width.ToCss());

    /// <summary>Maximum box width.</summary>
    public Container MaxWidth(Length width) => SetStyle(CssProp.MaxWidth, width.ToCss());

    /// <summary>Minimum box height.</summary>
    public Container MinHeight(Length height) => SetStyle(CssProp.MinHeight, height.ToCss());

    /// <summary>Maximum box height.</summary>
    public Container MaxHeight(Length height) => SetStyle(CssProp.MaxHeight, height.ToCss());

    /// <summary>Background fill color.</summary>
    public Container Background(Color color) => SetStyle(CssProp.Background, color.ToCss());

    /// <summary>Border on all four sides.</summary>
    public Container Border(Length width, Color color, BorderLineStyle style = BorderLineStyle.Solid)
        => SetStyle(CssProp.Border, BorderValue(width, color, style));

    /// <summary>Top border only.</summary>
    public Container BorderTop(Length width, Color color, BorderLineStyle style = BorderLineStyle.Solid)
        => SetStyle(CssProp.BorderTop, BorderValue(width, color, style));

    /// <summary>Right border only.</summary>
    public Container BorderRight(Length width, Color color, BorderLineStyle style = BorderLineStyle.Solid)
        => SetStyle(CssProp.BorderRight, BorderValue(width, color, style));

    /// <summary>Bottom border only.</summary>
    public Container BorderBottom(Length width, Color color, BorderLineStyle style = BorderLineStyle.Solid)
        => SetStyle(CssProp.BorderBottom, BorderValue(width, color, style));

    /// <summary>Left border only.</summary>
    public Container BorderLeft(Length width, Color color, BorderLineStyle style = BorderLineStyle.Solid)
        => SetStyle(CssProp.BorderLeft, BorderValue(width, color, style));

    private static string BorderValue(Length width, Color color, BorderLineStyle style)
        => width.ToCss() + " " + style.ToCss() + " " + color.ToCss();

    /// <summary>Rounded corners.</summary>
    public Container BorderRadius(Length radius) => SetStyle(CssProp.BorderRadius, radius.ToCss());

    /// <summary>Drop shadow: horizontal offset, vertical offset, blur radius, color.</summary>
    public Container BoxShadow(Length offsetX, Length offsetY, Length blur, Color color)
        => SetStyle(CssProp.BoxShadow, offsetX.ToCss() + " " + offsetY.ToCss() + " " + blur.ToCss() + " " + color.ToCss());

    // ── layout / effects ─────────────────────────────────────────────────────

    /// <summary>0 (transparent) to 1 (opaque).</summary>
    public Container Opacity(float value)
    {
        if (!(value >= 0f && value <= 1f))
            throw new System.ArgumentOutOfRangeException(nameof(value), value, "Opacity must be between 0 and 1.");
        return SetStyle(CssProp.Opacity, value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>What happens to content that doesn't fit the box (<see cref="OverflowMode.Hidden"/> clips it).</summary>
    public Container Overflow(OverflowMode mode) => SetStyle(CssProp.Overflow, mode.ToCss());

    /// <summary>Positioning scheme (<c>position</c>).</summary>
    public Container Position(PositionMode mode) => SetStyle(CssProp.Position, mode.ToCss());

    /// <summary>Display type (<c>display</c>).</summary>
    public Container Display(DisplayMode mode) => SetStyle(CssProp.Display, mode.ToCss());

    /// <summary>Floats the box left or right so following inline content wraps around it.</summary>
    public Container Float(FloatSide side) => SetStyle(CssProp.Float, side.ToCss());

    /// <summary>Applies a 2D transform (rotate, scale, translate, skew), e.g. <c>CssTransform.Rotate(10)</c>.</summary>
    public Container Transform(CssTransform transform) => SetStyle(CssProp.Transform, transform.ToCss());

    // ── explicit escape hatches (unchecked) ──────────────────────────────────

    /// <summary>
    /// Unchecked escape hatch: sets any CSS declaration by name and value. Nothing validates
    /// either string, so a typo is silently ignored by the CSS engine -- prefer the typed methods
    /// above, and use this only for a property that has none yet.
    /// </summary>
    public Container RawStyle(string property, string value) => SetStyle(property, value);

    /// <summary>Unchecked escape hatch: sets any HTML attribute by name. The first value set for a name wins; use <see cref="RawStyle"/>, not a <c>style</c> attribute.</summary>
    public Container RawAttribute(string name, string value)
    {
        _ctx.EnsureMutable();
        Element.SetAttribute(name, value ?? "");
        return this;
    }

    /// <summary>
    /// Unchecked escape hatch: parses <paramref name="html"/> as an HTML fragment and appends its
    /// nodes as children -- anything expressible in HTML is expressible here, even before a typed
    /// wrapper exists for it.
    /// </summary>
    public Container Raw(string html)
    {
        _ctx.EnsureMutable();
        var fragment = HtmlParser.Parse(html ?? "");
        var body = fragment.Body;
        if (body != null)
        {
            var children = new System.Collections.Generic.List<HtmlNode>(body.ChildNodes);
            foreach (var child in children)
            {
                body.RemoveChild(child);
                Element.AppendChild(child);
            }
        }
        return this;
    }
}
