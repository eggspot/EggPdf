using System.Collections.Generic;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

/// <summary>
/// One line of an inline element whose text was split into per-word <see cref="LayoutBox"/>
/// fragments. Every fragment on that line references the same span through
/// <see cref="LayoutBox.InlineSpan"/>, so a consumer that needs the line's full extent (a link
/// annotation, a structure-tree element) reads the span instead of one fragment. An element that
/// wraps gets one span per line.
/// </summary>
public class InlineElementSpan
{
    private float _x;
    private float _y;
    private float _width;
    private float _height;
    private List<LayoutBox>? _fragments;

    /// <summary>The inline element this line belongs to.</summary>
    public HtmlElement Element { get; }

    /// <summary>Left edge of the line's fragments, in the same coordinates as their boxes.</summary>
    public float X => _fragments == null ? _x : Bounds().left;

    /// <summary>Top edge of the line's fragments, in the same coordinates as their boxes.</summary>
    public float Y => _fragments == null ? _y : Bounds().top;

    /// <summary>Width covered by the line's fragments.</summary>
    public float Width
    {
        get
        {
            if (_fragments == null) return _width;
            var b = Bounds();
            return b.right - b.left;
        }
    }

    /// <summary>Height covered by the line's fragments.</summary>
    public float Height
    {
        get
        {
            if (_fragments == null) return _height;
            var b = Bounds();
            return b.bottom - b.top;
        }
    }

    /// <summary>
    /// Opaque slot for a consumer (the paint layer) to remember that it already handled this span,
    /// e.g. the link annotation created for it, so a later fragment doesn't create a second one.
    /// </summary>
    public object? PaintTag { get; set; }

    /// <summary>Create a span with a fixed rectangle.</summary>
    public InlineElementSpan(HtmlElement element, float x, float y, float width, float height)
    {
        Element = element;
        _x = x; _y = y; _width = width; _height = height;
    }

    internal InlineElementSpan(HtmlElement element, LayoutBox firstFragment)
    {
        Element = element;
        _fragments = new List<LayoutBox> { firstFragment };
    }

    internal void AddFragment(LayoutBox fragment) => _fragments?.Add(fragment);

    internal void RemoveFragment(LayoutBox fragment)
    {
        if (_fragments == null || _fragments.Count <= 1) return;
        _fragments.Remove(fragment);
    }

    /// <summary>Extend a fixed-rectangle span to also cover another rectangle on the same line.</summary>
    public void Union(float x, float y, float width, float height)
    {
        float right = System.Math.Max(_x + _width, x + width);
        float bottom = System.Math.Max(_y + _height, y + height);
        _x = System.Math.Min(_x, x);
        _y = System.Math.Min(_y, y);
        _width = right - _x;
        _height = bottom - _y;
    }

    private (float left, float top, float right, float bottom) Bounds()
    {
        var fragments = _fragments!;
        float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
        for (int i = 0; i < fragments.Count; i++)
        {
            var f = fragments[i];
            if (f.X < left) left = f.X;
            if (f.Y < top) top = f.Y;
            if (f.X + f.Width > right) right = f.X + f.Width;
            if (f.Y + f.Height > bottom) bottom = f.Y + f.Height;
        }
        return (left, top, right, bottom);
    }
}
