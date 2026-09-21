using System;
using System.Globalization;

namespace EggPdf.Layout;

/// <summary>
/// A parsed `shape-outside` descriptor -- circle(), ellipse(), polygon() or inset() -- in
/// coordinates local to the float's own border box (its "reference box"; other geometry-box
/// keywords like margin-box/padding-box are not supported, border-box is used unconditionally).
/// url()/image alpha shapes are not supported (image pixels are not known at layout time): a
/// float with one keeps its plain rectangular exclusion, as does an unparseable value.
/// </summary>
internal readonly struct ShapeOutsideDescriptor
{
    public readonly float Cx, Cy;
    public readonly float Rx, Ry;
    /// <summary>polygon()/inset() vertices as x0,y0,x1,y1,... (null for circle/ellipse).</summary>
    private readonly float[]? _points;

    public ShapeOutsideDescriptor(float cx, float cy, float rx, float ry)
    {
        Cx = cx; Cy = cy; Rx = rx; Ry = ry;
        _points = null;
    }

    public ShapeOutsideDescriptor(float[] polygonPoints)
    {
        Cx = Cy = Rx = Ry = 0;
        _points = polygonPoints;
    }

    /// <summary>
    /// The shape's rightmost X (local to the float's left edge) anywhere in the local Y range
    /// [<paramref name="top"/>, <paramref name="bottom"/>] (a line box), or null if the shape
    /// does not reach that range at all.
    /// </summary>
    public float? RightEdgeInRange(float top, float bottom)
    {
        if (_points != null)
            return PolygonExtent(top, bottom, out _, out float max) ? max : (float?)null;

        float? best = null;
        SampleEllipse(top, bottom, +1, ref best);
        return best;
    }

    /// <summary>The shape's leftmost X (local to the float's left edge) in the local Y range, or null if absent.</summary>
    public float? LeftEdgeInRange(float top, float bottom)
    {
        if (_points != null)
            return PolygonExtent(top, bottom, out float min, out _) ? min : (float?)null;

        float? best = null;
        SampleEllipse(top, bottom, -1, ref best);
        return best;
    }

    /// <summary>
    /// Sample an ellipse at the range's top, middle and bottom -- a reasonable approximation of
    /// its extent over one line box without per-pixel scanning, matching how browsers commonly
    /// sample CSS Shapes. side=+1 keeps the largest right edge, -1 the smallest left edge.
    /// </summary>
    private void SampleEllipse(float top, float bottom, int side, ref float? best)
    {
        if (Ry <= 0 || Rx <= 0) return;
        for (int i = 0; i < 3; i++)
        {
            float dy = (i == 0 ? top : i == 1 ? (top + bottom) / 2f : bottom) - Cy;
            float t = 1f - (dy * dy) / (Ry * Ry);
            if (t < 0f) continue;
            float x = Cx + side * Rx * (float)Math.Sqrt(t);
            if (!best.HasValue || (side > 0 ? x > best.Value : x < best.Value)) best = x;
        }
    }

    /// <summary>
    /// Exact horizontal extent of the polygon inside a Y range: every edge is clipped to the
    /// range and both clipped endpoints contribute, so a vertex (or a horizontal edge) that
    /// lands between a line box's sample points is still seen.
    /// </summary>
    private bool PolygonExtent(float top, float bottom, out float min, out float max)
    {
        var p = _points!;
        min = float.MaxValue; max = float.MinValue;
        bool any = false;
        int n = p.Length / 2;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            float x0 = p[2 * i], y0 = p[2 * i + 1], x1 = p[2 * j], y1 = p[2 * j + 1];
            float loY = Math.Min(y0, y1), hiY = Math.Max(y0, y1);
            if (hiY < top || loY > bottom) continue;

            if (y0 == y1)
            {
                Include(x0, ref min, ref max);
                Include(x1, ref min, ref max);
            }
            else
            {
                float ya = Math.Max(loY, top), yb = Math.Min(hiY, bottom);
                Include(x0 + (x1 - x0) * (ya - y0) / (y1 - y0), ref min, ref max);
                Include(x0 + (x1 - x0) * (yb - y0) / (y1 - y0), ref min, ref max);
            }
            any = true;
        }
        return any;
    }

    private static void Include(float x, ref float min, ref float max)
    {
        if (x < min) min = x;
        if (x > max) max = x;
    }
}

internal static partial class ShapeOutsideParser
{
    /// <summary>
    /// Parse a `shape-outside` value into a descriptor local to the float's own border box
    /// (width x height). Returns null for unsupported shapes (url, none) or an unparseable
    /// value -- callers fall back to the plain rectangular float exclusion in that case.
    /// </summary>
    public static ShapeOutsideDescriptor? Parse(string? value, float width, float height, float fontSize)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var v = value!.Trim();

        if (v.StartsWith("polygon(", StringComparison.OrdinalIgnoreCase))
            return ParsePolygon(v, width, height, fontSize);
        if (v.StartsWith("inset(", StringComparison.OrdinalIgnoreCase))
            return ParseInset(v, width, height, fontSize);

        bool isEllipse = v.StartsWith("ellipse(", StringComparison.OrdinalIgnoreCase);
        bool isCircle = !isEllipse && v.StartsWith("circle(", StringComparison.OrdinalIgnoreCase);
        if (!isEllipse && !isCircle) return null;

        int openParen = v.IndexOf('(');
        int closeParen = v.LastIndexOf(')');
        if (openParen < 0 || closeParen <= openParen) return null;
        string inner = v.Substring(openParen + 1, closeParen - openParen - 1).Trim();

        // Split "[radius[radius]] [at <position>]"
        string radiusPart = inner;
        string? positionPart = null;
        int atIdx = inner.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
        if (atIdx >= 0)
        {
            radiusPart = inner.Substring(0, atIdx).Trim();
            positionPart = inner.Substring(atIdx + 4).Trim();
        }

        // Default position is the center of the reference box
        float cx = width / 2f, cy = height / 2f;
        if (!string.IsNullOrEmpty(positionPart))
        {
            var posTokens = positionPart!.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (posTokens.Length >= 1)
                cx = ResolvePositionComponent(posTokens[0], width, cx);
            if (posTokens.Length >= 2)
                cy = ResolvePositionComponent(posTokens[1], height, cy);
        }

        float closestSideX = Math.Min(cx, width - cx);
        float closestSideY = Math.Min(cy, height - cy);

        if (isCircle)
        {
            float r = ResolveRadius(radiusPart, width, height, fontSize, Math.Min(closestSideX, closestSideY));
            return new ShapeOutsideDescriptor(cx, cy, r, r);
        }

        // ellipse: up to two radii (rx ry); a lone token applies to both, mirroring the
        // same "sx defaults sy" pattern CSS uses for scale()/etc. elsewhere in this codebase
        var radiusTokens = radiusPart.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        float rx = radiusTokens.Length >= 1
            ? ResolveRadius(radiusTokens[0], width, height, fontSize, closestSideX)
            : closestSideX;
        float ry = radiusTokens.Length >= 2
            ? ResolveRadius(radiusTokens[1], width, height, fontSize, closestSideY)
            : (radiusTokens.Length >= 1 ? rx : closestSideY);

        return new ShapeOutsideDescriptor(cx, cy, rx, ry);
    }

    private static float ResolvePositionComponent(string token, float axisSize, float fallback)
    {
        switch (token.ToLowerInvariant())
        {
            case "center": return axisSize / 2f;
            case "left": case "top": return 0f;
            case "right": case "bottom": return axisSize;
        }
        if (token.EndsWith("%", StringComparison.Ordinal))
        {
            if (float.TryParse(token.Substring(0, token.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                return axisSize * pct / 100f;
            return fallback;
        }
        if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            token = token.Substring(0, token.Length - 2);
        return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float px) ? px : fallback;
    }

    private static float ResolveRadius(string token, float width, float height, float fontSize, float closestSide)
    {
        if (token.Equals("closest-side", StringComparison.OrdinalIgnoreCase) || token.Length == 0)
            return closestSide;
        if (token.Equals("farthest-side", StringComparison.OrdinalIgnoreCase))
            return Math.Max(width, height); // coarse approximation, rarely used
        if (token.EndsWith("%", StringComparison.Ordinal))
        {
            if (float.TryParse(token.Substring(0, token.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
            {
                // Percentage radii resolve against sqrt((w^2+h^2)/2) per the CSS Shapes spec.
                double refLen = Math.Sqrt((width * width + height * height) / 2.0);
                return (float)(refLen * pct / 100.0);
            }
            return closestSide;
        }
        var resolved = BlockLayout.ResolveLength(token, 0, fontSize);
        return resolved > 0 ? resolved : closestSide;
    }
}
