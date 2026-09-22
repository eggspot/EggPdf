using System;
using System.Collections.Generic;
using EggPdf.Css.Parser;

namespace EggPdf;

public static partial class HtmlToPdf
{
    /// <summary>
    /// Whether the document sets font-stretch or font-variation-settings anywhere (inline, in a &lt;style&gt; block or
    /// in a linked stylesheet). Only then does layout measure installed variable fonts at their axes; every other
    /// document keeps its cheaper standard-metrics path.
    /// </summary>
    private static bool UsesFontVariations(string html, IReadOnlyList<CssStyleSheet>? stylesheets)
    {
        if (html.IndexOf("font-variation-settings", StringComparison.OrdinalIgnoreCase) >= 0 ||
            html.IndexOf("font-stretch", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (stylesheets == null) return false;

        foreach (var sheet in stylesheets)
        {
            if (HasVariationDeclaration(sheet.Rules)) return true;
            foreach (var media in sheet.MediaRules)
                if (HasVariationDeclaration(media.Rules)) return true;
        }
        return false;
    }

    private static bool HasVariationDeclaration(List<CssStyleRule> rules)
    {
        foreach (var rule in rules)
            foreach (var decl in rule.Declarations)
                if (decl.Property == "font-variation-settings" || decl.Property == "font-stretch") return true;
        return false;
    }
}
