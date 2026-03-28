using System.Collections.Generic;

namespace EggPdf.Svg;

/// <summary>
/// Represents a parsed SVG element with its tag name, attributes, and children.
/// </summary>
public class SvgElement
{
    public string TagName { get; set; } = "";
    public Dictionary<string, string> Attributes { get; set; } = new();
    public List<SvgElement> Children { get; set; } = new();
    public string TextContent { get; set; } = "";

    public string GetAttribute(string name)
    {
        return Attributes.TryGetValue(name, out var val) ? val : "";
    }
}
