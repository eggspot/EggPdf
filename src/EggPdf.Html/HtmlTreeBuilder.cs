using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EggPdf.Html.Dom;

namespace EggPdf.Html;

/// <summary>
/// Builds a DOM tree from HTML tokens. Implements a simplified version of the
/// WHATWG tree construction algorithm with core insertion modes.
/// Infallible: never throws on any input.
/// </summary>
internal class HtmlTreeBuilder
{
    private readonly HtmlDocument _document = new();
    private readonly Stack<HtmlElement> _openElements = new();
    private HtmlElement? _headElement;
    private HtmlElement? _bodyElement;
    private bool _ignoreNextLeadingNewline;
    private HtmlTokenizer? _tokenizer;

    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr"
    };

    private static readonly HashSet<string> RawTextElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "textarea", "title"
    };

    private static readonly HashSet<string> ImplicitlyClosesParagraph = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "details", "dialog", "dd", "div", "dl",
        "dt", "fieldset", "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4",
        "h5", "h6", "header", "hgroup", "hr", "li", "main", "menu", "nav", "ol", "p", "pre",
        "section", "summary", "table", "ul"
    };

    private static readonly HashSet<string> HeadElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "base", "basefont", "bgsound", "link", "meta", "title", "style", "script", "noscript"
    };

    public HtmlDocument Build(HtmlTokenizer tokenizer)
    {
        _tokenizer = tokenizer;
        HtmlToken token;
        while ((token = tokenizer.NextToken()).Type != HtmlTokenType.EndOfFile)
        {
            ProcessToken(token);
        }

        // Ensure document always has html/head/body even with empty input
        EnsureHtmlElement();
        EnsureHeadElement();
        EnsureBodyElement();

        return _document;
    }

    private void ProcessToken(HtmlToken token)
    {
        bool ignoreLeadingNewline = _ignoreNextLeadingNewline;
        _ignoreNextLeadingNewline = false;
        if (ignoreLeadingNewline && token.Type == HtmlTokenType.Character && token.Data != null)
            token.Data = WithoutLeadingNewline(token.Data);

        switch (token.Type)
        {
            case HtmlTokenType.Doctype:
                _document.AppendChild(new HtmlDocumentType(token.DoctypeName ?? "html"));
                break;

            case HtmlTokenType.StartTag:
                ProcessStartTag(token);
                break;

            case HtmlTokenType.EndTag:
                ProcessEndTag(token);
                break;

            case HtmlTokenType.Character:
                ProcessCharacter(token);
                break;

            case HtmlTokenType.Comment:
                var parent = CurrentNode() ?? (HtmlNode)_document;
                parent.AppendChild(new HtmlComment(token.Data ?? ""));
                break;
        }
    }

    private void ProcessStartTag(HtmlToken token)
    {
        var tagName = token.TagName!;

        // html element
        if (tagName == "html")
        {
            EnsureHtmlElement();
            // Per the HTML5 tree construction algorithm's "A start tag whose tag name is html"
            // step: attributes on this token (e.g. lang) apply to the document element even
            // when it already exists (e.g. synthesized by an earlier head/body tag), without
            // overwriting an attribute already set by an earlier <html> tag.
            var root = _document.DocumentElement!;
            foreach (var attr in token.Attributes)
                if (string.IsNullOrEmpty(root.GetAttribute(attr.Name)))
                    root.SetAttribute(attr.Name, attr.Value);
            return;
        }

        // head element
        if (tagName == "head")
        {
            EnsureHtmlElement();
            if (_headElement == null)
            {
                _headElement = CreateElement(token);
                _document.DocumentElement!.AppendChild(_headElement);
            }
            return;
        }

        // body element
        if (tagName == "body")
        {
            EnsureHtmlElement();
            EnsureHeadElement();
            if (_bodyElement == null)
            {
                _bodyElement = CreateElement(token);
                _document.DocumentElement!.AppendChild(_bodyElement);
                // Set as current insertion point
                _openElements.Clear();
                _openElements.Push(_document.DocumentElement!);
                _openElements.Push(_bodyElement);
            }
            return;
        }

        // Head content elements
        if (_bodyElement == null && HeadElements.Contains(tagName))
        {
            EnsureHtmlElement();
            EnsureHeadElement();

            var elem = CreateElement(token);
            _headElement!.AppendChild(elem);

            // Raw text elements: collect text until matching end tag
            if (RawTextElements.Contains(tagName))
            {
                CollectRawText(elem, tagName);
            }
            return;
        }

        EnsureBodyElement();

        CloseImpliedEndTags(tagName);

        // Handle <p> implicit close
        if (ImplicitlyClosesParagraph.Contains(tagName))
        {
            if (HasOpenElement("p"))
                CloseUpTo("p");
        }

        // Table: implicit tbody for <tr>
        if (tagName == "tr" && _openElements.Count > 0 && _openElements.Peek().TagName == "table")
        {
            var tbody = new HtmlElement("tbody");
            _openElements.Peek().AppendChild(tbody);
            _openElements.Push(tbody);
        }

        var element = CreateElement(token);
        var insertionPoint = CurrentNode() ?? (HtmlNode)_bodyElement!;
        insertionPoint.AppendChild(element);

        // Raw text elements in body (style in body, noscript, etc.)
        if (RawTextElements.Contains(tagName) && tagName != "noscript")
        {
            CollectRawText(element, tagName);
            return;
        }

        if (!VoidElements.Contains(tagName) && !token.SelfClosing)
        {
            _openElements.Push(element);
            _ignoreNextLeadingNewline = IgnoresLeadingNewline(tagName);
        }
    }

    private static bool IgnoresLeadingNewline(string tagName) =>
        string.Equals(tagName, "pre", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tagName, "listing", StringComparison.OrdinalIgnoreCase);

    private static string WithoutLeadingNewline(string text)
    {
        if (text.StartsWith("\r\n", StringComparison.Ordinal)) return text.Substring(2);
        if (text.Length > 0 && (text[0] == '\n' || text[0] == '\r')) return text.Substring(1);
        return text;
    }

    private void ProcessEndTag(HtmlToken token)
    {
        var tagName = token.TagName!;

        if (tagName == "html" || tagName == "body" || tagName == "head")
            return;

        // Fast path: properly nested close — no scratch stack needed.
        if (_openElements.Count > 0)
        {
            var topElement = _openElements.Peek();
            if (topElement.TagName == tagName)
            {
                _openElements.Pop();
                return;
            }
            if (topElement.TagName == "body" || topElement.TagName == "html")
                return;
        }
        else
        {
            return;
        }

        // Pop elements until we find matching start tag
        var temp = new Stack<HtmlElement>();
        bool found = false;

        while (_openElements.Count > 0)
        {
            var top = _openElements.Peek();
            if (top.TagName == "body" || top.TagName == "html")
                break;

            if (top.TagName == tagName)
            {
                _openElements.Pop();
                found = true;
                break;
            }
            temp.Push(_openElements.Pop());
        }

        if (!found)
        {
            // Push back
            while (temp.Count > 0) _openElements.Push(temp.Pop());
        }
    }

    private void ProcessCharacter(HtmlToken token)
    {
        if (string.IsNullOrEmpty(token.Data))
            return;

        // If body doesn't exist yet and text is whitespace, ignore
        if (_bodyElement == null && string.IsNullOrWhiteSpace(token.Data))
            return;

        EnsureBodyElement();

        var parent = CurrentNode() ?? (HtmlNode)_bodyElement!;

        // Merge with previous text node
        if (parent.ChildNodes.Count > 0 && parent.ChildNodes[parent.ChildNodes.Count - 1] is HtmlTextNode lastText)
            lastText.Data += token.Data;
        else
            parent.AppendChild(new HtmlTextNode(token.Data));
    }

    /// <summary>
    /// Consume tokens from the tokenizer until the matching end tag, collecting all text into the element.
    /// Used for style, script, title, textarea.
    /// </summary>
    private bool HasOpenElement(string tagName)
    {
        foreach (var e in _openElements)
            if (e.TagName == tagName) return true;
        return false;
    }

    private void CollectRawText(HtmlElement element, string tagName)
    {
        if (_tokenizer == null) return;

        var textBuf = new StringBuilder();
        HtmlToken token;
        while ((token = _tokenizer.NextToken()).Type != HtmlTokenType.EndOfFile)
        {
            if (token.Type == HtmlTokenType.EndTag && string.Equals(token.TagName, tagName, StringComparison.OrdinalIgnoreCase))
                break;

            if (token.Type == HtmlTokenType.Character)
                textBuf.Append(token.Data);
            else if (token.Type == HtmlTokenType.StartTag)
                textBuf.Append($"<{token.TagName}>");  // raw text: tags are text
        }

        var rawText = textBuf.ToString();
        if (string.Equals(tagName, "textarea", StringComparison.OrdinalIgnoreCase))
            rawText = WithoutLeadingNewline(rawText);
        if (rawText.Length > 0)
            element.AppendChild(new HtmlTextNode(rawText));
    }

    private void EnsureHtmlElement()
    {
        if (_document.DocumentElement != null) return;
        var html = new HtmlElement("html");
        _document.AppendChild(html);
        _openElements.Push(html);
    }

    private void EnsureHeadElement()
    {
        if (_headElement != null) return;
        EnsureHtmlElement();
        _headElement = new HtmlElement("head");
        _document.DocumentElement!.AppendChild(_headElement);
    }

    private void EnsureBodyElement()
    {
        if (_bodyElement != null) return;
        EnsureHtmlElement();
        EnsureHeadElement();
        _bodyElement = new HtmlElement("body");
        _document.DocumentElement!.AppendChild(_bodyElement);
        _openElements.Clear();
        _openElements.Push(_document.DocumentElement!);
        _openElements.Push(_bodyElement);
    }

    private void CloseUpTo(string tagName)
    {
        while (_openElements.Count > 0)
        {
            var top = _openElements.Peek();
            if (top.TagName == "body" || top.TagName == "html") return;
            if (top.TagName == tagName) { _openElements.Pop(); return; }
            _openElements.Pop();
        }
    }

    private HtmlElement? CurrentNode() => _openElements.Count > 0 ? _openElements.Peek() : null;

    private static readonly HashSet<string> ListItemTags = new(StringComparer.OrdinalIgnoreCase) { "li" };
    private static readonly HashSet<string> DefinitionItemTags = new(StringComparer.OrdinalIgnoreCase) { "dd", "dt" };
    private static readonly HashSet<string> TableCellTags = new(StringComparer.OrdinalIgnoreCase) { "td", "th" };
    private static readonly HashSet<string> TableRowTags = new(StringComparer.OrdinalIgnoreCase) { "tr" };
    private static readonly HashSet<string> TableSectionTags = new(StringComparer.OrdinalIgnoreCase) { "thead", "tbody", "tfoot" };
    private static readonly HashSet<string> OptionTags = new(StringComparer.OrdinalIgnoreCase) { "option" };
    private static readonly HashSet<string> OptionGroupTags = new(StringComparer.OrdinalIgnoreCase) { "optgroup" };

    private static readonly HashSet<string> TableScopeBoundaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "html", "body", "table", "template"
    };

    private static readonly HashSet<string> ListItemScopeBoundaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "html", "body", "applet", "area", "article", "aside", "base", "basefont", "bgsound", "blockquote",
        "button", "caption", "center", "col", "colgroup", "dd", "details", "dir", "dl", "dt", "embed",
        "fieldset", "figcaption", "figure", "footer", "form", "frame", "frameset", "h1", "h2", "h3", "h4",
        "h5", "h6", "header", "hgroup", "hr", "iframe", "img", "input", "li", "link", "listing", "main",
        "marquee", "menu", "meta", "nav", "noembed", "noframes", "noscript", "object", "ol", "plaintext",
        "pre", "script", "search", "section", "select", "source", "style", "summary", "table", "tbody", "td",
        "template", "textarea", "tfoot", "th", "thead", "title", "tr", "track", "ul", "wbr", "xmp"
    };

    private void CloseImpliedEndTags(string tagName)
    {
        switch (tagName.ToLowerInvariant())
        {
            case "li":
                CloseOpenElementInScope(ListItemTags, ListItemScopeBoundaries);
                break;
            case "dd":
            case "dt":
                CloseOpenElementInScope(DefinitionItemTags, ListItemScopeBoundaries);
                break;
            case "td":
            case "th":
                CloseOpenElementInScope(TableCellTags, TableScopeBoundaries);
                break;
            case "tr":
                CloseOpenElementInScope(TableCellTags, TableScopeBoundaries);
                CloseOpenElementInScope(TableRowTags, TableScopeBoundaries);
                break;
            case "thead":
            case "tbody":
            case "tfoot":
                CloseOpenElementInScope(TableCellTags, TableScopeBoundaries);
                CloseOpenElementInScope(TableRowTags, TableScopeBoundaries);
                CloseOpenElementInScope(TableSectionTags, TableScopeBoundaries);
                break;
            case "option":
                if (CurrentNode()?.TagName == "option") _openElements.Pop();
                break;
            case "optgroup":
                if (CurrentNode()?.TagName == "option") _openElements.Pop();
                CloseOpenElementInScope(OptionGroupTags, OptionTags);
                break;
        }
    }

    private void CloseOpenElementInScope(HashSet<string> targets, HashSet<string> boundaries)
    {
        string? found = null;
        foreach (var open in _openElements)
        {
            if (targets.Contains(open.TagName))
            {
                found = open.TagName;
                break;
            }
            if (boundaries.Contains(open.TagName))
                return;
        }
        if (found != null)
            CloseUpTo(found);
    }

    private static HtmlElement CreateElement(HtmlToken token)
    {
        var elem = new HtmlElement(token.TagName!);
        foreach (var attr in token.Attributes)
            elem.SetAttribute(attr.Name, attr.Value);
        return elem;
    }
}
