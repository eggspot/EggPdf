using System;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    /// <summary>
    /// Inject synthetic text/content for form elements that don't have normal child text nodes.
    /// Returns updated childY after any injected content.
    /// </summary>
    private static float InjectFormElementContent(HtmlElement element, ComputedStyle style,
        LayoutBox box, float fontSize, float contentWidth, float childY)
    {
        var tag = element.TagName;

        if (tag == "input")
        {
            var inputType = (element.GetAttribute("type") ?? "text").ToLowerInvariant();
            string? text = null;

            if (inputType == "checkbox" || inputType == "radio")
            {
                // appearance: none / -webkit-appearance: none — suppress native glyph
                var appearanceVal = style.Get("appearance") ?? style.Get("-webkit-appearance");
                bool suppressGlyph = appearanceVal == "none";
                if (!suppressGlyph)
                {
                    bool isChecked = element.HasAttribute("checked");
                    text = inputType == "checkbox"
                        ? (isChecked ? "\u2611" : "\u2610")   // ☑ / ☐
                        : (isChecked ? "\u25c9" : "\u25cb");  // ◉ / ○
                }
            }
            else if (inputType == "submit" || inputType == "button" || inputType == "reset")
            {
                text = element.GetAttribute("value") ?? inputType;
            }
            else if (inputType != "hidden" && inputType != "file" && inputType != "image")
            {
                // text, password, email, number, tel, url, search, date, etc.
                var value = element.GetAttribute("value");
                if (string.IsNullOrEmpty(value))
                {
                    // Show placeholder text when no value is set
                    var placeholder = element.GetAttribute("placeholder");
                    if (!string.IsNullOrEmpty(placeholder))
                    {
                        text = placeholder;
                        // Build a placeholder style: inherit from input but override color to gray
                        var phStyle = new ComputedStyle();
                        foreach (var kv in style.All)
                            phStyle.Set(kv.Key, kv.Value);
                        phStyle.Set("color", "#9e9e9e"); // UA default placeholder gray
                        phStyle.Set("font-style", "italic");
                        float lh = TextMeasurer.GetLineHeight(fontSize, style.Get("line-height"));
                        float tw = TextMeasurer.MeasureWidth(text, fontSize, style.FontFamily, style.FontWeight, "italic");
                        var phBox = new LayoutBox
                        {
                            Style = phStyle,
                            X = box.X + box.PaddingLeft,
                            Y = box.Y + box.PaddingTop + childY,
                            Width = contentWidth,
                            Height = lh,
                            ContentWidth = tw,
                            ContentHeight = lh,
                            Text = text
                        };
                        box.Children.Add(phBox);
                        childY += lh;
                        text = null; // handled
                    }
                    else
                    {
                        text = "";
                    }
                }
                else
                {
                    text = value;
                }
            }

            if (text != null)
            {
                float lh = TextMeasurer.GetLineHeight(fontSize, style.Get("line-height"));
                float tw = text.Length > 0 ? TextMeasurer.MeasureWidth(text, fontSize, style.FontFamily, style.FontWeight, style.Get("font-style")) : 0;
                var textBox = new LayoutBox
                {
                    Style = style,
                    X = box.X + box.PaddingLeft,
                    Y = box.Y + box.PaddingTop + childY,
                    Width = contentWidth,
                    Height = lh,
                    ContentWidth = tw,
                    ContentHeight = lh,
                    Text = text
                };
                box.Children.Add(textBox);
                childY += lh;
            }
        }
        else if (tag == "select")
        {
            // Find selected option text
            string? optionText = null;
            foreach (var child in element.ChildNodes)
            {
                if (child is HtmlElement optElem)
                {
                    HtmlElement? opt = null;
                    if (optElem.TagName == "option")
                        opt = optElem;
                    else if (optElem.TagName == "optgroup")
                    {
                        // Find first selected within optgroup
                        foreach (var og in optElem.ChildNodes)
                            if (og is HtmlElement o && o.TagName == "option" && o.HasAttribute("selected"))
                            { opt = o; break; }
                        if (opt == null)
                            foreach (var og in optElem.ChildNodes)
                                if (og is HtmlElement o && o.TagName == "option")
                                { opt = o; break; }
                    }

                    if (opt != null && opt.HasAttribute("selected"))
                    {
                        optionText = GetOptionText(opt);
                        break;
                    }
                    if (opt != null && optionText == null)
                        optionText = GetOptionText(opt); // fallback to first
                }
            }

            if (!string.IsNullOrEmpty(optionText))
            {
                float lh = TextMeasurer.GetLineHeight(fontSize, style.Get("line-height"));
                float tw = TextMeasurer.MeasureWidth(optionText, fontSize, style.FontFamily, style.FontWeight, style.Get("font-style"));
                var textBox = new LayoutBox
                {
                    Style = style,
                    X = box.X + box.PaddingLeft,
                    Y = box.Y + box.PaddingTop + childY,
                    Width = contentWidth,
                    Height = lh,
                    ContentWidth = tw,
                    ContentHeight = lh,
                    Text = optionText
                };
                box.Children.Add(textBox);
                childY += lh;
            }
        }

        return childY;
    }

    private static string GetOptionText(HtmlElement element)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var node in element.ChildNodes)
        {
            if (node is Html.Dom.HtmlTextNode t)
                sb.Append(t.Data);
            else if (node is HtmlElement child)
                sb.Append(GetOptionText(child));
        }
        return sb.ToString().Trim();
    }
}
