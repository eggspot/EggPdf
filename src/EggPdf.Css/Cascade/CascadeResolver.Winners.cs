using System;
using System.Collections.Generic;
using EggPdf.Css.Parser;

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

    private HashSet<string> ApplyWinnersInCascadeOrder(Dictionary<string, CascadeWinner> winners, ComputedStyle style)
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
                continue;
            }

            importantProperties.Add(winner.Property);
            foreach (var kv in Expanded(winner.Property, winner.Value).All)
            {
                style.Set(kv.Key, kv.Value);
                importantProperties.Add(kv.Key);
            }
        }
        return importantProperties;
    }

    private static ComputedStyle Expanded(string property, string value)
    {
        var expanded = new ComputedStyle();
        if (!CssShorthandExpander.TryExpand(property, value, expanded))
            expanded.Set(property, value);
        return expanded;
    }
}
