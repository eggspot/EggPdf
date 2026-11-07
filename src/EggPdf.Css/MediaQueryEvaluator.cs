using System;
using System.Collections.Generic;

namespace EggPdf.Css;

internal sealed class MediaQueryEvaluator
{
    private const float MediaFontSizePx = 16f;

    private readonly string _mediaType;
    private readonly float _width;
    private readonly float _height;

    public MediaQueryEvaluator(string mediaType, float width, float height)
    {
        _mediaType = mediaType.ToLowerInvariant();
        _width = width;
        _height = height;
    }

    public bool Matches(string? mediaQueryList)
    {
        if (string.IsNullOrWhiteSpace(mediaQueryList)) return true;
        foreach (var query in SplitTopLevel(mediaQueryList!, ','))
        {
            if (MatchesQuery(query.Trim().ToLowerInvariant())) return true;
        }
        return false;
    }

    private bool MatchesQuery(string query)
    {
        if (query.Length == 0) return false;

        bool negate = false;
        if (StartsWithWord(query, "not"))
        {
            negate = true;
            query = query.Substring(3).TrimStart();
        }
        else if (StartsWithWord(query, "only"))
        {
            query = query.Substring(4).TrimStart();
        }

        bool matches = true;
        var parts = SplitOnAnd(query);
        if (parts.Count == 0 || parts.Exists(p => p.Length == 0)) return false;
        int first = 0;
        if (parts[0].Length > 0 && parts[0][0] != '(')
        {
            matches = MediaTypeMatches(parts[0]);
            first = 1;
        }
        else if (negate && parts[0].Length > 0 && parts[0][0] == '(')
        {
            return !EvaluateConditions(parts, 0);
        }

        if (matches && first < parts.Count)
            matches = EvaluateConditions(parts, first);

        return negate ? !matches : matches;
    }

    private bool EvaluateConditions(List<string> parts, int start)
    {
        for (int i = start; i < parts.Count; i++)
        {
            var part = parts[i];
            if (part.Length < 2 || part[0] != '(' || part[part.Length - 1] != ')') return false;
            if (!FeatureMatches(part.Substring(1, part.Length - 2).Trim())) return false;
        }
        return true;
    }

    private bool MediaTypeMatches(string type) => type == "all" || type == _mediaType;

    private bool FeatureMatches(string feature)
    {
        if (feature.IndexOf('<') >= 0 || feature.IndexOf('>') >= 0 || (feature.IndexOf('=') >= 0 && feature.IndexOf(':') < 0))
            return RangeMatches(feature);

        int colon = feature.IndexOf(':');
        if (colon < 0) return BooleanFeatureMatches(feature);

        var name = feature.Substring(0, colon).Trim();
        var value = feature.Substring(colon + 1).Trim();
        switch (name)
        {
            case "width": return TryLength(value, out float w) && Math.Abs(_width - w) < 0.5f;
            case "min-width": return TryLength(value, out float minW) && _width >= minW;
            case "max-width": return TryLength(value, out float maxW) && _width <= maxW;
            case "height": return TryLength(value, out float h) && Math.Abs(_height - h) < 0.5f;
            case "min-height": return TryLength(value, out float minH) && _height >= minH;
            case "max-height": return TryLength(value, out float maxH) && _height <= maxH;
            case "orientation": return value == (IsPortrait ? "portrait" : "landscape");
            case "aspect-ratio": return TryRatio(value, out float r) && Math.Abs(AspectRatio - r) < 0.001f;
            case "min-aspect-ratio": return TryRatio(value, out float minR) && AspectRatio >= minR;
            case "max-aspect-ratio": return TryRatio(value, out float maxR) && AspectRatio <= maxR;
            case "color": return TryInteger(value, out int bits) && bits == ColorBitsPerComponent;
            case "min-color": return TryInteger(value, out int minBits) && ColorBitsPerComponent >= minBits;
            case "max-color": return TryInteger(value, out int maxBits) && ColorBitsPerComponent <= maxBits;
            case "monochrome": return TryInteger(value, out int mono) && mono == 0;
            case "hover":
            case "any-hover":
            case "pointer":
            case "any-pointer":
                return value == "none";
            case "prefers-color-scheme": return value == "light";
            case "prefers-reduced-motion": return value == "no-preference";
            case "prefers-contrast": return value == "no-preference";
            case "scripting": return value == "none";
            case "update": return value == "none";
            default: return false;
        }
    }

    private bool BooleanFeatureMatches(string name)
    {
        switch (name)
        {
            case "width":
            case "height":
            case "orientation":
            case "aspect-ratio":
            case "color":
                return true;
            default:
                return false;
        }
    }

    private bool RangeMatches(string feature)
    {
        var tokens = SplitRange(feature);
        if (tokens.Count != 3 && tokens.Count != 5) return false;
        for (int i = 1; i < tokens.Count; i += 2)
        {
            if (!CompareTerms(tokens[i - 1], tokens[i], tokens[i + 1])) return false;
        }
        return true;
    }

    private bool CompareTerms(string left, string op, string right)
    {
        if (!TryRangeTerm(left, out float l) || !TryRangeTerm(right, out float r)) return false;
        switch (op)
        {
            case "<": return l < r;
            case "<=": return l <= r;
            case ">": return l > r;
            case ">=": return l >= r;
            case "=": return Math.Abs(l - r) < 0.5f;
            default: return false;
        }
    }

    private bool TryRangeTerm(string term, out float value)
    {
        switch (term)
        {
            case "width": value = _width; return true;
            case "height": value = _height; return true;
            case "aspect-ratio": value = AspectRatio; return true;
        }
        if (term.IndexOf('/') >= 0) return TryRatio(term, out value);
        return TryLength(term, out value);
    }

    private static List<string> SplitRange(string feature)
    {
        var tokens = new List<string>();
        int start = 0;
        int i = 0;
        while (i < feature.Length)
        {
            char c = feature[i];
            if (c == '<' || c == '>' || c == '=')
            {
                tokens.Add(feature.Substring(start, i - start).Trim());
                int opLength = i + 1 < feature.Length && feature[i + 1] == '=' && c != '=' ? 2 : 1;
                tokens.Add(feature.Substring(i, opLength));
                i += opLength;
                start = i;
                continue;
            }
            i++;
        }
        tokens.Add(feature.Substring(start).Trim());
        return tokens;
    }

    private const int ColorBitsPerComponent = 8;

    private bool IsPortrait => _height >= _width;

    private float AspectRatio => _height > 0f ? _width / _height : 0f;

    private static bool TryLength(string value, out float px)
    {
        px = 0f;
        if (value == "0") return true;
        int unitStart = CssLengthUnits.UnitStart(value);
        if (unitStart == 0 || unitStart == value.Length) return false;
        if (!CssLengthUnits.TryParseNumber(value, unitStart, out float number)) return false;
        var context = new CssLengthContext(0f, MediaFontSizePx, MediaFontSizePx, 0f, 0f);
        return CssLengthUnits.TryToPx(number, value, unitStart, context, out px);
    }

    private static bool TryRatio(string value, out float ratio)
    {
        ratio = 0f;
        int slash = value.IndexOf('/');
        if (slash < 0)
            return CssLengthUnits.TryParseNumber(value, value.Length, out ratio);
        var numerator = value.Substring(0, slash).Trim();
        var denominator = value.Substring(slash + 1).Trim();
        if (!CssLengthUnits.TryParseNumber(numerator, numerator.Length, out float n) ||
            !CssLengthUnits.TryParseNumber(denominator, denominator.Length, out float d) || d == 0f)
            return false;
        ratio = n / d;
        return true;
    }

    private static bool TryInteger(string value, out int result) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out result);

    private static bool StartsWithWord(string text, string word) =>
        text.StartsWith(word, StringComparison.Ordinal) && (text.Length == word.Length || char.IsWhiteSpace(text[word.Length]));

    private static List<string> SplitOnAnd(string query)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < query.Length; i++)
        {
            char c = query[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && IsAndKeywordAt(query, i))
            {
                parts.Add(query.Substring(start, i - start).Trim());
                start = i + 3;
                i += 2;
            }
        }
        parts.Add(query.Substring(start).Trim());
        return parts;
    }

    private static bool IsAndKeywordAt(string query, int i) =>
        i + 3 <= query.Length &&
        string.CompareOrdinal(query, i, "and", 0, 3) == 0 &&
        (i == 0 || char.IsWhiteSpace(query[i - 1]) || query[i - 1] == ')') &&
        (i + 3 == query.Length || char.IsWhiteSpace(query[i + 3]) || query[i + 3] == '(');

    private static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (c == separator && depth == 0)
            {
                parts.Add(text.Substring(start, i - start));
                start = i + 1;
            }
        }
        parts.Add(text.Substring(start));
        return parts;
    }
}
