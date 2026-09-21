using System;
using System.Collections.Generic;

namespace EggPdf.Layout;

internal static partial class ShapeOutsideParser
{
    /// <summary>
    /// polygon([nonzero|evenodd,] x y, x y, ...): at least three vertices, each coordinate a
    /// length or percentage of the reference box. The fill rule is accepted and ignored -- only
    /// the shape's horizontal extent per line box matters for wrapping, not its interior holes.
    /// </summary>
    private static ShapeOutsideDescriptor? ParsePolygon(string value, float width, float height, float fontSize)
    {
        string? inner = InnerOfFunction(value);
        if (inner == null) return null;

        var points = new List<float>();
        var vertices = inner.Split(',');
        for (int i = 0; i < vertices.Length; i++)
        {
            var part = vertices[i].Trim();
            if (i == 0 && (part.Equals("evenodd", StringComparison.OrdinalIgnoreCase)
                        || part.Equals("nonzero", StringComparison.OrdinalIgnoreCase)))
                continue;

            var coords = part.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (coords.Length != 2) return null;
            if (!TryLength(coords[0], width, fontSize, out float x) || !TryLength(coords[1], height, fontSize, out float y))
                return null;
            points.Add(x);
            points.Add(y);
        }

        return points.Count >= 6 ? new ShapeOutsideDescriptor(points.ToArray()) : (ShapeOutsideDescriptor?)null;
    }

    /// <summary>
    /// inset(top [right [bottom [left]]] [round ...]): the reference box shrunk by the given
    /// offsets (CSS shorthand order). The rounded-corner suffix is accepted but the exclusion
    /// stays rectangular -- corner rounding only shaves sub-line-height slivers.
    /// </summary>
    private static ShapeOutsideDescriptor? ParseInset(string value, float width, float height, float fontSize)
    {
        string? inner = InnerOfFunction(value);
        if (inner == null) return null;

        int round = inner.IndexOf("round", StringComparison.OrdinalIgnoreCase);
        if (round >= 0) inner = inner.Substring(0, round);

        var tokens = inner.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || tokens.Length > 4) return null;

        // CSS shorthand expansion; top/bottom resolve against the height, left/right the width
        string tTok = tokens[0];
        string rTok = tokens.Length > 1 ? tokens[1] : tTok;
        string bTok = tokens.Length > 2 ? tokens[2] : tTok;
        string lTok = tokens.Length > 3 ? tokens[3] : rTok;
        if (!TryLength(tTok, height, fontSize, out float top) || !TryLength(rTok, width, fontSize, out float right)
            || !TryLength(bTok, height, fontSize, out float bottom) || !TryLength(lTok, width, fontSize, out float left))
            return null;

        float x0 = left, x1 = Math.Max(left, width - right);
        float y0 = top, y1 = Math.Max(top, height - bottom);
        return new ShapeOutsideDescriptor(new[] { x0, y0, x1, y0, x1, y1, x0, y1 });
    }

    private static string? InnerOfFunction(string value)
    {
        int open = value.IndexOf('(');
        int close = value.LastIndexOf(')');
        return open < 0 || close <= open ? null : value.Substring(open + 1, close - open - 1).Trim();
    }

    /// <summary>Parse a length/percentage/zero token, rejecting anything that is not one.</summary>
    private static bool TryLength(string token, float axisSize, float fontSize, out float result)
    {
        result = 0;
        char last = token[token.Length - 1];
        bool numeric = char.IsDigit(last) || last == '%' || char.IsLetter(last);
        if (!numeric || !(char.IsDigit(token[0]) || token[0] == '-' || token[0] == '+' || token[0] == '.')) return false;
        result = BlockLayout.ResolveLength(token, axisSize, fontSize);
        return true;
    }
}
