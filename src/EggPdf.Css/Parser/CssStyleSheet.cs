using System.Collections.Generic;

namespace EggPdf.Css.Parser;

/// <summary>A parsed CSS stylesheet containing rules.</summary>
public class CssStyleSheet
{
    public List<CssStyleRule> Rules { get; } = new();
    public List<CssMediaRule> MediaRules { get; } = new();
    public List<CssFontFaceRule> FontFaceRules { get; } = new();
    public List<CssPageRule> PageRules { get; } = new();
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
