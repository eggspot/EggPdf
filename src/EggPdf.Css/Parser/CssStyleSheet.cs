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
