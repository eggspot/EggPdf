using System;
using System.Collections.Generic;

namespace EggPdf.Layout;

/// <summary>
/// Tracks active floats within a block formatting context (BFC).
/// Used during layout to position floated elements and adjust available
/// width for non-floated content that flows around them.
/// </summary>
internal class FloatContext
{
    private readonly List<FloatInfo> _leftFloats = new List<FloatInfo>();
    private readonly List<FloatInfo> _rightFloats = new List<FloatInfo>();

    /// <summary>
    /// Add a left float at the given position. <paramref name="shape"/> (from
    /// shape-outside: circle()/ellipse()/polygon()/inset()) narrows the exclusion to the shape's actual
    /// extent at each line instead of the float's full rectangular width.
    /// </summary>
    public void AddLeftFloat(float x, float y, float width, float height, ShapeOutsideDescriptor? shape = null)
    {
        _leftFloats.Add(new FloatInfo(x, y, width, height, shape));
    }

    /// <summary>
    /// Add a right float at the given position. See <paramref name="shape"/> above.
    /// </summary>
    public void AddRightFloat(float x, float y, float width, float height, ShapeOutsideDescriptor? shape = null)
    {
        _rightFloats.Add(new FloatInfo(x, y, width, height, shape));
    }

    /// <summary>
    /// Get the left offset at the given Y position (how far right content must start
    /// to avoid left floats). Returns absolute X coordinate.
    /// </summary>
    public float GetLeftOffset(float y, float lineHeight)
    {
        float offset = 0;
        for (int i = 0; i < _leftFloats.Count; i++)
        {
            var f = _leftFloats[i];
            if (f.Y < y + lineHeight && f.Y + f.Height > y)
            {
                float right = f.X + f.Width;
                if (f.Shape.HasValue)
                {
                    // The line box clamped to the float's own height, in float-local Y
                    float top = Math.Max(y, f.Y) - f.Y;
                    float bottom = Math.Min(y + lineHeight, f.Y + f.Height) - f.Y;
                    var edge = f.Shape.Value.RightEdgeInRange(top, bottom);
                    if (!edge.HasValue) continue; // the shape doesn't reach this line
                    right = f.X + edge.Value;
                }
                if (right > offset)
                    offset = right;
            }
        }
        return offset;
    }

    /// <summary>
    /// Get the right offset at the given Y position.
    /// Returns the total width consumed from the right edge by right floats.
    /// </summary>
    public float GetRightOffset(float y, float lineHeight, float containerRight)
    {
        float offset = 0;
        for (int i = 0; i < _rightFloats.Count; i++)
        {
            var f = _rightFloats[i];
            if (f.Y < y + lineHeight && f.Y + f.Height > y)
            {
                float leftEdge = 0f;
                if (f.Shape.HasValue)
                {
                    float top = Math.Max(y, f.Y) - f.Y;
                    float bottom = Math.Min(y + lineHeight, f.Y + f.Height) - f.Y;
                    var edge = f.Shape.Value.LeftEdgeInRange(top, bottom);
                    if (!edge.HasValue) continue; // the shape doesn't reach this line
                    leftEdge = edge.Value;
                }
                float consumed = containerRight - (f.X + leftEdge);
                if (consumed > offset)
                    offset = consumed;
            }
        }
        return offset;
    }

    /// <summary>
    /// Get the available width for content at the given Y position, accounting
    /// for both left and right floats.
    /// </summary>
    public float GetAvailableWidth(float y, float lineHeight, float containerWidth, float containerX)
    {
        float leftOff = GetLeftOffset(y, lineHeight);
        float rightOff = GetRightOffset(y, lineHeight, containerX + containerWidth);

        float relativeLeft = leftOff > containerX ? leftOff - containerX : 0;
        float available = containerWidth - relativeLeft - rightOff;
        return available > 0 ? available : 0;
    }

    /// <summary>
    /// Get the X position where left-aligned content should start at the given Y position.
    /// </summary>
    public float GetContentStartX(float y, float lineHeight, float containerX)
    {
        float leftOff = GetLeftOffset(y, lineHeight);
        return leftOff > containerX ? leftOff : containerX;
    }

    /// <summary>Get Y position below all left floats (for clear: left).</summary>
    public float GetClearLeftY()
    {
        float maxBottom = 0;
        for (int i = 0; i < _leftFloats.Count; i++)
        {
            float bottom = _leftFloats[i].Y + _leftFloats[i].Height;
            if (bottom > maxBottom)
                maxBottom = bottom;
        }
        return maxBottom;
    }

    /// <summary>Get Y position below all right floats (for clear: right).</summary>
    public float GetClearRightY()
    {
        float maxBottom = 0;
        for (int i = 0; i < _rightFloats.Count; i++)
        {
            float bottom = _rightFloats[i].Y + _rightFloats[i].Height;
            if (bottom > maxBottom)
                maxBottom = bottom;
        }
        return maxBottom;
    }

    /// <summary>Get Y position below all floats (for clear: both).</summary>
    public float GetClearBothY()
    {
        return Math.Max(GetClearLeftY(), GetClearRightY());
    }

    /// <summary>Get the clear Y based on the clear property value.</summary>
    public float GetClearY(string clear)
    {
        switch (clear)
        {
            case "left": return GetClearLeftY();
            case "right": return GetClearRightY();
            case "both": return GetClearBothY();
            default: return 0;
        }
    }

    /// <summary>
    /// Find the X position for a new left float, accounting for existing left floats.
    /// </summary>
    public float FindLeftFloatX(float startY, float containerX)
    {
        float x = containerX;
        for (int i = 0; i < _leftFloats.Count; i++)
        {
            var f = _leftFloats[i];
            if (f.Y < startY + 1 && f.Y + f.Height > startY)
            {
                float right = f.X + f.Width;
                if (right > x)
                    x = right;
            }
        }
        return x;
    }

    /// <summary>
    /// Find the X position for a new right float, accounting for existing right floats.
    /// </summary>
    public float FindRightFloatX(float startY, float totalFloatWidth, float containerRight)
    {
        float x = containerRight - totalFloatWidth;
        for (int i = 0; i < _rightFloats.Count; i++)
        {
            var f = _rightFloats[i];
            if (f.Y < startY + 1 && f.Y + f.Height > startY)
            {
                if (f.X < x + totalFloatWidth)
                    x = f.X - totalFloatWidth;
            }
        }
        return x;
    }

    /// <summary>Whether there are any active floats.</summary>
    public bool HasFloats
    {
        get { return _leftFloats.Count > 0 || _rightFloats.Count > 0; }
    }

    /// <summary>Get the maximum bottom of all floats (for BFC containment).</summary>
    public float GetMaxFloatBottom()
    {
        return GetClearBothY();
    }

    /// <summary>Describes the geometry of a single float.</summary>
    private struct FloatInfo
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Width;
        public readonly float Height;
        public readonly ShapeOutsideDescriptor? Shape;

        public FloatInfo(float x, float y, float width, float height, ShapeOutsideDescriptor? shape = null)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Shape = shape;
        }
    }
}
