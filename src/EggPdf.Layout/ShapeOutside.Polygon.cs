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
    /// inset(top [right [bottom [left]]] [round r1 [r2 [r3 [r4]]]]): the reference box shrunk by the given
    /// offsets (CSS shorthand order), with optional corner radii (top-left, top-right, bottom-right, bottom-left;
    /// percentages are elliptical: of the width horizontally, the height vertically).
    /// </summary>
    private static ShapeOutsideDescriptor? ParseInset(string value, float width, float height, float fontSize)
    {
        string? inner = InnerOfFunction(value);
        if (inner == null) return null;

        string? radiusPart = null;
        int round = inner.IndexOf("round", StringComparison.OrdinalIgnoreCase);
        if (round >= 0)
        {
            radiusPart = inner.Substring(round + 5);
            inner = inner.Substring(0, round);
        }

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

        var radii = radiusPart == null ? null : ParseCornerRadii(radiusPart, x1 - x0, y1 - y0, fontSize);
        if (radii == null) return new ShapeOutsideDescriptor(new[] { x0, y0, x1, y0, x1, y1, x0, y1 });

        // Corners (tl, tr, br, bl) -> (rx, ry); scale all down together when neighbours would overlap
        float w = x1 - x0, h = y1 - y0;
        float f = 1f;
        if (radii[0] + radii[2] > w) f = Math.Min(f, w / (radii[0] + radii[2]));
        if (radii[4] + radii[6] > w) f = Math.Min(f, w / (radii[4] + radii[6]));
        if (radii[1] + radii[7] > h) f = Math.Min(f, h / (radii[1] + radii[7]));
        if (radii[3] + radii[5] > h) f = Math.Min(f, h / (radii[3] + radii[5]));
        for (int i = 0; i < 8; i++) radii[i] *= f;

        var pts = new System.Collections.Generic.List<float>();
        void Arc(float cx, float cy, float rx, float ry, int fromDeg)
        {
            if (rx <= 0f || ry <= 0f) { pts.Add(cx); pts.Add(cy); return; }
            for (int k = 0; k <= 8; k++)
            {
                double a = (fromDeg + 90.0 * k / 8) * Math.PI / 180.0;
                pts.Add(cx + rx * (float)Math.Cos(a)); pts.Add(cy + ry * (float)Math.Sin(a));
            }
        }
        Arc(x0 + radii[0], y0 + radii[1], radii[0], radii[1], 180); // top-left
        Arc(x1 - radii[2], y0 + radii[3], radii[2], radii[3], 270); // top-right
        Arc(x1 - radii[4], y1 - radii[5], radii[4], radii[5], 0);   // bottom-right
        Arc(x0 + radii[6], y1 - radii[7], radii[6], radii[7], 90);  // bottom-left
        return new ShapeOutsideDescriptor(pts.ToArray());
    }

    /// <summary>Up to four corner radii in shorthand order as rx,ry pairs for tl, tr, br, bl (null when malformed).</summary>
    private static float[]? ParseCornerRadii(string value, float boxWidth, float boxHeight, float fontSize)
    {
        var tokens = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || tokens.Length > 4) return null;

        string Pick(int corner) // tl, tr, br, bl -> shorthand slots 0, 1, 2, 3
        {
            switch (tokens.Length)
            {
                case 1: return tokens[0];
                case 2: return corner % 2 == 0 ? tokens[0] : tokens[1];
                case 3: return corner == 0 ? tokens[0] : corner == 2 ? tokens[2] : tokens[1];
                default: return tokens[corner];
            }
        }

        var r = new float[8];
        for (int corner = 0; corner < 4; corner++)
        {
            var token = Pick(corner);
            if (!TryLength(token, boxWidth, fontSize, out float rx) || !TryLength(token, boxHeight, fontSize, out float ry)) return null;
            r[corner * 2] = Math.Max(0f, rx); r[corner * 2 + 1] = Math.Max(0f, ry);
        }
        return r;
    }

    /// <summary>
    /// url(image): the shape is the image's pixels whose alpha exceeds <paramref name="threshold"/>, stretched over the
    /// float's box. Needs <see cref="ImageLoader"/>; null (rectangular exclusion) when it is unset or the load fails.
    /// </summary>
    private static ShapeOutsideDescriptor? ParseUrl(string value, float width, float height, float threshold)
    {
        var loader = ImageLoader;
        string? source = InnerOfFunction(value)?.Trim().Trim('"', (char)39);
        if (loader == null || string.IsNullOrEmpty(source)) return null;

        var mask = loader(source!);
        if (mask == null) return null;
        var (aw, ah, alpha) = mask.Value;
        if (aw <= 0 || ah <= 0 || alpha.Length < aw * ah) return null;

        var left = new float[ah]; var right = new float[ah];
        int cut = (int)Math.Floor(Math.Max(0f, Math.Min(1f, threshold)) * 255f); // "above" the threshold: alpha > cut
        for (int y = 0; y < ah; y++)
        {
            int first = -1, last = -1;
            for (int x = 0; x < aw; x++)
            {
                if (alpha[y * aw + x] <= cut) continue;
                if (first < 0) first = x;
                last = x;
            }
            left[y] = first < 0 ? float.NaN : first * width / aw;
            right[y] = first < 0 ? float.NaN : (last + 1) * width / aw;
        }
        return new ShapeOutsideDescriptor(left, right, height);
    }

    /// <summary>shape-image-threshold: a number or percentage in 0..1 (default 0).</summary>
    /// <remarks>Also see <see cref="BlockLayout.ShapeImageLoader"/>, the public hook for url() shapes.</remarks>
    internal static float ParseThreshold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0f;
        var v = value!.Trim();
        bool percent = v.EndsWith("%", StringComparison.Ordinal);
        if (percent) v = v.Substring(0, v.Length - 1);
        if (!float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float n)) return 0f;
        return Math.Max(0f, Math.Min(1f, percent ? n / 100f : n));
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
