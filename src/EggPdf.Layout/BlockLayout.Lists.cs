using System;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private static string GetListMarkerText(string listStyleType, HtmlElement element,
        HtmlElement? parentElement, CssCounterContext? counterCtx = null)
    {
        switch (listStyleType)
        {
            case "disc":
                return "\u2022"; // • (bullet, WinAnsi 0x95)
            case "circle":
                return "o"; // circle marker (WinAnsi-safe)
            case "square":
                return "\u2013"; // – as square substitute (WinAnsi 0x96)
            case "none":
                return "";
            case "decimal":
                int index = GetListItemIndex(element, parentElement);
                return index + ".";
            case "decimal-leading-zero":
                int idx = GetListItemIndex(element, parentElement);
                return idx.ToString("D2") + ".";
            case "lower-alpha":
            case "lower-latin":
                int ai = GetListItemIndex(element, parentElement);
                return ai > 0 && ai <= 26 ? ((char)('a' + ai - 1)).ToString() + "." : ai + ".";
            case "upper-alpha":
            case "upper-latin":
                int bi = GetListItemIndex(element, parentElement);
                return bi > 0 && bi <= 26 ? ((char)('A' + bi - 1)).ToString() + "." : bi + ".";
            case "lower-roman":
                int ri = GetListItemIndex(element, parentElement);
                return ToRoman(ri).ToLowerInvariant() + ".";
            case "upper-roman":
                int ui = GetListItemIndex(element, parentElement);
                return ToRoman(ui) + ".";
            default:
                // Check custom @counter-style
                if (counterCtx != null)
                {
                    int itemIdx = GetListItemIndex(element, parentElement);
                    var custom = counterCtx.FormatCustomStyle(listStyleType, itemIdx);
                    if (custom != null) return custom;
                }
                return "\u2022"; // default to disc
        }
    }

    private static int GetListItemIndex(HtmlElement li, HtmlElement? parent)
    {
        if (parent == null) return 1;
        int index = 0;
        foreach (var child in parent.ChildNodes)
        {
            if (child is HtmlElement e && e.TagName == "li")
            {
                index++;
                if (e == li) return index;
            }
        }
        return 1;
    }

    private static string ToRoman(int number)
    {
        if (number <= 0 || number > 3999) return number.ToString();
        string[] thousands = { "", "M", "MM", "MMM" };
        string[] hundreds = { "", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM" };
        string[] tens = { "", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC" };
        string[] ones = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
        return thousands[number / 1000] + hundreds[(number % 1000) / 100] +
               tens[(number % 100) / 10] + ones[number % 10];
    }

}
