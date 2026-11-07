using System;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private static void OffsetBoxY(LayoutBox box, float deltaY)
    {
        box.Y += deltaY;
        for (int i = 0; i < box.Children.Count; i++)
        {
            if (!box.Children[i].IsAbsolutelyPositioned)
                OffsetBoxY(box.Children[i], deltaY);
        }
    }

    private static void ResolveAbsolutePositions(LayoutBox box, float offsetX, float offsetY)
    {
        foreach (var child in box.Children)
        {
            if (child.IsAbsolutelyPositioned)
            {
                ResolveAbsolutePositions(child, child.X, child.Y);
                continue;
            }

            if (box.Y > 0)
                child.Y += box.Y;

            ResolveAbsolutePositions(child, child.X, child.Y);
        }
    }

}
