using System;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private static bool IsPaintedFormControl(HtmlElement element)
    {
        if (element.TagName == "progress" || element.TagName == "meter") return true;
        if (element.TagName != "input") return false;
        var type = element.GetAttribute("type");
        return string.Equals(type, "checkbox", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, "radio", StringComparison.OrdinalIgnoreCase);
    }

    internal static float CollapseMargins(float first, float second) =>
        Math.Max(0f, Math.Max(first, second)) + Math.Min(0f, Math.Min(first, second));

    private static bool CollapsesMarginsWithChildren(HtmlElement element, ComputedStyle style, ComputedStyle? parentStyle)
    {
        var tag = element.TagName;
        if (tag == "body" || tag == "html" || tag == "td" || tag == "th" || tag == "table" || tag == "caption") return false;

        var display = style.Get("display");
        if (!string.IsNullOrEmpty(display) && display != "block" && display != "list-item") return false;

        var position = style.Get("position");
        if (position == "absolute" || position == "fixed") return false;

        var floatValue = style.Get("float");
        if (!string.IsNullOrEmpty(floatValue) && floatValue != "none") return false;

        var overflow = style.Get("overflow");
        if (!string.IsNullOrEmpty(overflow) && overflow != "visible" && overflow != "clip") return false;

        var contain = style.Get("contain");
        if (!string.IsNullOrEmpty(contain) && contain != "none") return false;

        var parentDisplay = parentStyle?.Get("display");
        if (parentDisplay == "flex" || parentDisplay == "inline-flex" || parentDisplay == "grid" || parentDisplay == "inline-grid")
            return false;

        return true;
    }
}
