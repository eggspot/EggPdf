using System;
using System.Globalization;

namespace EggPdf.Layout;

/// <summary>
/// A parsed `shape-outside: circle()` or `ellipse()` descriptor, in coordinates local to
/// the float's own border box (its "reference box" -- other geometry-box keywords like
/// margin-box/padding-box are not supported, border-box is used unconditionally).
/// polygon()/inset()/url() are a much larger surface (edge-intersection scanning, image
/// alpha-channel sampling) and are not supported: a float with one of those keeps its
/// plain rectangular exclusion, matching the "common case, not full spec" precedent
/// already used elsewhere (GPOS kerning, GSUB substitution, COLRv0-only color fonts).
/// </summary>
internal readonly struct ShapeOutsideDescriptor
{
    public readonly float Cx, Cy;
    public readonly float Rx, Ry;

    public ShapeOutsideDescriptor(float cx, float cy, float rx, float ry)
    {
        Cx = cx; Cy = cy; Rx = rx; Ry = ry;
    }

    /// <summary>
    /// The shape's rightmost X (local to the float's left edge) at a given local Y, or
    /// null if the shape doesn't reach that Y at all (the row falls entirely outside it).
    /// </summary>
    public float? RightEdgeAtLocalY(float localY)
    {
        if (Ry <= 0 || Rx <= 0) return null;
        float dy = localY - Cy;
        float t = 1f - (dy * dy) / (Ry * Ry);
        if (t < 0f) return null;
        return Cx + Rx * (float)Math.Sqrt(t);
    }

    /// <summary>The shape's leftmost X (local to the float's left edge) at a given local Y, or null if absent there.</summary>
    public float? LeftEdgeAtLocalY(float localY)
    {
        if (Ry <= 0 || Rx <= 0) return null;
        float dy = localY - Cy;
        float t = 1f - (dy * dy) / (Ry * Ry);
        if (t < 0f) return null;
        return Cx - Rx * (float)Math.Sqrt(t);
    }
}

internal static class ShapeOutsideParser
{
    /// <summary>
    /// Parse a `shape-outside` value into a circle/ellipse descriptor local to the float's
    /// own border box (width x height). Returns null for unsupported shapes (polygon,
    /// inset, url, none) or an unparseable value -- callers fall back to the plain
    /// rectangular float exclusion in that case.
    /// </summary>
    public static ShapeOutsideDescriptor? Parse(string? value, float width, float height, float fontSize)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var v = value!.Trim();

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
