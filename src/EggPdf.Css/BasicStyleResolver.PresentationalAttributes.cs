using System.Globalization;
using EggPdf.Html.Dom;

namespace EggPdf.Css;

public partial class BasicStyleResolver
{
    private const string TableCellBorderWidth = "1px";

    private static void ApplyPresentationalAttributes(HtmlElement element, ComputedStyle style)
    {
        var tag = element.TagName;
        bool isTableOrCell = tag == "table" || tag == "td" || tag == "th";

        ApplyDimensionAttribute(element, "width", style, rejectZero: isTableOrCell);
        ApplyDimensionAttribute(element, "height", style, rejectZero: isTableOrCell);

        var bgcolor = element.GetAttribute("bgcolor");
        if (!string.IsNullOrEmpty(bgcolor) && !style.Has("background-color"))
            style.Set("background-color", bgcolor);

        var align = element.GetAttribute("align");
        if (!string.IsNullOrEmpty(align))
            ApplyAlignAttribute(tag, align.Trim().ToLowerInvariant(), style);

        var valign = element.GetAttribute("valign");
        if (!string.IsNullOrEmpty(valign) && !style.Has("vertical-align"))
            style.Set("vertical-align", valign.ToLowerInvariant());

        if (tag == "table")
            ApplyTableAttributes(element, style);
        else if (tag == "td" || tag == "th")
            ApplyCellAttributesFromTable(element, style);

        var colorAttr = element.GetAttribute("color");
        if (!string.IsNullOrEmpty(colorAttr) && !style.Has("color"))
            style.Set("color", colorAttr);

        var face = element.GetAttribute("face");
        if (!string.IsNullOrEmpty(face) && !style.Has("font-family"))
            style.Set("font-family", face);
    }

    private static void ApplyDimensionAttribute(HtmlElement element, string name, ComputedStyle style, bool rejectZero)
    {
        var value = element.GetAttribute(name);
        if (string.IsNullOrEmpty(value) || style.Has(name)) return;
        var css = element.TagName == "svg" ? ParseSvgLengthAttribute(value!) : ParseHtmlDimension(value!, rejectZero);
        if (css != null)
            style.Set(name, css);
    }

    internal static string? ParseHtmlDimension(string value, bool rejectZero)
    {
        int start = SkipHtmlWhitespace(value, 0);
        int end = ScanDecimalNumber(value, start);
        if (end == start) return null;

        var number = value.Substring(start, end - start);
        if (rejectZero && IsZeroNumber(number)) return null;
        bool percent = end < value.Length && value[end] == '%';
        return number + (percent ? "%" : "px");
    }

    private static string? ParseSvgLengthAttribute(string value)
    {
        var trimmed = value.Trim();
        int end = ScanDecimalNumber(trimmed, 0);
        if (end == 0) return null;
        if (end == trimmed.Length) return trimmed + "px";
        if (end == trimmed.Length - 1 && trimmed[end] == '%') return trimmed;
        for (int i = end; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z'))
                return null;
        }
        return trimmed;
    }

    internal static bool TryParseHtmlNonNegativeInteger(string? value, out int result)
    {
        result = 0;
        if (value == null) return false;
        int i = SkipHtmlWhitespace(value, 0);
        if (i < value.Length && value[i] == '+') i++;
        int digitsStart = i;
        long accumulated = 0;
        while (i < value.Length && value[i] >= '0' && value[i] <= '9')
        {
            if (accumulated < int.MaxValue)
                accumulated = accumulated * 10 + (value[i] - '0');
            i++;
        }
        if (i == digitsStart) return false;
        result = accumulated > int.MaxValue ? int.MaxValue : (int)accumulated;
        return true;
    }

    private static int SkipHtmlWhitespace(string value, int index)
    {
        while (index < value.Length && (value[index] == ' ' || value[index] == '\t' || value[index] == '\n' || value[index] == '\r' || value[index] == '\f'))
            index++;
        return index;
    }

    private static int ScanDecimalNumber(string value, int start)
    {
        int i = start;
        while (i < value.Length && value[i] >= '0' && value[i] <= '9') i++;
        if (i == start) return start;
        if (i + 1 < value.Length && value[i] == '.' && value[i + 1] >= '0' && value[i + 1] <= '9')
        {
            i++;
            while (i < value.Length && value[i] >= '0' && value[i] <= '9') i++;
        }
        return i;
    }

    private static bool IsZeroNumber(string number)
    {
        for (int i = 0; i < number.Length; i++)
            if (number[i] != '0' && number[i] != '.') return false;
        return true;
    }

    private static void ApplyAlignAttribute(string tag, string align, ComputedStyle style)
    {
        switch (tag)
        {
            case "caption":
                if (align == "top" || align == "bottom")
                    style.Set("caption-side", align);
                else
                    style.Set("text-align", align);
                return;
            case "table":
                ApplyTableAlign(align, style);
                return;
            case "img":
            case "object":
            case "embed":
            case "iframe":
            case "input":
                ApplyReplacedElementAlign(align, style);
                return;
            default:
                style.Set("text-align", align == "middle" ? "center" : align);
                return;
        }
    }

    private static void ApplyTableAlign(string align, ComputedStyle style)
    {
        if (align == "left" || align == "right")
        {
            style.Set("float", align);
        }
        else if (align == "center")
        {
            style.Set("margin-left", "auto");
            style.Set("margin-right", "auto");
        }
    }

    private static void ApplyReplacedElementAlign(string align, ComputedStyle style)
    {
        switch (align)
        {
            case "left":
            case "right":
                style.Set("float", align);
                return;
            case "top":
                style.Set("vertical-align", "top");
                return;
            case "middle":
            case "absmiddle":
            case "center":
                style.Set("vertical-align", "middle");
                return;
            case "texttop":
                style.Set("vertical-align", "text-top");
                return;
            case "bottom":
            case "baseline":
            case "absbottom":
                style.Set("vertical-align", "baseline");
                return;
        }
    }

    private static void ApplyTableAttributes(HtmlElement table, ComputedStyle style)
    {
        if (TryParseHtmlNonNegativeInteger(table.GetAttribute("cellspacing"), out int spacing))
            style.Set("border-spacing", spacing.ToString(CultureInfo.InvariantCulture) + "px");

        int border = TableBorderWidth(table);
        if (border <= 0 || style.Has("border-top-width")) return;
        SetAllBorderSides(style, border.ToString(CultureInfo.InvariantCulture) + "px");
    }

    private static void ApplyCellAttributesFromTable(HtmlElement cell, ComputedStyle style)
    {
        var table = FindAncestorTable(cell);
        if (table == null) return;

        if (TryParseHtmlNonNegativeInteger(table.GetAttribute("cellpadding"), out int padding))
        {
            var px = padding.ToString(CultureInfo.InvariantCulture) + "px";
            style.Set("padding-top", px);
            style.Set("padding-right", px);
            style.Set("padding-bottom", px);
            style.Set("padding-left", px);
        }

        if (TableBorderWidth(table) > 0 && !style.Has("border-top-width"))
            SetAllBorderSides(style, TableCellBorderWidth);
    }

    private static int TableBorderWidth(HtmlElement table)
    {
        if (!table.HasAttribute("border")) return 0;
        return TryParseHtmlNonNegativeInteger(table.GetAttribute("border"), out int width) ? width : 1;
    }

    private static void SetAllBorderSides(ComputedStyle style, string width)
    {
        style.Set("border-top-width", width);
        style.Set("border-right-width", width);
        style.Set("border-bottom-width", width);
        style.Set("border-left-width", width);
        style.Set("border-top-style", "solid");
        style.Set("border-right-style", "solid");
        style.Set("border-bottom-style", "solid");
        style.Set("border-left-style", "solid");
    }

    private static HtmlElement? FindAncestorTable(HtmlElement element)
    {
        var parent = element.Parent as HtmlElement;
        while (parent != null)
        {
            if (parent.TagName == "table")
                return parent;
            parent = parent.Parent as HtmlElement;
        }
        return null;
    }
}
