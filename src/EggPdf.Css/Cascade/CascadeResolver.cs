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
    private readonly float _pageWidth;
    /// <summary>@property initial-values keyed by custom property name.</summary>
    private readonly Dictionary<string, string> _propertyInitialValues =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Custom @counter-style rules from all parsed stylesheets.</summary>
    public List<CssCounterStyleRule> CounterStyleRules { get; } = new();

    public CascadeResolver(IEnumerable<CssStyleSheet> stylesheets, string mediaType = "print",
        float pageWidth = 1240f)
    {
        _mediaType = mediaType;
        _pageWidth = pageWidth;

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

            // @container rules — evaluate against page width (best approximation for PDF)
            foreach (var containerRule in sheet.ContainerRules)
            {
                // Normalize: remove spaces around colon so "min-width : 400px" → "min-width:400px"
                var rawCondition = containerRule.Condition.Trim('(', ')').Trim();
                var condition = NormalizeCondition(rawCondition);
                if (ContainerQueryResolver.Evaluate(condition, _pageWidth, 0f))
                    _authorRules.AddRange(containerRule.Rules);
            }

            // @counter-style rules
            CounterStyleRules.AddRange(sheet.CounterStyleRules);

            // @property initial-values
            foreach (var propRule in sheet.PropertyRules)
            {
                if (!string.IsNullOrEmpty(propRule.Name) && propRule.InitialValue != null)
                    _propertyInitialValues[propRule.Name] = propRule.InitialValue;
            }
        }
    }

    /// <summary>Resolve computed style for an element.</summary>
    public ComputedStyle Resolve(HtmlElement element, ComputedStyle? parentStyle)
    {
        // 1. Start with UA defaults + inheritance
        var style = _uaResolver.Resolve(element, parentStyle);

        // 2. Collect matching author rules with specificity and layer order
        // Tuple: (decl, specificity, layerOrder, sourceOrder)
        // layerOrder: int.MaxValue = unlayered (wins), 0,1,2... = layer index (later wins)
        var matches = new List<(CssDeclaration decl, int specificity, int layerOrder, int order)>();
        int ruleOrder = 0;

        foreach (var rule in _authorRules)
        {
            if (SelectorMatcher.Matches(rule.SelectorText, element))
            {
                var spec = SelectorMatcher.CalculateSpecificity(rule.SelectorText);
                int specScore = spec.A * 10000 + spec.B * 100 + spec.C;

                foreach (var decl in rule.Declarations)
                {
                    matches.Add((decl, specScore, rule.LayerOrder, ruleOrder));
                }
            }
            ruleOrder++;
        }

        // 3. Sort: !important first, then specificity, then layer order, then source order
        // CSS cascade spec: unlayered > any layer (for non-!important rules).
        // Within layers: later layer index wins over earlier.
        // int.MaxValue (unlayered) naturally sorts after any layer index, so unlayered wins.
        matches.Sort((a, b) =>
        {
            int imp = (a.decl.Important ? 0 : 1).CompareTo(b.decl.Important ? 0 : 1);
            if (imp != 0) return imp;
            int spec = a.specificity.CompareTo(b.specificity);
            if (spec != 0) return spec;
            int layer = a.layerOrder.CompareTo(b.layerOrder);
            if (layer != 0) return layer;
            return a.order.CompareTo(b.order);
        });

        // 4. Apply in order (later wins for same property at same importance level)
        // Group by property, last one wins (unless !important overrides)
        var propertyWinners = new Dictionary<string, (string value, bool important, int specificity, int layerOrder, int order)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (decl, spec, layerOrder, order) in matches)
        {
            if (propertyWinners.TryGetValue(decl.Property, out var existing))
            {
                // !important always wins over non-important
                if (decl.Important && !existing.important)
                {
                    propertyWinners[decl.Property] = (decl.Value, decl.Important, spec, layerOrder, order);
                }
                else if (decl.Important == existing.important)
                {
                    // Same importance: higher specificity wins, then later layer, then later source order
                    bool wins = spec > existing.specificity
                        || (spec == existing.specificity && layerOrder > existing.layerOrder)
                        || (spec == existing.specificity && layerOrder == existing.layerOrder && order >= existing.order);
                    if (wins)
                    {
                        propertyWinners[decl.Property] = (decl.Value, decl.Important, spec, layerOrder, order);
                    }
                }
                // non-important cannot override !important
            }
            else
            {
                propertyWinners[decl.Property] = (decl.Value, decl.Important, spec, layerOrder, order);
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

        // 6.5. Resolve CSS-wide keywords: inherit, initial, unset, revert
        ResolveCssWideKeywords(style, parentStyle, element);

        // 7. Inherit custom properties from parent (all custom properties inherit per spec)
        if (parentStyle != null)
        {
            foreach (var kv in parentStyle.All)
            {
                if (CssVariableResolver.IsCustomProperty(kv.Key) && !style.Has(kv.Key))
                    style.Set(kv.Key, kv.Value);
            }
        }

        // 8a. Apply @property initial-values for custom properties not yet set
        foreach (var kv in _propertyInitialValues)
        {
            if (!style.Has(kv.Key))
                style.Set(kv.Key, kv.Value);
        }

        // 8. Resolve var() references in all non-custom property values
        ResolveCustomProperties(style);

        // 9. Map logical properties (margin-inline-start, padding-block, inline-size, etc.)
        //    to their physical counterparts based on writing direction.
        bool isRTL = style.Get("direction") == "rtl";
        bool isVertical = style.Get("writing-mode")?.IndexOf("vertical", System.StringComparison.OrdinalIgnoreCase) >= 0;
        LogicalPropertyResolver.Resolve(style, isRTL, isVertical);

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

        // ::before and ::after require a content property to generate a box.
        // ::first-line, ::first-letter, ::marker, and ::placeholder style existing content — no content property needed.
        if (pseudo != "first-line" && pseudo != "first-letter" && pseudo != "marker" && pseudo != "placeholder")
        {
            if (!style.Has("content")) return null;
        }

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
            case "font-variant":
            case "font-variant-caps":
            case "font-variant-numeric":
            case "font-variant-ligatures":
            case "font-variant-alternates":
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
            case "quotes":
            case "tab-size":
            case "font-feature-settings":
            case "font-synthesis":
            case "font-size-adjust":
            case "font-kerning":
            case "font-optical-sizing":
            case "font-variation-settings":
            case "print-color-adjust":
            case "text-emphasis":
            case "text-emphasis-style":
            case "text-emphasis-color":
            case "text-emphasis-position":
            case "hanging-punctuation":
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
                (kv.Value.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 kv.Value.IndexOf("env(", StringComparison.OrdinalIgnoreCase) >= 0))
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

    /// <summary>Remove spaces around colons in container conditions: "min-width : 400px" → "min-width:400px".</summary>
    private static string NormalizeCondition(string condition)
    {
        if (string.IsNullOrEmpty(condition)) return condition;
        var sb = new System.Text.StringBuilder(condition.Length);
        for (int i = 0; i < condition.Length; i++)
        {
            char c = condition[i];
            if (c == ':')
            {
                // Remove trailing space from sb
                while (sb.Length > 0 && sb[sb.Length - 1] == ' ')
                    sb.Remove(sb.Length - 1, 1);
                sb.Append(':');
                // Skip leading spaces after colon
                while (i + 1 < condition.Length && condition[i + 1] == ' ')
                    i++;
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    // Properties that are inherited by default per CSS spec
    private static readonly HashSet<string> _inheritedProps = new(StringComparer.OrdinalIgnoreCase)
    {
        "color", "font-family", "font-size", "font-weight", "font-style",
        "font-variant", "font-variant-caps", "font-variant-numeric",
        "font-variant-ligatures", "font-variant-alternates",
        "line-height", "text-align", "text-indent", "text-transform",
        "letter-spacing", "word-spacing", "white-space", "direction",
        "visibility", "list-style-type", "list-style-position",
        "border-collapse", "border-spacing", "caption-side", "empty-cells",
        "quotes", "tab-size",
        "font-feature-settings", "font-synthesis", "font-size-adjust",
        "font-kerning", "font-optical-sizing", "font-variation-settings",
        "print-color-adjust",
        "text-emphasis", "text-emphasis-style", "text-emphasis-color", "text-emphasis-position",
        "hanging-punctuation"
    };

    // CSS spec initial values for commonly used properties
    private static readonly Dictionary<string, string> _initialValues = new(StringComparer.OrdinalIgnoreCase)
    {
        ["color"] = "canvastext",
        ["background-color"] = "transparent",
        ["background-image"] = "none",
        ["font-size"] = "medium",
        ["font-weight"] = "normal",
        ["font-style"] = "normal",
        ["font-variant"] = "normal",
        ["line-height"] = "normal",
        ["text-align"] = "start",
        ["text-decoration"] = "none",
        ["text-transform"] = "none",
        ["letter-spacing"] = "normal",
        ["word-spacing"] = "normal",
        ["white-space"] = "normal",
        ["direction"] = "ltr",
        ["visibility"] = "visible",
        ["display"] = "inline",
        ["position"] = "static",
        ["overflow"] = "visible",
        ["opacity"] = "1",
        ["z-index"] = "auto",
        ["cursor"] = "auto",
        ["pointer-events"] = "auto",
        ["border-style"] = "none",
        ["border-width"] = "medium",
        ["border-color"] = "currentcolor",
        ["margin"] = "0",
        ["padding"] = "0",
        ["width"] = "auto",
        ["height"] = "auto",
        ["min-width"] = "0",
        ["min-height"] = "0",
        ["max-width"] = "none",
        ["max-height"] = "none",
        ["top"] = "auto",
        ["right"] = "auto",
        ["bottom"] = "auto",
        ["left"] = "auto",
        ["float"] = "none",
        ["clear"] = "none",
    };

    private static void ResolveCssWideKeywords(ComputedStyle style, ComputedStyle? parentStyle, HtmlElement element)
    {
        // Collect keys that need updating (avoid modifying while iterating)
        List<(string key, string newValue)>? updates = null;
        List<string>? removals = null;

        foreach (var kv in style.All)
        {
            var val = kv.Value;
            if (val == null) continue;
            var lower = val.Trim().ToLowerInvariant();
            if (lower != "inherit" && lower != "initial" && lower != "unset" && lower != "revert")
                continue;

            string? resolved = null;
            bool remove = false;

            if (lower == "inherit")
            {
                var parentVal = parentStyle?.Get(kv.Key);
                if (parentVal != null)
                    resolved = parentVal;
                else
                    remove = true; // no parent, treat as initial (unset)
            }
            else if (lower == "initial")
            {
                if (_initialValues.TryGetValue(kv.Key, out var init))
                    resolved = init;
                else
                    remove = true; // unrecognized property, remove the keyword
            }
            else if (lower == "unset")
            {
                bool isInherited = _inheritedProps.Contains(kv.Key) ||
                                   kv.Key.StartsWith("--", StringComparison.Ordinal);
                if (isInherited)
                {
                    var parentVal = parentStyle?.Get(kv.Key);
                    if (parentVal != null) resolved = parentVal;
                    else remove = true;
                }
                else
                {
                    if (_initialValues.TryGetValue(kv.Key, out var init))
                        resolved = init;
                    else
                        remove = true;
                }
            }
            else // revert: treat as inherit for inherited props, remove (UA default) otherwise
            {
                bool isInherited = _inheritedProps.Contains(kv.Key);
                if (isInherited)
                {
                    var parentVal = parentStyle?.Get(kv.Key);
                    if (parentVal != null) resolved = parentVal;
                    else remove = true;
                }
                else
                {
                    remove = true; // let UA defaults show through
                }
            }

            if (resolved != null)
            {
                if (updates == null) updates = new List<(string, string)>();
                updates.Add((kv.Key, resolved));
            }
            else if (remove)
            {
                if (removals == null) removals = new List<string>();
                removals.Add(kv.Key);
            }
        }

        if (updates != null)
            foreach (var (key, val) in updates)
                style.Set(key, val);

        if (removals != null)
            foreach (var key in removals)
                style.Remove(key);
    }
}
