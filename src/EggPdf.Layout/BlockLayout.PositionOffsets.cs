using System;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    /// <summary>Offset a box and all its descendants by deltaY (skip absolutely positioned).</summary>
    private static void OffsetBoxY(LayoutBox box, float deltaY)
    {
        box.Y += deltaY;
        for (int i = 0; i < box.Children.Count; i++)
        {
            if (!box.Children[i].IsAbsolutelyPositioned)
                OffsetBoxY(box.Children[i], deltaY);
        }
    }

    /// <summary>
    /// Post-layout pass: resolve children with relative Y coordinates to absolute.
    /// Children created by CreateBox have Y relative to parent's content area,
    /// but the parent's final Y is set after CreateBox returns.
    /// </summary>
    private static void ResolveAbsolutePositions(LayoutBox box, float offsetX, float offsetY)
    {
        foreach (var child in box.Children)
        {
            if (child.IsAbsolutelyPositioned)
            {
                ResolveAbsolutePositions(child, child.X, child.Y);
                continue;
            }

            // Children are created with Y = parent.PaddingTop + childY (relative, since parent.Y was 0
            // during CreateBox). Always add parent's resolved Y to convert to absolute coordinates.
            // List markers also need this adjustment since they're created with relative Y.
            if (box.Y > 0)
                child.Y += box.Y;

            // X positions are set absolutely during CreateBox (parent.X + padding + margin),
            // so no post-fixup is needed for X coordinates.

            ResolveAbsolutePositions(child, child.X, child.Y);
        }
    }

}
