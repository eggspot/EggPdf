using System;
using EggPdf.Css;

namespace EggPdf.Layout;

internal static class AnonymousBoxStyle
{
    internal static ComputedStyle From(ComputedStyle containerStyle)
    {
        var style = new ComputedStyle();
        foreach (var kv in containerStyle.All)
        {
            if (!BelongsToTheContainerBox(kv.Key))
                style.Set(kv.Key, kv.Value);
        }
        return style;
    }

    private static readonly string[] ContainerBoxPropertyPrefixes =
    {
        "border", "background", "box-shadow", "outline", "padding", "margin", "width", "height", "min-", "max-",
        "display", "grid", "flex", "position", "top", "right", "bottom", "left", "inset", "transform", "opacity",
        "overflow", "box-sizing", "z-index", "float", "clear", "align-self", "justify-self", "order", "column", "gap",
        "row-gap", "aspect-ratio", "contain", "clip", "filter", "mix-blend-mode"
    };

    private static bool BelongsToTheContainerBox(string property)
    {
        foreach (var prefix in ContainerBoxPropertyPrefixes)
            if (property.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        return false;
    }
}
