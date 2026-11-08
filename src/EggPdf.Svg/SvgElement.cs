using System.Collections.Generic;

namespace EggPdf.Svg;

/// <summary>
/// Represents a parsed SVG element with its tag name, attributes, and children.
/// </summary>
public class SvgElement
{
    /// <summary>The lower-case element name, or <c>#text</c> for a run of character data inside text.</summary>
    public string TagName { get; set; } = "";

    /// <summary>Attributes plus the effective presentation properties (own, from <c>style</c>, or inherited).</summary>
    public Dictionary<string, string> Attributes { get; set; } = new();

    /// <summary>Child elements in document order.</summary>
    public List<SvgElement> Children { get; set; } = new();

    /// <summary>The element's own character data, concatenated.</summary>
    public string TextContent { get; set; } = "";

    internal HashSet<string>? InheritedPropertyNames { get; set; }

    /// <summary>The attribute's value, or an empty string when it is absent.</summary>
    public string GetAttribute(string name)
    {
        return Attributes.TryGetValue(name, out var val) ? val : "";
    }
}
