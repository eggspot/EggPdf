using System;
using System.Collections.Generic;
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
        matches.Sort((a, b) =>
        {
            int imp = (a.decl.Important ? 0 : 1).CompareTo(b.decl.Important ? 0 : 1);
            if (imp != 0) return imp;
            int spec = a.specificity.CompareTo(b.specificity);
            if (spec != 0) return spec;
            return a.order.CompareTo(b.order);
        });

        // 4. Apply in order (later wins for same property at same importance level)
        // Group by property, last one wins (unless !important overrides)
        var propertyWinners = new Dictionary<string, (string value, bool important, int specificity, int order)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (decl, spec, order) in matches)
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

        // 7. Inherit custom properties from parent (all custom properties inherit per spec)
        if (parentStyle != null)
        {
            foreach (var kv in parentStyle.All)
            {
                if (CssVariableResolver.IsCustomProperty(kv.Key) && !style.Has(kv.Key))
                    style.Set(kv.Key, kv.Value);
            }
        }

        // 8. Resolve var() references in all non-custom property values
        ResolveCustomProperties(style);

        return style;
    }

    /// <summary>Resolve computed style for a pseudo-element (::before or ::after).</summary>
    public ComputedStyle? ResolvePseudoElement(HtmlElement element, string pseudo, ComputedStyle? parentStyle)
    {
        // Collect matching rules that target this pseudo-element
        string pseudoSuffix = "::" + pseudo;
        string pseudoSuffixSingle = ":" + pseudo; // legacy single-colon
        var matches = new List<(CssDeclaration decl, int specificity, int order)>();
        int ruleOrder = 0;

        foreach (var rule in _authorRules)
        {
            var sel = rule.SelectorText;
            bool hasPseudo = false;
            string? baseSelector = null;

            if (sel.EndsWith(pseudoSuffix, StringComparison.OrdinalIgnoreCase))
            {
                baseSelector = sel.Substring(0, sel.Length - pseudoSuffix.Length);
                hasPseudo = true;
            }
            else if (sel.EndsWith(pseudoSuffixSingle, StringComparison.OrdinalIgnoreCase))
            {
                baseSelector = sel.Substring(0, sel.Length - pseudoSuffixSingle.Length);
                hasPseudo = true;
            }

            if (hasPseudo && !string.IsNullOrEmpty(baseSelector) && SelectorMatcher.Matches(baseSelector, element))
            {
                var spec = SelectorMatcher.CalculateSpecificity(baseSelector);
                int specScore = spec.A * 10000 + spec.B * 100 + spec.C + 1; // +1 for pseudo-element

                foreach (var decl in rule.Declarations)
                    matches.Add((decl, specScore, ruleOrder));
            }
            ruleOrder++;
        }

        if (matches.Count == 0) return null;

        // Build a style inheriting from parent
        var style = new ComputedStyle();
        if (parentStyle != null)
        {
            // Inherit inheritable properties from parent
            foreach (var kv in parentStyle.All)
            {
                if (IsInherited(kv.Key))
                    style.Set(kv.Key, kv.Value);
            }
        }

        // Apply matched declarations (sorted by specificity then order)
        matches.Sort((a, b) =>
        {
            int spec = a.specificity.CompareTo(b.specificity);
            return spec != 0 ? spec : a.order.CompareTo(b.order);
        });
        foreach (var (decl, _, _) in matches)
        {
            if (!CssShorthandExpander.TryExpand(decl.Property, decl.Value, style))
                style.Set(decl.Property, decl.Value);
        }

        // Only return if there is a content property
        if (!style.Has("content")) return null;

        ResolveCustomProperties(style);
        return style;
    }

    private static bool IsInherited(string property)
    {
        // Simplified set of inherited properties
        switch (property.ToLowerInvariant())
        {
            case "color":
            case "font-family":
            case "font-size":
            case "font-weight":
            case "font-style":
            case "line-height":
            case "text-align":
            case "white-space":
            case "letter-spacing":
            case "word-spacing":
            case "visibility":
            case "text-transform":
            case "direction":
            case "list-style-type":
            case "list-style-position":
            case "border-collapse":
            case "border-spacing":
            case "caption-side":
            case "empty-cells":
                return true;
            default:
                return CssVariableResolver.IsCustomProperty(property);
        }
    }

    /// <summary>
    /// Resolve all var() references in non-custom property values.
    /// Custom properties themselves are not resolved (they store raw values).
    /// </summary>
    private static void ResolveCustomProperties(ComputedStyle style)
    {
        // Collect properties that need resolution (avoid modifying during iteration)
        var toResolve = new List<KeyValuePair<string, string>>();
        foreach (var kv in style.All)
        {
            if (!CssVariableResolver.IsCustomProperty(kv.Key) &&
                kv.Value.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                toResolve.Add(kv);
            }
        }

        foreach (var kv in toResolve)
        {
            var resolved = CssVariableResolver.ResolveVariables(kv.Value, style);
            if (resolved != kv.Value)
                style.Set(kv.Key, resolved);
        }
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
