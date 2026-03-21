using System;

namespace EggPdf.Core;

/// <summary>A 2D point.</summary>
public readonly struct PointF : IEquatable<PointF>
{
    public float X { get; }
    public float Y { get; }
    public PointF(float x, float y) { X = x; Y = y; }
    public bool Equals(PointF other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is PointF o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public static bool operator ==(PointF a, PointF b) => a.Equals(b);
    public static bool operator !=(PointF a, PointF b) => !a.Equals(b);
}

/// <summary>A 2D size.</summary>
public readonly struct SizeF : IEquatable<SizeF>
{
    public float Width { get; }
    public float Height { get; }
    public SizeF(float width, float height) { Width = width; Height = height; }
    public bool Equals(SizeF other) => Width == other.Width && Height == other.Height;
    public override bool Equals(object? obj) => obj is SizeF o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(Width, Height);
    public static bool operator ==(SizeF a, SizeF b) => a.Equals(b);
    public static bool operator !=(SizeF a, SizeF b) => !a.Equals(b);
}

/// <summary>An axis-aligned rectangle.</summary>
public readonly struct RectF : IEquatable<RectF>
{
    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }

    public RectF(float x, float y, float width, float height)
    {
        X = x; Y = y; Width = width; Height = height;
    }

    public float Left => X;
    public float Top => Y;
    public float Right => X + Width;
    public float Bottom => Y + Height;

    public bool Contains(PointF point)
        => point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;

    public bool Intersects(RectF other)
        => Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

    public bool Equals(RectF other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    public override bool Equals(object? obj) => obj is RectF o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    public static bool operator ==(RectF a, RectF b) => a.Equals(b);
    public static bool operator !=(RectF a, RectF b) => !a.Equals(b);
}

/// <summary>Edge sizes for margin, padding, or border (top, right, bottom, left).</summary>
public readonly struct EdgeSizes : IEquatable<EdgeSizes>
{
    public float Top { get; }
    public float Right { get; }
    public float Bottom { get; }
    public float Left { get; }

    public EdgeSizes(float top, float right, float bottom, float left)
    {
        Top = top; Right = right; Bottom = bottom; Left = left;
    }

    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;

    public static readonly EdgeSizes Zero = new(0, 0, 0, 0);
    public static EdgeSizes Uniform(float value) => new(value, value, value, value);

    public bool Equals(EdgeSizes other) => Top == other.Top && Right == other.Right && Bottom == other.Bottom && Left == other.Left;
    public override bool Equals(object? obj) => obj is EdgeSizes o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(Top, Right, Bottom, Left);
    public static bool operator ==(EdgeSizes a, EdgeSizes b) => a.Equals(b);
    public static bool operator !=(EdgeSizes a, EdgeSizes b) => !a.Equals(b);
}

/// <summary>Standard page sizes in CSS pixels (96 DPI).</summary>
public static class PageSizes
{
    public static readonly SizeF A3 = new(1122.52f, 1587.40f);
    public static readonly SizeF A4 = new(595.28f, 841.89f);
    public static readonly SizeF A5 = new(419.53f, 595.28f);
    public static readonly SizeF Letter = new(612f, 792f);
    public static readonly SizeF Legal = new(612f, 1008f);
    public static readonly SizeF Tabloid = new(792f, 1224f);

    public static SizeF Landscape(SizeF portrait) => new(portrait.Height, portrait.Width);
}

/// <summary>CSS pixel to PDF point coordinate conversion.</summary>
public static class PdfCoordinates
{
    public const float PxToPt = 72f / 96f;
    public const float PtToPx = 96f / 72f;

    public static float ToPdfLength(float cssPx) => cssPx * PxToPt;
    public static float ToPdfX(float cssPx) => cssPx * PxToPt;
    public static float ToPdfY(float cssPx, float pageHeightPx) => (pageHeightPx - cssPx) * PxToPt;
}
