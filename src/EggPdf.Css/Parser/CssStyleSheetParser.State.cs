using System;
using System.Collections.Generic;
using System.Text;

namespace EggPdf.Css.Parser;

public static partial class CssStyleSheetParser
{
    private const int UnlayeredOrder = int.MaxValue;

    private sealed class ParseState
    {
        private readonly Dictionary<string, int> _layerOrders = new(StringComparer.OrdinalIgnoreCase);
        private int _nextLayerOrder;
        private int _nextSourceIndex;

        public int CurrentLayerOrder { get; set; } = UnlayeredOrder;

        public string? CurrentLayerName { get; set; }

        public int NextSourceIndex() => _nextSourceIndex++;

        public string QualifiedLayerName(string name) =>
            CurrentLayerName == null ? name : CurrentLayerName + "." + name;

        public int LayerOrderOf(string? qualifiedName)
        {
            if (string.IsNullOrEmpty(qualifiedName)) return _nextLayerOrder++;
            if (!_layerOrders.TryGetValue(qualifiedName!, out int order))
            {
                order = _nextLayerOrder++;
                _layerOrders[qualifiedName!] = order;
            }
            return order;
        }
    }

    private readonly struct BlockContext
    {
        public BlockContext(List<CssStyleRule> rules, string? mediaQuery)
        {
            Rules = rules;
            MediaQuery = mediaQuery;
        }

        public List<CssStyleRule> Rules { get; }

        public string? MediaQuery { get; }
    }

    private static class MediaQueryCombiner
    {
        public static string Combine(string? outer, string inner)
        {
            if (string.IsNullOrEmpty(outer)) return inner;
            if (string.IsNullOrEmpty(inner)) return outer!;

            var combined = new StringBuilder();
            foreach (var outerQuery in SplitTopLevelCommas(outer!))
            {
                foreach (var innerQuery in SplitTopLevelCommas(inner))
                {
                    if (combined.Length > 0) combined.Append(", ");
                    combined.Append(Conjunction(outerQuery, innerQuery));
                }
            }
            return combined.ToString();
        }

        private static string Conjunction(string outer, string inner)
        {
            if (inner.StartsWith("(", StringComparison.Ordinal)) return outer + " and " + inner;
            if (outer.StartsWith("(", StringComparison.Ordinal)) return inner + " and " + outer;
            return string.Equals(outer, inner, StringComparison.OrdinalIgnoreCase) ? outer : outer + " and " + inner;
        }

        private static List<string> SplitTopLevelCommas(string query)
        {
            var parts = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < query.Length; i++)
            {
                if (query[i] == '(') depth++;
                else if (query[i] == ')') depth--;
                else if (query[i] == ',' && depth == 0)
                {
                    parts.Add(query.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            parts.Add(query.Substring(start).Trim());
            return parts;
        }
    }
}
