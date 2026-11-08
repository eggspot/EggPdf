using System.Collections.Generic;
using System.Linq;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

/// <summary>
/// A positioned box in the layout tree. Each box has coordinates, dimensions, and style.
/// </summary>
public class LayoutBox
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    public float ContentWidth { get; set; }
    public float ContentHeight { get; set; }

    public float PaddingTop { get; set; }
    public float PaddingRight { get; set; }
    public float PaddingBottom { get; set; }
    public float PaddingLeft { get; set; }

    public float MarginTop { get; set; }
    public float MarginRight { get; set; }
    public float MarginBottom { get; set; }
    public float MarginLeft { get; set; }

    public HtmlElement? Element { get; set; }

    /// <summary>
    /// Set on every per-word fragment a multi-word inline element's text splits into (see
    /// <see cref="InlineElementSpan"/>) -- null for anything not produced by that word-splitting
    /// path. Fragments on the same line of the same element share one instance.
    /// </summary>
    public InlineElementSpan? InlineSpan { get; set; }

    internal float InlineLineTop { get; set; } = float.NaN;

    internal bool IsInlineFlowItem => !float.IsNaN(InlineLineTop);

    internal bool EndsLineAtForcedBreak { get; set; }

    public ComputedStyle Style { get; set; } = new();
    public string? Text { get; set; }
    public bool IsListMarker { get; set; }
    public bool IsGeneratedContent { get; set; }
    public bool IsFloat { get; set; }
    public bool IsAbsolutelyPositioned { get; set; }
    public string? ImageSource { get; set; }
    public byte[]? ImageData { get; set; }

    /// <summary>
    /// The root <c>&lt;svg&gt;</c> element parsed from <see cref="ImageData"/> when the image source
    /// is an SVG document, painted as vector graphics into this box; null for raster images.
    /// </summary>
    public HtmlElement? ImageSvgRoot { get; set; }

    /// <summary>
    /// The product of the <c>opacity</c> of every ancestor box (1 when none is translucent); the
    /// painter multiplies it with the box's own opacity so a translucent element fades its descendants too.
    /// </summary>
    public float AncestorOpacity { get; set; } = 1f;

    public List<LayoutBox> Children { get; } = new();

    public string? TagName => Element?.TagName;

    /// <summary>Find first descendant with matching tag name.</summary>
    public LayoutBox? FindByTag(string tagName)
    {
        if (Element?.TagName == tagName) return this;
        foreach (var child in Children)
        {
            var found = child.FindByTag(tagName);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>Find all descendants with matching tag name.</summary>
    public List<LayoutBox> FindAllByTag(string tagName)
    {
        var results = new List<LayoutBox>();
        FindAllByTagRecursive(tagName, results);
        return results;
    }

    private void FindAllByTagRecursive(string tagName, List<LayoutBox> results)
    {
        if (Element?.TagName == tagName) results.Add(this);
        foreach (var child in Children)
            child.FindAllByTagRecursive(tagName, results);
    }

    /// <summary>Find first descendant whose element has the given id attribute.</summary>
    public LayoutBox? FindById(string id)
    {
        if (Element is EggPdf.Html.Dom.HtmlElement el && el.Id == id) return this;
        foreach (var child in Children)
        {
            var found = child.FindById(id);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>Find all descendants matching the given predicate.</summary>
    public List<LayoutBox> FindAll(System.Func<LayoutBox, bool> predicate)
    {
        var results = new List<LayoutBox>();
        FindAllRecursive(predicate, results);
        return results;
    }

    private void FindAllRecursive(System.Func<LayoutBox, bool> predicate, List<LayoutBox> results)
    {
        if (predicate(this)) results.Add(this);
        foreach (var child in Children)
            child.FindAllRecursive(predicate, results);
    }
}
