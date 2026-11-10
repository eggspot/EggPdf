using System;
using System.Collections.Generic;
using EggPdf.Css.Parser;
using EggPdf.Html.Dom;

namespace EggPdf.Css.Cascade;

public partial class CascadeResolver
{
    private static readonly Comparison<CascadeWinner> ByCascadePrecedence = CascadeWinner.ComparePrecedence;

    private readonly struct CascadeWinner
    {
        public CascadeWinner(string property, string value, bool important, int layerOrder, int specificity, int order)
        {
            Property = property;
            Value = value;
            Important = important;
            LayerRank = important ? -layerOrder : layerOrder;
            Specificity = specificity;
            Order = order;
        }

        public string Property { get; }

        public string Value { get; }

        public bool Important { get; }

        public int LayerRank { get; }

        public int Specificity { get; }

        public int Order { get; }

        public static int ComparePrecedence(CascadeWinner a, CascadeWinner b)
        {
            if (a.Important != b.Important) return a.Important ? 1 : -1;
            int byLayer = a.LayerRank.CompareTo(b.LayerRank);
            if (byLayer != 0) return byLayer;
            int bySpecificity = a.Specificity.CompareTo(b.Specificity);
            return bySpecificity != 0 ? bySpecificity : a.Order.CompareTo(b.Order);
        }
    }

    private static void AddCandidates(Dictionary<string, CascadeWinner> winners, CssStyleRule rule, int specificity, int order)
    {
        foreach (var decl in rule.Declarations)
        {
            var candidate = new CascadeWinner(decl.Property, decl.Value, decl.Important, rule.LayerOrder, specificity, order);
            if (!winners.TryGetValue(decl.Property, out var existing) ||
                CascadeWinner.ComparePrecedence(candidate, existing) >= 0)
                winners[decl.Property] = candidate;
        }
    }

    /// <summary>
    /// Applies the winning declarations in cascade order. A logical declaration (margin-inline-start, ...) is applied to
    /// its physical property right away, for the element's predicted direction and writing mode, so the later of a
    /// logical and a physical declaration wins, as in browsers.
    /// </summary>
    private HashSet<string> ApplyWinnersInCascadeOrder(Dictionary<string, CascadeWinner> winners, ComputedStyle style,
        bool isRtl, bool isVertical)
    {
        var ordered = _orderedWinnersScratch;
        ordered.Clear();
        foreach (var winner in winners.Values) ordered.Add(winner);
        ordered.Sort(ByCascadePrecedence);

        var importantProperties = _importantPropertiesScratch;
        importantProperties.Clear();
        foreach (var winner in ordered)
        {
            if (!winner.Important)
            {
                if (!CssShorthandExpander.TryExpand(winner.Property, winner.Value, style))
                    style.Set(winner.Property, winner.Value);
                LogicalPropertyResolver.Apply(style, winner.Property, winner.Value, isRtl, isVertical, overwrite: true, null, null);
                continue;
            }

            importantProperties.Add(winner.Property);
            foreach (var kv in Expanded(winner.Property, winner.Value).All)
            {
                style.Set(kv.Key, kv.Value);
                importantProperties.Add(kv.Key);
            }
            LogicalPropertyResolver.Apply(style, winner.Property, winner.Value, isRtl, isVertical, overwrite: true, null, importantProperties);
        }
        return importantProperties;
    }

    private static void ApplyInlineStyle(string inlineCss, ComputedStyle style, HashSet<string> importantProperties,
        bool isRtl, bool isVertical)
    {
        foreach (var decl in CssInlineParser.Parse(inlineCss))
        {
            if (decl.Important || importantProperties.Count == 0)
            {
                if (!CssShorthandExpander.TryExpand(decl.Property, decl.Value, style))
                    style.Set(decl.Property, decl.Value);
                LogicalPropertyResolver.Apply(style, decl.Property, decl.Value, isRtl, isVertical, overwrite: true, null, null);
                continue;
            }
            if (importantProperties.Contains(decl.Property)) continue;
            foreach (var kv in Expanded(decl.Property, decl.Value).All)
            {
                if (!importantProperties.Contains(kv.Key))
                    style.Set(kv.Key, kv.Value);
            }
            LogicalPropertyResolver.Apply(style, decl.Property, decl.Value, isRtl, isVertical, overwrite: true, importantProperties, null);
        }
    }

    /// <summary>
    /// The element's direction and writing mode before its declarations are applied, so logical properties can be
    /// mapped in cascade order: an !important author declaration, then the inline style, then the winning author
    /// declaration, then the <c>dir</c> attribute, then what the element inherits.
    /// </summary>
    private static (bool IsRtl, bool IsVertical) PredictWritingDirection(Dictionary<string, CascadeWinner> winners,
        HtmlElement element, string? inlineCss, ComputedStyle inherited)
    {
        string? direction = null, writingMode = null;
        bool hasDirection = winners.TryGetValue("direction", out var directionWinner);
        bool hasWritingMode = winners.TryGetValue("writing-mode", out var modeWinner);
        if (hasDirection && directionWinner.Important) direction = directionWinner.Value;
        if (hasWritingMode && modeWinner.Important) writingMode = modeWinner.Value;

        if (!string.IsNullOrEmpty(inlineCss) &&
            (inlineCss!.IndexOf("direction", StringComparison.OrdinalIgnoreCase) >= 0 ||
             inlineCss.IndexOf("writing-mode", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            foreach (var decl in CssInlineParser.Parse(inlineCss))
            {
                if (decl.Property == "direction") direction ??= decl.Value;
                else if (decl.Property == "writing-mode") writingMode ??= decl.Value;
            }
        }

        if (hasDirection) direction ??= directionWinner.Value;
        if (hasWritingMode) writingMode ??= modeWinner.Value;
        if (direction == null)
        {
            var dirAttribute = element.GetAttribute("dir");
            if (string.Equals(dirAttribute, "rtl", StringComparison.OrdinalIgnoreCase)) direction = "rtl";
            else if (string.Equals(dirAttribute, "ltr", StringComparison.OrdinalIgnoreCase)) direction = "ltr";
        }
        direction ??= inherited.Get("direction");
        writingMode ??= inherited.Get("writing-mode");

        bool isVertical = writingMode != null && writingMode.IndexOf("vertical", StringComparison.OrdinalIgnoreCase) >= 0;
        return (direction == "rtl", isVertical);
    }

    private static ComputedStyle Expanded(string property, string value)
    {
        var expanded = new ComputedStyle();
        if (!CssShorthandExpander.TryExpand(property, value, expanded))
            expanded.Set(property, value);
        return expanded;
    }
}
