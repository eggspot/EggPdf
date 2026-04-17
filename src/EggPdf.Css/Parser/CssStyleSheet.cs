using System.Collections.Generic;

namespace EggPdf.Css.Parser;

/// <summary>A parsed CSS stylesheet containing rules.</summary>
public class CssStyleSheet
{
    public List<CssStyleRule> Rules { get; } = new();
    public List<CssMediaRule> MediaRules { get; } = new();
    public List<CssFontFaceRule> FontFaceRules { get; } = new();
    public List<CssPageRule> PageRules { get; } = new();
    public List<CssImportRule> ImportRules { get; } = new();
    public List<CssCounterStyleRule> CounterStyleRules { get; } = new();
    /// <summary>CSS @property rules for typed custom properties.</summary>
    public List<CssPropertyRule> PropertyRules { get; } = new();
    /// <summary>CSS @container rules with size conditions.</summary>
    public List<CssContainerRule> ContainerRules { get; } = new();
}

/// <summary>@import rule with URL and optional media query.</summary>
public class CssImportRule
{
    public string Url { get; set; } = "";
    public string? MediaQuery { get; set; }
}

/// <summary>A style rule: selector + declarations.</summary>
public class CssStyleRule
{
    public string SelectorText { get; set; } = "";
    public List<CssDeclaration> Declarations { get; set; } = new();
    /// <summary>
    /// Layer priority index. Unlayered rules use int.MaxValue (highest priority).
    /// Layered rules use their declaration order (0, 1, 2…); later layers win over earlier ones.
    /// </summary>
    public int LayerOrder { get; set; } = int.MaxValue;
}

/// <summary>@media rule with nested rules.</summary>
public class CssMediaRule
{
    public string MediaQuery { get; set; } = "";
    public List<CssStyleRule> Rules { get; } = new();
}

/// <summary>@font-face rule.</summary>
public class CssFontFaceRule
{
    public List<CssDeclaration> Declarations { get; } = new();
}

/// <summary>@page rule.</summary>
public class CssPageRule
{
    public string? PageSelector { get; set; }
    public List<CssDeclaration> Declarations { get; } = new();
    /// <summary>Nested margin-box at-rules (@top-center, @bottom-left, etc.).</summary>
    public List<CssPageMarginBox> MarginBoxes { get; } = new();
}

/// <summary>A CSS @page margin box (e.g. @top-center, @bottom-right).</summary>
public class CssPageMarginBox
{
    /// <summary>The position identifier, e.g. "top-center", "bottom-right".</summary>
    public string Position { get; set; } = "";
    public List<CssDeclaration> Declarations { get; } = new();
}

/// <summary>CSS @property rule — defines a typed custom property with initial-value and inheritance.</summary>
public class CssPropertyRule
{
    /// <summary>Custom property name including the "--" prefix.</summary>
    public string Name { get; set; } = "";
    /// <summary>CSS syntax descriptor, e.g. "&lt;color&gt;", "&lt;length&gt;", "*".</summary>
    public string Syntax { get; set; } = "*";
    /// <summary>Whether the property inherits (default: true per spec).</summary>
    public bool Inherits { get; set; } = true;
    /// <summary>Initial value used when the property is not set.</summary>
    public string? InitialValue { get; set; }
}

/// <summary>@container rule with a size condition and nested style rules.</summary>
public class CssContainerRule
{
    /// <summary>Optional container name (empty = any container).</summary>
    public string ContainerName { get; set; } = "";
    /// <summary>The size condition, e.g. "(min-width: 300px)".</summary>
    public string Condition { get; set; } = "";
    public List<CssStyleRule> Rules { get; } = new();
}

/// <summary>@counter-style rule defining a custom list marker style.</summary>
public class CssCounterStyleRule
{
    public string Name { get; set; } = "";
    /// <summary>system: cyclic | symbolic | alphabetic | numeric | fixed | extends &lt;name&gt;</summary>
    public string System { get; set; } = "symbolic";
    /// <summary>Space/comma-separated symbols (may be quoted strings or identifiers).</summary>
    public List<string> Symbols { get; } = new();
    public string Prefix { get; set; } = "";
    public string Suffix { get; set; } = ". ";
    /// <summary>Name of the counter style this one extends (for system: extends).</summary>
    public string? Extends { get; set; }
    public string? Negative { get; set; }
    public string? Fallback { get; set; }
}
