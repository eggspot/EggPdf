using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Tagged PDF / PDF/UA structure tree for accessibility.
/// Maps HTML semantic elements to PDF structure elements with roles,
/// enabling screen readers and assistive technology to navigate the document.
/// </summary>
public class PdfTaggedStructure
{
    private readonly List<StructureElement> _elements = new();
    private StructureElement? _current;

    /// <summary>Document language (from html lang attribute).</summary>
    public string Language { get; set; } = "en";

    /// <summary>Begin a structure element (e.g., heading, paragraph, table).</summary>
    public void BeginElement(string htmlTag, string? altText = null, int pageIndex = 0)
    {
        var role = MapHtmlTagToRole(htmlTag);
        if (role == null) return;

        var element = new StructureElement
        {
            Role = role,
            AltText = altText,
            PageIndex = pageIndex,
            Parent = _current,
        };

        if (_current != null)
            _current.Children.Add(element);
        else
            _elements.Add(element);

        _current = element;
    }

    /// <summary>End the current structure element.</summary>
    public void EndElement()
    {
        if (_current != null)
            _current = _current.Parent;
    }

    /// <summary>Add a marked content reference (links structure to page content).</summary>
    public void AddContentReference(int mcid)
    {
        if (_current != null)
            _current.MCIDs.Add(mcid);
    }

    /// <summary>Get all root structure elements.</summary>
    public IReadOnlyList<StructureElement> RootElements => _elements;

    /// <summary>Check if there are any structure elements.</summary>
    public bool HasStructure => _elements.Count > 0;

    /// <summary>Map HTML tag to PDF structure role.</summary>
    public static string? MapHtmlTagToRole(string htmlTag)
    {
        switch (htmlTag.ToLowerInvariant())
        {
            // Document structure
            case "html": return "Document";
            case "body": return "Document";
            case "header": return "Header";
            case "footer": return "Footer";
            case "nav": return "Nav";
            case "main": return "Main";
            case "article": return "Art";
            case "section": return "Sect";
            case "aside": return "Aside";

            // Headings
            case "h1": return "H1";
            case "h2": return "H2";
            case "h3": return "H3";
            case "h4": return "H4";
            case "h5": return "H5";
            case "h6": return "H6";

            // Text
            case "p": return "P";
            case "span": return "Span";
            case "blockquote": return "BlockQuote";
            case "pre": return "Code";
            case "code": return "Code";

            // Lists
            case "ul": return "L";
            case "ol": return "L";
            case "li": return "LI";

            // Tables
            case "table": return "Table";
            case "thead": return "THead";
            case "tbody": return "TBody";
            case "tfoot": return "TFoot";
            case "tr": return "TR";
            case "th": return "TH";
            case "td": return "TD";
            case "caption": return "Caption";

            // Links and images
            case "a": return "Link";
            case "img": return "Figure";
            case "figure": return "Figure";
            case "figcaption": return "Caption";

            // Forms
            case "form": return "Form";
            case "input": return "Form";
            case "button": return "Form";
            case "select": return "Form";
            case "textarea": return "Form";
            case "label": return "Lbl";

            // Inline
            case "strong": return "Strong";
            case "b": return "Strong";
            case "em": return "Em";
            case "i": return "Em";

            default: return null;
        }
    }
}

/// <summary>A node in the PDF structure tree.</summary>
public class StructureElement
{
    /// <summary>PDF structure role (H1, P, Table, etc.).</summary>
    public string Role { get; set; } = "";

    /// <summary>Alternative text for accessibility (images).</summary>
    public string? AltText { get; set; }

    /// <summary>Page index this element appears on.</summary>
    public int PageIndex { get; set; }

    /// <summary>Marked content IDs that reference page content.</summary>
    public List<int> MCIDs { get; set; } = new();

    /// <summary>Child structure elements.</summary>
    public List<StructureElement> Children { get; set; } = new();

    /// <summary>Parent element (null for root).</summary>
    internal StructureElement? Parent { get; set; }
}
