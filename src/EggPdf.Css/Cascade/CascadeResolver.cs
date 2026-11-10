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
public partial class CascadeResolver
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

    private readonly List<string> _authorRuleSelectors = new();
    private readonly Dictionary<string, CascadeWinner> _winnersScratch = new(CssPropertyNames.Comparer);
    private readonly List<CascadeWinner> _orderedWinnersScratch = new();
    private readonly HashSet<string> _importantPropertiesScratch = new(CssPropertyNames.Comparer);

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
        new Dictionary<string, string>(CssPropertyNames.Comparer);

    /// <summary>Custom @counter-style rules from all parsed stylesheets.</summary>
    public List<CssCounterStyleRule> CounterStyleRules { get; } = new();

    public CascadeResolver(IEnumerable<CssStyleSheet> stylesheets, string mediaType = "print",
        float pageWidth = 1240f, float pageHeight = 0f)
    {
        _pageWidth = pageWidth;
        var media = new MediaQueryEvaluator(mediaType, pageWidth,
            pageHeight > 0f ? pageHeight : pageWidth * DefaultPageAspectRatio);

        var sheetRules = new List<CssStyleRule>();
        foreach (var sheet in stylesheets)
        {
            sheetRules.Clear();
            sheetRules.AddRange(sheet.Rules);

            foreach (var mediaRule in sheet.MediaRules)
            {
                if (media.Matches(mediaRule.MediaQuery))
                    sheetRules.AddRange(mediaRule.Rules);
            }

            foreach (var containerRule in sheet.ContainerRules)
            {
                var rawCondition = containerRule.Condition.Trim('(', ')').Trim();
                var condition = NormalizeCondition(rawCondition);
                if (ContainerQueryResolver.Evaluate(condition, _pageWidth, 0f))
                    sheetRules.AddRange(containerRule.Rules);
            }

            AddRulesInSourceOrder(sheetRules);

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

        var winners = _winnersScratch;
        winners.Clear();

        string elemTag = element.TagName;
        string? elemId = element.GetAttribute("id");
        string? elemClass = element.GetAttribute("class");

        for (int ri = 0; ri < _authorRules.Count; ri++)
        {
            var filter = _authorRuleFilters[ri];
            if (filter.SkipAlways) continue;
            if (!FilterMightMatch(in filter, elemTag, elemId, elemClass)) continue;

            if (SelectorMatcher.Matches(_authorRuleSelectors[ri], element))
                AddCandidates(winners, _authorRules[ri], _authorRuleSpecificities[ri], ri);
        }

        var inlineCss = element.GetAttribute("style");
        var (isRtlForLogical, isVerticalForLogical) = PredictWritingDirection(winners, element, inlineCss, style);
        var importantProperties = ApplyWinnersInCascadeOrder(winners, style, isRtlForLogical, isVerticalForLogical);

        if (!string.IsNullOrEmpty(inlineCss))
            ApplyInlineStyle(inlineCss!, style, importantProperties, isRtlForLogical, isVerticalForLogical);

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

        CssVariableResolver.SubstituteVariables(style);
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

        var winners = _winnersScratch;
        winners.Clear();
        foreach (var (baseSelector, rule, specScore, order) in candidates)
        {
            if (SelectorMatcher.Matches(baseSelector, element))
                AddCandidates(winners, rule, specScore, order);
        }

        if (winners.Count == 0) return null;

        var style = new ComputedStyle();
        if (parentStyle != null)
        {
            foreach (var kv in parentStyle.All)
            {
                if (IsInherited(kv.Key))
                    style.Set(kv.Key, kv.Value);
            }
        }

        var (isRtlForLogical, isVerticalForLogical) = PredictWritingDirection(winners, element, null, style);
        ApplyWinnersInCascadeOrder(winners, style, isRtlForLogical, isVerticalForLogical);

        if (pseudo != "first-line" && pseudo != "first-letter" && pseudo != "marker" && pseudo != "placeholder")
        {
            if (!style.Has("content")) return null;
        }

        CssVariableResolver.SubstituteVariables(style);
        ComputedFontMetrics.ComputeFontSizeAndLineHeight(style, parentStyle, _rootFontSizePx);
        return style;
    }

    private static bool IsInherited(string property) =>
        _inheritedProps.Contains(property) || CssVariableResolver.IsCustomProperty(property);

    private void AddRulesInSourceOrder(List<CssStyleRule> rules)
    {
        var ordered = new (CssStyleRule Rule, int Sequence)[rules.Count];
        for (int i = 0; i < rules.Count; i++) ordered[i] = (rules[i], i);
        Array.Sort(ordered, (a, b) =>
        {
            int bySource = a.Rule.SourceIndex.CompareTo(b.Rule.SourceIndex);
            return bySource != 0 ? bySource : a.Sequence.CompareTo(b.Sequence);
        });
        foreach (var (rule, _) in ordered)
            AddRule(rule);
    }

    private void AddRule(CssStyleRule rule)
    {
        var selectors = SelectorMatcher.SplitSelectorList(rule.SelectorText);
        foreach (var part in selectors)
        {
            var selector = part.Trim();
            if (selector.Length == 0) continue;
            var spec = SelectorMatcher.CalculateSpecificity(selector);
            int order = _authorRules.Count;
            _authorRuleSpecificities.Add(SpecificityScore(spec));
            _authorRules.Add(rule);
            _authorRuleSelectors.Add(selector);
            _authorRuleFilters.Add(BuildFilter(selector));
            IndexPseudoElementRule(rule, selector, order);
        }
    }

    private static int SpecificityScore((int A, int B, int C) spec) => spec.A * 10000 + spec.B * 100 + spec.C;

    private void IndexPseudoElementRule(CssStyleRule rule, string sel, int order)
    {
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
            int specScore = SpecificityScore(spec) + PseudoElementSpecificity;

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

    private void ResolveCssWideKeywords(ComputedStyle style, ComputedStyle? parentStyle, HtmlElement element)
    {
        List<(string key, string newValue)>? updates = null;
        List<string>? removals = null;
        ComputedStyle? userAgentStyle = null;

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
            else
            {
                userAgentStyle ??= _uaResolver.Resolve(element, parentStyle);
                resolved = userAgentStyle.Get(kv.Key);
                if (resolved == null) remove = true;
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
