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
            var child = box.Children[i];
            if (!IsPositionedAgainstPage(child, box))
                OffsetBoxY(child, deltaY);
        }
    }

    private static void ResolveAbsolutePositions(LayoutBox box, float offsetX, float offsetY)
    {
        foreach (var child in box.Children)
        {
            if (!IsPositionedAgainstPage(child, box) && box.Y > 0)
                child.Y += box.Y;

            ResolveAbsolutePositions(child, child.X, child.Y);
        }
    }

    private static bool IsPositionedAgainstPage(LayoutBox child, LayoutBox parent)
    {
        if (!child.IsAbsolutelyPositioned) return false;
        if (child.Style.Get("position") == "fixed") return true;
        return !EstablishesContainingBlockForAbsolute(parent.Style.Get("position"));
    }

    private static bool EstablishesContainingBlockForAbsolute(string? position) =>
        position == "relative" || position == "absolute" || position == "fixed" || position == "sticky";
}
