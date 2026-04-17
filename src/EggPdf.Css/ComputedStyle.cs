using System;
using System.Collections.Generic;

namespace EggPdf.Css;

/// <summary>
/// Stores resolved CSS property values for an element.
/// For Phase 1, this is a simple string dictionary. Later phases will use typed values.
/// </summary>
public class ComputedStyle
{
    private readonly Dictionary<string, string> _properties = new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string property)
        => _properties.TryGetValue(property, out var val) ? val : null;

    public void Set(string property, string value)
        => _properties[property] = value;

    public bool Has(string property) => _properties.ContainsKey(property);

    public void Remove(string property) => _properties.Remove(property);

    public IEnumerable<KeyValuePair<string, string>> All => _properties;

    // Common property shortcuts
    public string Display => Get("display") ?? "inline";
    public string? Color => Get("color");
    public string? BackgroundColor => Get("background-color");
    public string? FontSize => Get("font-size");
    public string? FontWeight => Get("font-weight");
    public string? FontFamily => Get("font-family");
    public string? TextAlign => Get("text-align");
    public string? Width => Get("width");
    public string? Height => Get("height");
    public string? MarginTop => Get("margin-top");
    public string? MarginRight => Get("margin-right");
    public string? MarginBottom => Get("margin-bottom");
    public string? MarginLeft => Get("margin-left");
    public string? PaddingTop => Get("padding-top");
    public string? PaddingRight => Get("padding-right");
    public string? PaddingBottom => Get("padding-bottom");
    public string? PaddingLeft => Get("padding-left");
}
