using System;
using System.Collections.Generic;
using EggPdf.Css.Parser;
using EggPdf.Html.Dom;

namespace EggPdf;

public static partial class HtmlToPdf
{
    /// <summary>
    /// Whether the document sets font-stretch or font-variation-settings anywhere (inline, in a &lt;style&gt; block or
    /// in a linked stylesheet). Only then does layout measure installed variable fonts at their axes; every other
    /// document keeps its cheaper standard-metrics path. Internal (not private) so EggPdf.Tests.Unit can exercise
    /// the DOM-walk path directly.
    /// </summary>
    internal static bool UsesFontVariations(HtmlDocument document, IReadOnlyList<CssStyleSheet>? stylesheets)
    {
        if (HasInlineVariationStyle(document)) return true;
        if (stylesheets == null) return false;

        foreach (var sheet in stylesheets)
        {
            if (HasVariationDeclaration(sheet.Rules)) return true;
            foreach (var media in sheet.MediaRules)
                if (HasVariationDeclaration(media.Rules)) return true;
        }
        return false;
    }

    /// <summary>
    /// Finds font-stretch/font-variation-settings anywhere in the DOM's own text: inline style=""
    /// attributes (which never reach <see cref="CssStyleSheet"/>) and the raw text of every
    /// &lt;style&gt; element, which also covers at-rules the structured stylesheet check doesn't
    /// walk (@supports, @layer, nested rules). This is the DOM equivalent of the raw-HTML substring
    /// scan it replaced. Iterative, not recursive: the parser accepts arbitrarily deep nesting, and a
    /// recursive walk would turn that into an uncatchable stack overflow.
    /// </summary>
    private static bool HasInlineVariationStyle(HtmlNode root)
    {
        var pending = new Stack<HtmlNode>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node is HtmlElement el)
            {
                // Inline style="" and SVG presentation attributes (<text font-stretch="condensed">).
                // Name lookups, not attribute enumeration: this runs per element on every render.
                if (MentionsVariation(el.GetAttribute("style")) ||
                    el.HasAttribute("font-stretch") || el.HasAttribute("font-variation-settings")) return true;
                if (el.TagName == "style")
                {
                    foreach (var child in el.ChildNodes)
                        if (child is HtmlTextNode text && MentionsVariation(text.Data)) return true;
                }
            }
            for (int i = 0; i < node.ChildNodes.Count; i++)
                pending.Push(node.ChildNodes[i]);
        }
        return false;
    }

    private static bool MentionsVariation(string? css)
        => css != null &&
           (css.IndexOf("font-variation-settings", StringComparison.OrdinalIgnoreCase) >= 0 ||
            css.IndexOf("font-stretch", StringComparison.OrdinalIgnoreCase) >= 0);

    private static bool HasVariationDeclaration(List<CssStyleRule> rules)
    {
        foreach (var rule in rules)
            foreach (var decl in rule.Declarations)
                if (string.Equals(decl.Property, "font-variation-settings", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(decl.Property, "font-stretch", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
