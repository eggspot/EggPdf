using System;
using System.Collections.Generic;

namespace EggPdf.Html.Dom;

/// <summary>Base class for all DOM nodes.</summary>
public abstract class HtmlNode
{
    public HtmlNode? Parent { get; internal set; }
    public List<HtmlNode> ChildNodes { get; } = new();

    public void AppendChild(HtmlNode child)
    {
        child.Parent = this;
        ChildNodes.Add(child);
    }

    public void InsertBefore(HtmlNode child, HtmlNode? reference)
    {
        child.Parent = this;
        if (reference == null)
        {
            ChildNodes.Add(child);
            return;
        }
        int index = ChildNodes.IndexOf(reference);
        if (index >= 0)
            ChildNodes.Insert(index, child);
        else
            ChildNodes.Add(child);
    }

    public void RemoveChild(HtmlNode child)
    {
        ChildNodes.Remove(child);
        child.Parent = null;
    }
}

/// <summary>The root document node.</summary>
public class HtmlDocument : HtmlNode
{
    public HtmlElement? DocumentElement =>
        ChildNodes.Find(n => n is HtmlElement e && e.TagName == "html") as HtmlElement;

    public HtmlElement? Head =>
        DocumentElement?.ChildNodes.Find(n => n is HtmlElement e && e.TagName == "head") as HtmlElement;

    public HtmlElement? Body =>
        DocumentElement?.ChildNodes.Find(n => n is HtmlElement e && e.TagName == "body") as HtmlElement;
}

/// <summary>An HTML element with tag name and attributes.</summary>
public class HtmlElement : HtmlNode
{
    public string TagName { get; }
    private readonly Dictionary<string, string> _attributes = new(StringComparer.OrdinalIgnoreCase);

    public HtmlElement(string tagName)
    {
        TagName = tagName.ToLowerInvariant();
    }

    public string? Id => GetAttribute("id");

    public string[] ClassList
    {
        get
        {
            var cls = GetAttribute("class");
            if (string.IsNullOrWhiteSpace(cls)) return Array.Empty<string>();
            return cls.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }

    public string? GetAttribute(string name)
        => _attributes.TryGetValue(name, out var val) ? val : null;

    public bool HasAttribute(string name)
        => _attributes.ContainsKey(name);

    public void SetAttribute(string name, string value)
    {
        // Per HTML5 spec, first attribute wins on duplicates
        if (!_attributes.ContainsKey(name))
            _attributes[name] = value;
    }

    public IEnumerable<KeyValuePair<string, string>> Attributes => _attributes;
}

/// <summary>A text content node.</summary>
public class HtmlTextNode : HtmlNode
{
    public string Data { get; set; }

    public HtmlTextNode(string data)
    {
        Data = data;
    }
}

/// <summary>An HTML comment node.</summary>
public class HtmlComment : HtmlNode
{
    public string Data { get; }

    public HtmlComment(string data)
    {
        Data = data;
    }
}

/// <summary>A doctype node.</summary>
public class HtmlDocumentType : HtmlNode
{
    public string Name { get; }
    public string? PublicId { get; }
    public string? SystemId { get; }

    public HtmlDocumentType(string name, string? publicId = null, string? systemId = null)
    {
        Name = name;
        PublicId = publicId;
        SystemId = systemId;
    }
}
