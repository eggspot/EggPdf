using System;

namespace EggPdf.Pdf;

internal readonly struct CornerRadii
{
    public CornerRadii(float topLeftX, float topLeftY, float topRightX, float topRightY,
        float bottomRightX, float bottomRightY, float bottomLeftX, float bottomLeftY)
    {
        TopLeftX = topLeftX;
        TopLeftY = topLeftY;
        TopRightX = topRightX;
        TopRightY = topRightY;
        BottomRightX = bottomRightX;
        BottomRightY = bottomRightY;
        BottomLeftX = bottomLeftX;
        BottomLeftY = bottomLeftY;
    }

    public float TopLeftX { get; }
    public float TopLeftY { get; }
    public float TopRightX { get; }
    public float TopRightY { get; }
    public float BottomRightX { get; }
    public float BottomRightY { get; }
    public float BottomLeftX { get; }
    public float BottomLeftY { get; }

    public static CornerRadii Circular(float topLeft, float topRight, float bottomRight, float bottomLeft) =>
        new CornerRadii(topLeft, topLeft, topRight, topRight, bottomRight, bottomRight, bottomLeft, bottomLeft);

    public bool IsSquare =>
        !IsRounded(TopLeftX, TopLeftY) && !IsRounded(TopRightX, TopRightY) &&
        !IsRounded(BottomRightX, BottomRightY) && !IsRounded(BottomLeftX, BottomLeftY);

    public CornerRadii Scaled(float factor) =>
        new CornerRadii(TopLeftX * factor, TopLeftY * factor, TopRightX * factor, TopRightY * factor,
            BottomRightX * factor, BottomRightY * factor, BottomLeftX * factor, BottomLeftY * factor);

    public CornerRadii Expanded(float amount)
    {
        var (tlX, tlY) = ExpandCorner(TopLeftX, TopLeftY, amount);
        var (trX, trY) = ExpandCorner(TopRightX, TopRightY, amount);
        var (brX, brY) = ExpandCorner(BottomRightX, BottomRightY, amount);
        var (blX, blY) = ExpandCorner(BottomLeftX, BottomLeftY, amount);
        return new CornerRadii(tlX, tlY, trX, trY, brX, brY, blX, blY);
    }

    public CornerRadii FittedTo(float width, float height)
    {
        float factor = 1f;
        factor = OverlapFactor(factor, width, CornerX(TopLeftX, TopLeftY) + CornerX(TopRightX, TopRightY));
        factor = OverlapFactor(factor, width, CornerX(BottomLeftX, BottomLeftY) + CornerX(BottomRightX, BottomRightY));
        factor = OverlapFactor(factor, height, CornerX(TopLeftY, TopLeftX) + CornerX(BottomLeftY, BottomLeftX));
        factor = OverlapFactor(factor, height, CornerX(TopRightY, TopRightX) + CornerX(BottomRightY, BottomRightX));
        return factor < 1f ? Scaled(factor) : this;
    }

    public static bool IsRounded(float x, float y) => x > 0f && y > 0f;

    private static float CornerX(float x, float y) => IsRounded(x, y) ? x : 0f;

    private static float OverlapFactor(float factor, float sideLength, float radiiOnSide) =>
        radiiOnSide > sideLength && radiiOnSide > 0f ? Math.Min(factor, Math.Max(0f, sideLength) / radiiOnSide) : factor;

    private static (float X, float Y) ExpandCorner(float x, float y, float amount) =>
        IsRounded(x, y) ? (Math.Max(0f, x + amount), Math.Max(0f, y + amount)) : (0f, 0f);
}
