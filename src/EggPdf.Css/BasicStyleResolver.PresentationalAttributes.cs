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
            ApplyAlignAttribute(element, align.Trim().ToLowerInvariant(), style);

        var valign = element.GetAttribute("valign");
        if (!string.IsNullOrEmpty(valign) && !style.Has("vertical-align"))
            style.Set("vertical-align", valign.ToLowerInvariant());

        switch (tag)
        {
            case "table":
                ApplyTableAttributes(element, style);
                break;
            case "td":
            case "th":
                ApplyCellAttributesFromTable(element, style);
                if (element.HasAttribute("nowrap"))
                    style.Set("white-space", "nowrap");
                break;
            case "img":
                ApplyImageSpacingAttributes(element, style);
                break;
            case "ol":
            case "ul":
            case "li":
                ApplyListTypeAttribute(element.GetAttribute("type"), tag, style);
                break;
            case "font":
                ApplyFontSizeAttribute(element.GetAttribute("size"), style);
                break;
            case "body":
                ApplyBodyAttributes(element, style);
                break;
        }

        if (AcceptsBackgroundAttribute(tag))
        {
            var background = element.GetAttribute("background")?.Trim();
            if (!string.IsNullOrEmpty(background) && !style.Has("background-image"))
                style.Set("background-image", "url(" + CssStringEscapes.Quote(background!) + ")");
        }

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

        bool relative = end < value.Length && value[end] == '*';
        if (relative) return null;
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

    private static void ApplyAlignAttribute(HtmlElement element, string align, ComputedStyle style)
    {
        switch (element.TagName)
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
                ApplyReplacedElementAlign(align, style);
                return;
            case "input":
                if (string.Equals(element.GetAttribute("type")?.Trim(), "image", System.StringComparison.OrdinalIgnoreCase))
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
            case "abscenter":
            case "center":
                style.Set("vertical-align", "middle");
                return;
            case "texttop":
                style.Set("vertical-align", "text-top");
                return;
            case "bottom":
            case "baseline":
                style.Set("vertical-align", "baseline");
                return;
            case "absbottom":
                style.Set("vertical-align", "bottom");
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

    private static bool AcceptsBackgroundAttribute(string tag) =>
        tag == "body" || tag == "table" || tag == "thead" || tag == "tbody" || tag == "tfoot" ||
        tag == "tr" || tag == "td" || tag == "th";

    private static void ApplyImageSpacingAttributes(HtmlElement image, ComputedStyle style)
    {
        var hspace = image.GetAttribute("hspace");
        var horizontal = hspace == null ? null : ParseHtmlDimension(hspace, rejectZero: false);
        if (horizontal != null)
        {
            style.Set("margin-left", horizontal);
            style.Set("margin-right", horizontal);
        }

        var vspace = image.GetAttribute("vspace");
        var vertical = vspace == null ? null : ParseHtmlDimension(vspace, rejectZero: false);
        if (vertical != null)
        {
            style.Set("margin-top", vertical);
            style.Set("margin-bottom", vertical);
        }

        if (TryParseHtmlNonNegativeInteger(image.GetAttribute("border"), out int border) && !style.Has("border-top-width"))
        {
            if (border > 0)
                SetAllBorderSides(style, border.ToString(CultureInfo.InvariantCulture) + "px");
        }
    }

    private static void ApplyListTypeAttribute(string? type, string tag, ComputedStyle style)
    {
        if (string.IsNullOrEmpty(type)) return;
        var trimmed = type!.Trim();
        var listStyle = tag switch
        {
            "ol" => OrderedListStyleForType(trimmed),
            "ul" => UnorderedListStyleForType(trimmed),
            _ => OrderedListStyleForType(trimmed) ?? UnorderedListStyleForType(trimmed),
        };
        if (listStyle != null)
            style.Set("list-style-type", listStyle);
    }

    private static string? OrderedListStyleForType(string type)
    {
        switch (type)
        {
            case "1": return "decimal";
            case "a": return "lower-alpha";
            case "A": return "upper-alpha";
            case "i": return "lower-roman";
            case "I": return "upper-roman";
            default: return null;
        }
    }

    private static string? UnorderedListStyleForType(string type)
    {
        switch (type.ToLowerInvariant())
        {
            case "disc": return "disc";
            case "circle": return "circle";
            case "square": return "square";
            case "none": return "none";
            default: return null;
        }
    }

    private static readonly string[] LegacyFontSizeKeywords =
        { "x-small", "small", "medium", "large", "x-large", "xx-large", "xxx-large" };

    private static void ApplyFontSizeAttribute(string? size, ComputedStyle style)
    {
        if (size == null || style.Has("font-size")) return;
        int i = SkipHtmlWhitespace(size, 0);
        int sign = 0;
        if (i < size.Length && (size[i] == '+' || size[i] == '-'))
        {
            sign = size[i] == '+' ? 1 : -1;
            i++;
        }
        int digitsStart = i;
        int value = 0;
        while (i < size.Length && size[i] >= '0' && size[i] <= '9')
        {
            if (value < 100) value = value * 10 + (size[i] - '0');
            i++;
        }
        if (i == digitsStart) return;
        int level = sign == 0 ? value : 3 + sign * value;
        if (level < 1) level = 1;
        if (level > 7) level = 7;
        style.Set("font-size", LegacyFontSizeKeywords[level - 1]);
    }

    private static void ApplyBodyAttributes(HtmlElement body, ComputedStyle style)
    {
        var text = body.GetAttribute("text");
        if (!string.IsNullOrEmpty(text) && !style.Has("color"))
            style.Set("color", text);

        ApplyBodyMarginPair(body.GetAttribute("marginwidth") ?? body.GetAttribute("leftmargin"), "margin-left", "margin-right", style);
        ApplyBodyMarginPair(body.GetAttribute("marginheight") ?? body.GetAttribute("topmargin"), "margin-top", "margin-bottom", style);
    }

    private static void ApplyBodyMarginPair(string? value, string firstProperty, string secondProperty, ComputedStyle style)
    {
        var margin = value == null ? null : ParseHtmlDimension(value, rejectZero: false);
        if (margin == null) return;
        style.Set(firstProperty, margin);
        style.Set(secondProperty, margin);
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
