using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EggPdf.Html.Dom;

namespace EggPdf.Css.Selectors;

internal sealed class SiblingIndex
{
    internal const int MinChildCount = 32;

    private static readonly ConditionalWeakTable<HtmlNode, Holder> Cache = new();

    private readonly int _childNodeCount;
    private readonly HtmlElement[] _elements;
    private readonly Dictionary<HtmlElement, int> _positions;
    private readonly int[] _positionsAmongType;
    private readonly Dictionary<string, int> _countsByType;

    private SiblingIndex(HtmlNode parent)
    {
        _childNodeCount = parent.ChildNodes.Count;
        var elements = new List<HtmlElement>(_childNodeCount);
        foreach (var child in parent.ChildNodes)
            if (child is HtmlElement element) elements.Add(element);

        _elements = elements.ToArray();
        _positions = new Dictionary<HtmlElement, int>(_elements.Length);
        _positionsAmongType = new int[_elements.Length];
        _countsByType = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < _elements.Length; i++)
        {
            _positions[_elements[i]] = i;
            _countsByType.TryGetValue(_elements[i].TagName, out int seen);
            _positionsAmongType[i] = seen;
            _countsByType[_elements[i].TagName] = seen + 1;
        }
    }

    public static SiblingIndex Of(HtmlNode parent)
    {
        var holder = Cache.GetValue(parent, _ => new Holder());
        var index = holder.Index;
        if (index == null || index._childNodeCount != parent.ChildNodes.Count)
        {
            index = new SiblingIndex(parent);
            holder.Index = index;
        }
        return index;
    }

    public int ElementCount => _elements.Length;

    public int PositionOf(HtmlElement element) => _positions.TryGetValue(element, out int position) ? position : -1;

    public HtmlElement? ElementAt(int position) =>
        position >= 0 && position < _elements.Length ? _elements[position] : null;

    public int PositionAmongType(int position) => _positionsAmongType[position];

    public int CountOfType(string tagName) => _countsByType.TryGetValue(tagName, out int count) ? count : 0;

    private sealed class Holder
    {
        public SiblingIndex? Index;
    }
}
