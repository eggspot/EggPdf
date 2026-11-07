using System;
using System.Collections.Generic;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private static readonly string DiscMarker = ((char)0x2022).ToString();
    private const string CircleMarkerWinAnsiSafe = "o";
    private static readonly string SquareMarkerEnDashSubstitute = ((char)0x2013).ToString();
    private const int MaxRomanNumeral = 3999;

    private sealed class ListOrdinalCursor
    {
        public int ChildIndex = -1;
        public int Ordinal;
        public int Step = 1;
        public bool HonorsItemValues;
    }

    [ThreadStatic]
    private static Dictionary<HtmlElement, ListOrdinalCursor>? _listOrdinalCursors;

    private static string GetListMarkerText(string listStyleType, HtmlElement element,
        HtmlElement? parentElement, CssCounterContext? counterCtx = null)
    {
        switch (listStyleType)
        {
            case "disc":
                return DiscMarker;
            case "circle":
                return CircleMarkerWinAnsiSafe;
            case "square":
                return SquareMarkerEnDashSubstitute;
            case "none":
                return "";
        }

        int ordinal = GetListItemOrdinal(element, parentElement);
        switch (listStyleType)
        {
            case "decimal":
                return ToDecimal(ordinal) + ".";
            case "decimal-leading-zero":
                return ordinal.ToString("D2", CultureInfo.InvariantCulture) + ".";
            case "lower-alpha":
            case "lower-latin":
                return ToAlphabetic(ordinal, 'a') + ".";
            case "upper-alpha":
            case "upper-latin":
                return ToAlphabetic(ordinal, 'A') + ".";
            case "lower-roman":
                return ToRoman(ordinal).ToLowerInvariant() + ".";
            case "upper-roman":
                return ToRoman(ordinal) + ".";
            default:
                var custom = counterCtx?.FormatCustomStyle(listStyleType, ordinal);
                return custom ?? DiscMarker;
        }
    }

    private static int GetListItemOrdinal(HtmlElement li, HtmlElement? list)
    {
        if (list == null) return 1;
        var cursors = _listOrdinalCursors ??= new Dictionary<HtmlElement, ListOrdinalCursor>();
        if (!cursors.TryGetValue(list, out var cursor))
        {
            cursor = StartListOrdinals(list);
            cursors[list] = cursor;
        }
        if (AdvanceToListItem(list, li, cursor))
            return cursor.Ordinal;

        var restarted = StartListOrdinals(list);
        cursors[list] = restarted;
        return AdvanceToListItem(list, li, restarted) ? restarted.Ordinal : 1;
    }

    private static ListOrdinalCursor StartListOrdinals(HtmlElement list)
    {
        bool isOrderedList = list.TagName == "ol";
        bool reversed = isOrderedList && list.HasAttribute("reversed");
        int step = reversed ? -1 : 1;
        if (!isOrderedList || !TryParseHtmlInteger(list.GetAttribute("start"), out int start))
            start = reversed ? CountListItems(list) : 1;
        return new ListOrdinalCursor { Ordinal = start - step, Step = step, HonorsItemValues = isOrderedList };
    }

    private static bool AdvanceToListItem(HtmlElement list, HtmlElement li, ListOrdinalCursor cursor)
    {
        var children = list.ChildNodes;
        for (int i = cursor.ChildIndex + 1; i < children.Count; i++)
        {
            if (children[i] is HtmlElement item && item.TagName == "li")
            {
                cursor.Ordinal = cursor.HonorsItemValues && TryParseHtmlInteger(item.GetAttribute("value"), out int value)
                    ? value
                    : cursor.Ordinal + cursor.Step;
                cursor.ChildIndex = i;
                if (item == li) return true;
            }
        }
        return false;
    }

    private static int CountListItems(HtmlElement list)
    {
        int count = 0;
        foreach (var child in list.ChildNodes)
        {
            if (child is HtmlElement e && e.TagName == "li")
                count++;
        }
        return count;
    }

    private static bool TryParseHtmlInteger(string? value, out int result)
    {
        result = 0;
        return !string.IsNullOrEmpty(value) &&
               int.TryParse(value!.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);
    }

    private static string ToDecimal(int number) => number.ToString(CultureInfo.InvariantCulture);

    private static string ToAlphabetic(int number, char firstLetter)
    {
        if (number <= 0) return ToDecimal(number);
        var letters = new char[8];
        int pos = letters.Length;
        while (number > 0)
        {
            number--;
            letters[--pos] = (char)(firstLetter + number % 26);
            number /= 26;
        }
        return new string(letters, pos, letters.Length - pos);
    }

    private static string ToRoman(int number)
    {
        if (number <= 0 || number > MaxRomanNumeral) return ToDecimal(number);
        string[] thousands = { "", "M", "MM", "MMM" };
        string[] hundreds = { "", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM" };
        string[] tens = { "", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC" };
        string[] ones = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
        return thousands[number / 1000] + hundreds[(number % 1000) / 100] +
               tens[(number % 100) / 10] + ones[number % 10];
    }
}
