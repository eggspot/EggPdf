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
    private readonly List<int> _authorRuleSpecificities = new();
    private readonly List<RuleFilter> _authorRuleFilters = new();
    private readonly BasicStyleResolver _uaResolver = new();
    private float _rootFontSizePx = ComputedFontMetrics.InitialFontSizePx;

    /// <summary>
    /// Pseudo-element rules indexed by pseudo name (before/after/marker/...),
    /// with base selector and specificity precomputed — ResolvePseudoElement
    /// runs ~6x per element and must not rescan every author rule.
    /// </summary>
    private readonly Dictionary<string, List<(string baseSelector, CssStyleRule rule, int specScore, int order)>>
        _pseudoRules = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] PseudoElementNames =
        { "before", "after", "marker", "first-line", "first-letter", "placeholder" };

    private readonly List<(CssDeclaration decl, int specificity, int layerOrder, int order)> _matchScratch = new();
    private readonly Dictionary<string, (string value, bool important, int specificity, int layerOrder, int order)>
        _winnersScratch = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Cheap per-rule rejection data derived from the rightmost compound.
    /// Extra simple selectors (attributes, pseudo-classes) only narrow a
    /// match, so a required tag/id/class extracted before them stays sound.
    /// </summary>
    private struct RuleFilter
    {
        public string? Tag;
        public string? Id;
        public string? Class;
        public bool AlwaysCheck;
        public bool SkipAlways;
    }
    private const float DefaultPageAspectRatio = 1.4142f;
    private const int PseudoElementSpecificity = 1;
    private readonly float _pageWidth;
    /// <summary>@property initial-values keyed by custom property name.</summary>
    private readonly Dictionary<string, string> _propertyInitialValues =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Custom @counter-style rules from all parsed stylesheets.</summary>
    public List<CssCounterStyleRule> CounterStyleRules { get; } = new();

    public CascadeResolver(IEnumerable<CssStyleSheet> stylesheets, string mediaType = "print",
        float pageWidth = 1240f, float pageHeight = 0f)
    {
        _pageWidth = pageWidth;
        var media = new MediaQueryEvaluator(mediaType, pageWidth,
            pageHeight > 0f ? pageHeight : pageWidth * DefaultPageAspectRatio);

        foreach (var sheet in stylesheets)
        {
            AddRules(sheet.Rules);

            foreach (var mediaRule in sheet.MediaRules)
            {
                if (media.Matches(mediaRule.MediaQuery))
                    AddRules(mediaRule.Rules);
            }

            foreach (var containerRule in sheet.ContainerRules)
            {
                var rawCondition = containerRule.Condition.Trim('(', ')').Trim();
                var condition = NormalizeCondition(rawCondition);
                if (ContainerQueryResolver.Evaluate(condition, _pageWidth, 0f))
                    AddRules(containerRule.Rules);
            }

            CounterStyleRules.AddRange(sheet.CounterStyleRules);

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
        var style = _uaResolver.Resolve(element, parentStyle);

        var matches = _matchScratch;
        matches.Clear();

        string elemTag = element.TagName;
        string? elemId = element.GetAttribute("id");
        string? elemClass = element.GetAttribute("class");

        for (int ri = 0; ri < _authorRules.Count; ri++)
        {
            var filter = _authorRuleFilters[ri];
            if (filter.SkipAlways) continue;
            if (!FilterMightMatch(in filter, elemTag, elemId, elemClass)) continue;

            var rule = _authorRules[ri];
            if (SelectorMatcher.Matches(rule.SelectorText, element))
            {
                int specScore = _authorRuleSpecificities[ri];
                foreach (var decl in rule.Declarations)
                    matches.Add((decl, specScore, rule.LayerOrder, ri));
            }
        }

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

        var propertyWinners = _winnersScratch;
        propertyWinners.Clear();

        foreach (var (decl, spec, layerOrder, order) in matches)
        {
            if (propertyWinners.TryGetValue(decl.Property, out var existing))
            {
                if (decl.Important && !existing.important)
                {
                    propertyWinners[decl.Property] = (decl.Value, decl.Important, spec, layerOrder, order);
                }
                else if (decl.Important == existing.important)
                {
                    bool wins = spec > existing.specificity
                        || (spec == existing.specificity && layerOrder > existing.layerOrder)
                        || (spec == existing.specificity && layerOrder == existing.layerOrder && order >= existing.order);
                    if (wins)
                    {
                        propertyWinners[decl.Property] = (decl.Value, decl.Important, spec, layerOrder, order);
                    }
                }
            }
            else
            {
                propertyWinners[decl.Property] = (decl.Value, decl.Important, spec, layerOrder, order);
            }
        }

        foreach (var kv in propertyWinners)
        {
            if (!CssShorthandExpander.TryExpand(kv.Key, kv.Value.value, style))
                style.Set(kv.Key, kv.Value.value);
        }

        var inlineCss = element.GetAttribute("style");
        if (!string.IsNullOrEmpty(inlineCss))
        {
            var inlineDecls = CssInlineParser.Parse(inlineCss);
            foreach (var decl in inlineDecls)
            {
                if (propertyWinners.TryGetValue(decl.Property, out var existing) && existing.important && !decl.Important)
                    continue;

                if (!CssShorthandExpander.TryExpand(decl.Property, decl.Value, style))
                    style.Set(decl.Property, decl.Value);
            }
        }

        if (element.HasAttribute("hidden"))
            style.Set("display", "none");

        if (!style.Has("direction"))
        {
            var dirAttr = element.GetAttribute("dir");
            if (string.Equals(dirAttr, "rtl", System.StringComparison.OrdinalIgnoreCase))
                style.Set("direction", "rtl");
            else if (string.Equals(dirAttr, "ltr", System.StringComparison.OrdinalIgnoreCase))
                style.Set("direction", "ltr");
        }

        ResolveCssWideKeywords(style, parentStyle, element);

        if (parentStyle != null)
        {
            foreach (var kv in parentStyle.All)
            {
                if (CssVariableResolver.IsCustomProperty(kv.Key) && !style.Has(kv.Key))
                    style.Set(kv.Key, kv.Value);
            }
        }

        foreach (var kv in _propertyInitialValues)
        {
            if (!style.Has(kv.Key))
                style.Set(kv.Key, kv.Value);
        }

        ResolveCustomProperties(style);
        FontVariationMarker.Apply(style);

        bool isRTL = style.Get("direction") == "rtl";
        bool isVertical = style.Get("writing-mode")?.IndexOf("vertical", System.StringComparison.OrdinalIgnoreCase) >= 0;
        LogicalPropertyResolver.Resolve(style, isRTL, isVertical);

        bool isRoot = parentStyle == null;
        float fontSizePx = ComputedFontMetrics.ComputeFontSizeAndLineHeight(style, parentStyle,
            isRoot ? ComputedFontMetrics.InitialFontSizePx : _rootFontSizePx);
        if (isRoot)
            _rootFontSizePx = fontSizePx;

        return style;
    }

    /// <summary>Resolve computed style for a pseudo-element (::before or ::after).</summary>
    public ComputedStyle? ResolvePseudoElement(HtmlElement element, string pseudo, ComputedStyle? parentStyle)
    {
        if (!_pseudoRules.TryGetValue(pseudo, out var candidates))
            return null;

        var matches = new List<(CssDeclaration decl, int specificity, int order)>();
        foreach (var (baseSelector, rule, specScore, order) in candidates)
        {
            if (SelectorMatcher.Matches(baseSelector, element))
            {
                foreach (var decl in rule.Declarations)
                    matches.Add((decl, specScore, order));
            }
        }

        if (matches.Count == 0) return null;

        var style = new ComputedStyle();
        if (parentStyle != null)
        {
            foreach (var kv in parentStyle.All)
            {
                if (IsInherited(kv.Key))
                    style.Set(kv.Key, kv.Value);
            }
        }

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

        if (pseudo != "first-line" && pseudo != "first-letter" && pseudo != "marker" && pseudo != "placeholder")
        {
            if (!style.Has("content")) return null;
        }

        ResolveCustomProperties(style);
        ComputedFontMetrics.ComputeFontSizeAndLineHeight(style, parentStyle, _rootFontSizePx);
        return style;
    }

    private static bool IsInherited(string property)
    {
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
            case "white-space-collapse":
            case "text-wrap":
            case "text-wrap-mode":
            case "text-wrap-style":
            case "letter-spacing":
            case "word-spacing":
            case "word-break":
            case "overflow-wrap":
            case "word-wrap":
            case "hyphens":
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
        List<KeyValuePair<string, string>>? toResolve = null;
        foreach (var kv in style.All)
        {
            if (kv.Value.IndexOf('(') < 0) continue;
            if (!CssVariableResolver.IsCustomProperty(kv.Key) &&
                (kv.Value.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 kv.Value.IndexOf("env(", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                (toResolve ??= new List<KeyValuePair<string, string>>()).Add(kv);
            }
        }
        if (toResolve == null) return;

        foreach (var kv in toResolve)
        {
            var resolved = CssVariableResolver.ResolveVariables(kv.Value, style);
            if (resolved != kv.Value)
                style.Set(kv.Key, resolved);
        }
    }

    private void AddRules(IEnumerable<CssStyleRule> rules)
    {
        foreach (var rule in rules)
        {
            var spec = SelectorMatcher.CalculateSpecificity(rule.SelectorText);
            _authorRuleSpecificities.Add(spec.A * 10000 + spec.B * 100 + spec.C);
            int order = _authorRules.Count;
            _authorRules.Add(rule);
            _authorRuleFilters.Add(BuildFilter(rule.SelectorText));
            IndexPseudoElementRule(rule, order);
        }
    }

    /// <summary>If the selector targets a pseudo-element, index it by pseudo name.</summary>
    private void IndexPseudoElementRule(CssStyleRule rule, int order)
    {
        var sel = rule.SelectorText;
        foreach (var name in PseudoElementNames)
        {
            string? baseSelector = null;
            var doubleColon = "::" + name;
            var singleColon = ":" + name;
            if (sel.EndsWith(doubleColon, StringComparison.OrdinalIgnoreCase))
                baseSelector = sel.Substring(0, sel.Length - doubleColon.Length);
            else if (sel.EndsWith(singleColon, StringComparison.OrdinalIgnoreCase))
                baseSelector = sel.Substring(0, sel.Length - singleColon.Length);

            if (string.IsNullOrEmpty(baseSelector)) continue;

            var spec = SelectorMatcher.CalculateSpecificity(baseSelector!);
            int specScore = spec.A * 10000 + spec.B * 100 + spec.C + PseudoElementSpecificity;

            if (!_pseudoRules.TryGetValue(name, out var list))
                _pseudoRules[name] = list = new List<(string, CssStyleRule, int, int)>();
            list.Add((baseSelector!, rule, specScore, order));
            return;
        }
    }

    /// <summary>Extract cheap rejection requirements from the rightmost compound.</summary>
    private static RuleFilter BuildFilter(string selectorText)
    {
        var f = new RuleFilter();
        var sel = selectorText.Trim();

        if (sel.Length == 0 || sel.IndexOf(',') >= 0 || sel.IndexOf('\\') >= 0)
        {
            f.AlwaysCheck = true;
            return f;
        }

        if (sel.IndexOf("::", StringComparison.Ordinal) >= 0)
        {
            if (SelectorMatcher.HasPseudoElement(sel))
            {
                f.SkipAlways = true;
                return f;
            }
            f.AlwaysCheck = true;
            return f;
        }

        int start = SelectorMatcher.RightmostCompoundStart(sel);

        int p = start;
        if (p < sel.Length && sel[p] == '*')
        {
            p++;
        }
        else
        {
            int identStart = p;
            while (p < sel.Length && (char.IsLetterOrDigit(sel[p]) || sel[p] == '-' || sel[p] == '_')) p++;
            if (p > identStart) f.Tag = sel.Substring(identStart, p - identStart);
        }

        while (p < sel.Length && (sel[p] == '.' || sel[p] == '#'))
        {
            char kind = sel[p];
            p++;
            int identStart = p;
            while (p < sel.Length && (char.IsLetterOrDigit(sel[p]) || sel[p] == '-' || sel[p] == '_')) p++;
            if (p == identStart) { f.AlwaysCheck = true; return f; }
            var ident = sel.Substring(identStart, p - identStart);
            if (kind == '#') f.Id = ident;
            else if (f.Class == null) f.Class = ident;
        }

        if (f.Tag == null && f.Id == null && f.Class == null)
            f.AlwaysCheck = true;
        return f;
    }

    private static bool FilterMightMatch(in RuleFilter f, string tagName, string? id, string? classAttr)
    {
        if (f.AlwaysCheck) return true;
        if (f.Tag != null && !string.Equals(f.Tag, tagName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (f.Id != null && !string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase))
            return false;
        if (f.Class != null && (classAttr == null ||
            classAttr.IndexOf(f.Class, StringComparison.OrdinalIgnoreCase) < 0))
            return false;
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
                while (sb.Length > 0 && sb[sb.Length - 1] == ' ')
                    sb.Remove(sb.Length - 1, 1);
                sb.Append(':');
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

    private static readonly HashSet<string> _inheritedProps = new(StringComparer.OrdinalIgnoreCase)
    {
        "color", "font-family", "font-size", "font-weight", "font-style",
        "font-variant", "font-variant-caps", "font-variant-numeric",
        "font-variant-ligatures", "font-variant-alternates",
        "line-height", "text-align", "text-indent", "text-transform",
        "letter-spacing", "word-spacing", "white-space", "direction",
        "white-space-collapse", "text-wrap", "text-wrap-mode", "text-wrap-style",
        "word-break", "overflow-wrap", "word-wrap", "hyphens",
        "visibility", "list-style-type", "list-style-position",
        "border-collapse", "border-spacing", "caption-side", "empty-cells",
        "quotes", "tab-size",
        "font-feature-settings", "font-synthesis", "font-size-adjust",
        "font-kerning", "font-optical-sizing", "font-variation-settings", "font-stretch",
        "print-color-adjust",
        "text-emphasis", "text-emphasis-style", "text-emphasis-color", "text-emphasis-position",
        "hanging-punctuation"
    };

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
        List<(string key, string newValue)>? updates = null;
        List<string>? removals = null;

        foreach (var kv in style.All)
        {
            var val = kv.Value;
            if (val == null) continue;
            if (val.Length < 5) continue;
            var trimmed = val.Trim();
            if (trimmed.Length < 5 || trimmed.Length > 7) continue;
            string lower;
            if (trimmed.Equals("inherit", StringComparison.OrdinalIgnoreCase)) lower = "inherit";
            else if (trimmed.Equals("initial", StringComparison.OrdinalIgnoreCase)) lower = "initial";
            else if (trimmed.Equals("unset", StringComparison.OrdinalIgnoreCase)) lower = "unset";
            else if (trimmed.Equals("revert", StringComparison.OrdinalIgnoreCase)) lower = "revert";
            else continue;

            string? resolved = null;
            bool remove = false;

            if (lower == "inherit")
            {
                var parentVal = parentStyle?.Get(kv.Key);
                if (parentVal != null)
                    resolved = parentVal;
                else
                    remove = true;
            }
            else if (lower == "initial")
            {
                if (_initialValues.TryGetValue(kv.Key, out var init))
                    resolved = init;
                else
                    remove = true;
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
                    remove = true;
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
