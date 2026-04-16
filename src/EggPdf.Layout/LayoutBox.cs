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
    public ComputedStyle Style { get; set; } = new();
    public string? Text { get; set; }
    public bool IsListMarker { get; set; }
    public bool IsGeneratedContent { get; set; }
    public bool IsFloat { get; set; }
    public bool IsAbsolutelyPositioned { get; set; }
    public string? ImageSource { get; set; }
    public byte[]? ImageData { get; set; }
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
