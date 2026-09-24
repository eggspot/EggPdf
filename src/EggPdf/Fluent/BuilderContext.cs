using System;
using System.Collections.Generic;
using System.Text;
using EggPdf.Html.Dom;

namespace EggPdf.Fluent;

/// <summary>
/// Per-document builder state. Fluent style setters (<see cref="Container.FontSize"/> etc.)
/// accumulate declarations here keyed by element instead of writing the "style" attribute
/// directly, since <see cref="HtmlElement.SetAttribute"/> keeps the first value on a repeated
/// key (HTML5 duplicate-attribute semantics) rather than overwriting it. <see cref="FinalizeStyles"/>
/// serializes each element's accumulated declarations into its "style" attribute exactly once,
/// after the whole builder tree (and therefore every style call) has run.
/// </summary>
internal sealed class BuilderContext
{
    private readonly Dictionary<HtmlElement, List<KeyValuePair<string, string>>> _styles = new();
    private readonly Dictionary<HtmlElement, List<string>> _classes = new();
    private readonly List<string> _globalCss = new();
    private readonly HashSet<HtmlElement> _generatedContentUsed = new();
    private int _classCounter;
    private bool _frozen;

    /// <summary>The document's &lt;head&gt;, where head-level markup from <see cref="Container.Raw"/> (e.g. a &lt;style&gt;) is placed.</summary>
    public HtmlElement? Head { get; set; }

    /// <summary>Called once the document is built: from then on every builder call throws, since styles have been serialized and later changes would be silently lost.</summary>
    public void Freeze() => _frozen = true;

    public void EnsureMutable()
    {
        if (_frozen)
            throw new InvalidOperationException(
                "This document has already been built or rendered and can no longer be changed. " +
                "Create a new one with Document.New()/Document.Create(...) to make different content.");
    }

    public HtmlElement CreateElement(string tagName)
    {
        EnsureMutable();
        return new HtmlElement(tagName);
    }

    /// <summary>
    /// A fresh, document-unique class name (e.g. "eggpdf-pn-3"), for features that need a real
    /// stylesheet rule instead of an inline declaration -- ::before/::after generated content
    /// (page numbers) can't be expressed as an inline style="" attribute at all.
    /// </summary>
    public string NextClassName(string prefix) => "eggpdf-" + prefix + "-" + (++_classCounter).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Registers a raw CSS rule (e.g. ".eggpdf-pn-3::after{content:counter(page);}") to emit in a shared &lt;style&gt; block.</summary>
    public void AddGlobalCss(string css)
    {
        EnsureMutable();
        _globalCss.Add(css);
    }

    /// <summary>The joined text of every <see cref="AddGlobalCss"/> call, or null if none were made.</summary>
    public string? BuildGlobalCss() => _globalCss.Count == 0 ? null : string.Join("", _globalCss);

    /// <summary>
    /// True (and records the use) the first time this element requests generated (::after)
    /// content; false on every call after that. CSS allows only one <c>content</c> declaration per
    /// pseudo-element, so a second registration on the same element wouldn't add to the first --
    /// it would silently replace it in the cascade. Callers use this to fail loudly instead.
    /// </summary>
    public bool TryMarkGeneratedContentUsed(HtmlElement element) => _generatedContentUsed.Add(element);

    public void SetStyle(HtmlElement element, string property, string value)
    {
        EnsureMutable();
        if (!_styles.TryGetValue(element, out var decls))
        {
            decls = new List<KeyValuePair<string, string>>();
            _styles[element] = decls;
        }

        for (int i = 0; i < decls.Count; i++)
        {
            if (decls[i].Key == property)
            {
                decls[i] = new KeyValuePair<string, string>(property, value);
                return;
            }
        }
        decls.Add(new KeyValuePair<string, string>(property, value));
    }

    /// <summary>Adds a class to the element; written once, joined with any others, by <see cref="FinalizeStyles"/>.</summary>
    public void AddClass(HtmlElement element, string className)
    {
        EnsureMutable();
        if (!_classes.TryGetValue(element, out var list))
        {
            list = new List<string>();
            _classes[element] = list;
        }
        if (!list.Contains(className)) list.Add(className);
    }

    /// <summary>Serializes every accumulated class list and style into attributes, walking the tree with an explicit stack so a very deep (e.g. generated) tree can't overflow the call stack.</summary>
    public void FinalizeStyles(HtmlNode root)
    {
        var pending = new Stack<HtmlNode>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node is HtmlElement el)
            {
                if (_classes.TryGetValue(el, out var classes) && classes.Count > 0)
                    el.SetAttribute("class", string.Join(" ", classes));

                if (_styles.TryGetValue(el, out var decls) && decls.Count > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var decl in decls)
                        sb.Append(decl.Key).Append(':').Append(decl.Value).Append(';');
                    el.SetAttribute("style", sb.ToString());
                }
            }

            for (int i = 0; i < node.ChildNodes.Count; i++)
                pending.Push(node.ChildNodes[i]);
        }
    }
}
