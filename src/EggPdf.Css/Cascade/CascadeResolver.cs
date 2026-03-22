using System;
using System.Collections.Generic;
using System.Linq;
using EggPdf.Css.Parser;
using EggPdf.Css.Selectors;
using EggPdf.Html.Dom;

namespace EggPdf.Css.Cascade;

/// <summary>
/// Resolves the computed style for an element by applying the CSS cascade:
/// UA defaults -> author stylesheets (by specificity) -> inline styles.
/// Handles @media filtering, !important, inheritance.
/// </summary>
public class CascadeResolver
{
    private readonly List<CssStyleRule> _authorRules = new();
    private readonly BasicStyleResolver _uaResolver = new();
    private readonly string _mediaType;

    public CascadeResolver(IEnumerable<CssStyleSheet> stylesheets, string mediaType = "print")
    {
        _mediaType = mediaType;

        foreach (var sheet in stylesheets)
        {
            // Direct rules
            _authorRules.AddRange(sheet.Rules);

            // @media rules - include if media matches
            foreach (var mediaRule in sheet.MediaRules)
            {
                if (MediaMatches(mediaRule.MediaQuery))
                    _authorRules.AddRange(mediaRule.Rules);
            }
        }
    }

    /// <summary>Resolve computed style for an element.</summary>
    public ComputedStyle Resolve(HtmlElement element, ComputedStyle? parentStyle)
    {
        // 1. Start with UA defaults + inheritance
        var style = _uaResolver.Resolve(element, parentStyle);

        // 2. Collect matching author rules with specificity
        var matches = new List<(CssDeclaration decl, int specificity, int order)>();
        int ruleOrder = 0;

        foreach (var rule in _authorRules)
        {
            if (SelectorMatcher.Matches(rule.SelectorText, element))
            {
                var spec = SelectorMatcher.CalculateSpecificity(rule.SelectorText);
                int specScore = spec.A * 10000 + spec.B * 100 + spec.C;

                foreach (var decl in rule.Declarations)
                {
                    matches.Add((decl, specScore, ruleOrder));
                }
            }
            ruleOrder++;
        }

        // 3. Sort: !important first, then by specificity, then by source order
        var sorted = matches
            .OrderBy(m => m.decl.Important ? 0 : 1)  // !important first
            .ThenBy(m => m.specificity)
            .ThenBy(m => m.order)
            .ToList();

        // 4. Apply in order (later wins for same property at same importance level)
        // Group by property, last one wins (unless !important overrides)
        var propertyWinners = new Dictionary<string, (string value, bool important, int specificity, int order)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (decl, spec, order) in sorted)
        {
            if (propertyWinners.TryGetValue(decl.Property, out var existing))
            {
                // !important always wins over non-important
                if (decl.Important && !existing.important)
                {
                    propertyWinners[decl.Property] = (decl.Value, decl.Important, spec, order);
                }
                else if (decl.Important == existing.important)
                {
                    // Same importance: higher specificity wins, or later source order
                    if (spec > existing.specificity || (spec == existing.specificity && order >= existing.order))
                    {
                        propertyWinners[decl.Property] = (decl.Value, decl.Important, spec, order);
                    }
                }
                // non-important cannot override !important
            }
            else
            {
                propertyWinners[decl.Property] = (decl.Value, decl.Important, spec, order);
            }
        }

        // Apply winners to style (expand shorthands)
        foreach (var kv in propertyWinners)
        {
            if (!CssShorthandExpander.TryExpand(kv.Key, kv.Value.value, style))
                style.Set(kv.Key, kv.Value.value);
        }

        // 5. Apply inline styles (highest priority, except for !important in stylesheets)
        var inlineCss = element.GetAttribute("style");
        if (!string.IsNullOrEmpty(inlineCss))
        {
            var inlineDecls = CssInlineParser.Parse(inlineCss);
            foreach (var decl in inlineDecls)
            {
                // Inline styles override author styles unless author has !important
                if (propertyWinners.TryGetValue(decl.Property, out var existing) && existing.important && !decl.Important)
                    continue; // author !important wins over inline non-important

                if (!CssShorthandExpander.TryExpand(decl.Property, decl.Value, style))
                    style.Set(decl.Property, decl.Value);
            }
        }

        // 6. Handle hidden attribute
        if (element.HasAttribute("hidden"))
            style.Set("display", "none");

        return style;
    }

    private bool MediaMatches(string mediaQuery)
    {
        var query = mediaQuery.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(query) || query == "all")
            return true;

        // Simple media type matching
        if (query == _mediaType)
            return true;

        if (query == "screen" && _mediaType == "print")
            return false;

        if (query == "print" && _mediaType == "screen")
            return false;

        // "not print" etc.
        if (query.StartsWith("not "))
        {
            var negated = query.Substring(4).Trim();
            return negated != _mediaType;
        }

        // Unknown media query: include by default
        return true;
    }
}
