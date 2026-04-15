using EggPdf.Css.Cascade;
using EggPdf.Css.Parser;
using EggPdf.Html;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>
/// Integration tests reproducing the Invoice Pro template layout.
/// Uses CascadeResolver (not BasicStyleResolver) to verify real rendering path.
/// A4 page width = 793.70px at 96dpi. No @page margins in this template.
/// </summary>
public class InvoiceLayoutTests
{
    // A4 at 96dpi (CSS pixels): 210mm / 25.4 * 96
    private const float A4Width = 793.7f;
    private const float A4Height = 1122.52f;

    private static LayoutBox LayoutWithCascade(string html, float pageWidth = A4Width, float pageHeight = A4Height)
    {
        var document = HtmlParser.Parse(html);
        var sheets = new System.Collections.Generic.List<CssStyleSheet>();

        // Extract <style> tags from <head>
        if (document.Head != null)
        {
            foreach (var node in document.Head.ChildNodes)
            {
                if (node is EggPdf.Html.Dom.HtmlElement elem && elem.TagName == "style")
                {
                    var text = GetInnerText(elem);
                    if (!string.IsNullOrWhiteSpace(text))
                        sheets.Add(CssStyleSheetParser.Parse(text));
                }
            }
        }

        var cascadeResolver = new CascadeResolver(sheets, "print");
        return BlockLayout.LayoutDocument(document, pageWidth, pageHeight, cascadeResolver);
    }

    private static string GetInnerText(EggPdf.Html.Dom.HtmlElement elem)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var n in elem.ChildNodes)
            if (n is EggPdf.Html.Dom.HtmlTextNode t) sb.Append(t.Data);
        return sb.ToString();
    }

    [Fact]
    public void InvoiceBody_ContentWidth_Is_PageWidth_Minus_Padding()
    {
        // Body has padding: 40px and margin: 0 auto on A4 page
        var root = LayoutWithCascade(InvoiceHtml);

        var body = root.FindByTag("body");
        body.Should().NotBeNull();

        // Padding 40px each side: body.ContentWidth = A4Width - 40 - 40 = 713.7
        body!.ContentWidth.Should().BeApproximately(A4Width - 80f, 2f,
            "body has padding: 40px so ContentWidth = A4Width - 80");
    }

    [Fact]
    public void InvoiceHeader_RightFlexItem_StaysWithinPage()
    {
        var root = LayoutWithCascade(InvoiceHtml);

        // The header has display:flex; justify-content:space-between
        // Right item is the address div (second child of the header flex container)
        var body = root.FindByTag("body");
        body.Should().NotBeNull();

        // Find the header flex container (first div in body)
        var headerFlex = body!.Children.Count > 0 ? body.Children[0] : null;
        headerFlex.Should().NotBeNull("header flex container should be first child of body");

        // Right item: second child of header flex container
        headerFlex!.Children.Count.Should().BeGreaterOrEqualTo(2,
            "header flex row should have 2 flex items");

        var rightItem = headerFlex.Children[1];
        float rightEdge = rightItem.X + rightItem.Width;

        rightEdge.Should().BeLessThanOrEqualTo(A4Width + 1f,
            $"right flex item should not overflow page (X={rightItem.X}, Width={rightItem.Width})");
        rightEdge.Should().BeLessThanOrEqualTo(A4Width - 38f,
            $"right flex item should have at least body padding clearance from right page edge (X={rightItem.X}, Width={rightItem.Width})");
    }

    [Fact]
    public void InvoiceHeader_RightFlexItem_TextBoxesWithinPage()
    {
        var root = LayoutWithCascade(InvoiceHtml);

        // All text boxes (including Acme Corporation LLC) must stay within page width
        var allBoxes = new System.Collections.Generic.List<LayoutBox>();
        CollectAll(root, allBoxes);

        foreach (var box in allBoxes)
        {
            if (box.Text == null) continue;
            float rightEdge = box.X + box.Width;
            rightEdge.Should().BeLessThanOrEqualTo(A4Width + 1f,
                $"text box '{box.Text}' at X={box.X} Width={box.Width} overflows page");
        }
    }

    [Fact]
    public void InvoiceFourItemFlex_RightItem_AmountDue_WithinPage()
    {
        var root = LayoutWithCascade(InvoiceHtml);

        // Find the 4-item flex row (second flex div in body)
        // The right-most item has "flex: 1; text-align: right" and contains "Amount Due (USD)"
        var body = root.FindByTag("body");
        body.Should().NotBeNull();

        // There should be multiple flex containers in body
        // Find text box containing "Amount Due"
        var allBoxes = new System.Collections.Generic.List<LayoutBox>();
        CollectAll(root, allBoxes);

        LayoutBox? amountDueBox = null;
        foreach (var box in allBoxes)
        {
            if (box.Text != null && box.Text.IndexOf("Amount Due", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                amountDueBox = box;
                break;
            }
        }

        amountDueBox.Should().NotBeNull("should find 'Amount Due (USD)' text box");

        float rightEdge = amountDueBox!.X + amountDueBox.Width;
        rightEdge.Should().BeLessThanOrEqualTo(A4Width + 1f,
            $"Amount Due text at X={amountDueBox.X} Width={amountDueBox.Width} overflows page");
        rightEdge.Should().BeLessThanOrEqualTo(A4Width - 38f,
            $"Amount Due text should be within body padding clearance (X={amountDueBox.X} Width={amountDueBox.Width})");
    }

    private static void CollectAll(LayoutBox box, System.Collections.Generic.List<LayoutBox> result)
    {
        result.Add(box);
        foreach (var child in box.Children)
            CollectAll(child, result);
    }

    // The Invoice Pro template HTML (simplified to relevant parts)
    private const string InvoiceHtml = @"<!DOCTYPE html>
<html lang=""en""><head><meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Invoice #INV-2026-00001</title>
    <style>@media print {
            body { margin: 0; padding: 10px 20px; }
            @page { margin: 5mm; }
        }
    </style></head>

<body style=""font-family: 'Segoe UI', Arial, sans-serif; max-width: 800px; margin: 0 auto; padding: 40px; color: #333333; font-size: 14px; line-height: 1.5; background-color: #ffffff;"">

<div style=""display: flex; justify-content: space-between; margin-bottom: 60px;"">
    <div style=""font-size: 32px; font-weight: bold; color: #f97316; letter-spacing: 3px;"">ACME CORP</div>
    <div style=""text-align: left;"">
        <p style=""margin: 0; font-weight: bold;"">Acme Corporation LLC</p>
        <p style=""margin: 0;"">100 Tech Boulevard</p>
        <p style=""margin: 0;"">Austin, Texas</p>
        <p style=""margin: 0;"">United States</p>
        <p style=""margin: 0;"">78701</p>
        <p style=""margin: 0;"">billing@acmecorp.example</p>
    </div>
</div>

<div style=""display: flex; justify-content: space-between; margin-bottom: 40px;"">
    <div style=""flex: 2;"">
        <p style=""margin: 0 0 5px 0; color: #f97316; font-size: 13px;"">Billed To</p>
        <p style=""margin: 0;"">ClientCo Inc</p>
    </div>
    <div style=""flex: 1;"">
        <p style=""margin: 0 0 5px 0; color: #f97316; font-size: 13px;"">Date of Issue</p>
        <p style=""margin: 0 0 15px 0;"">2026-04-01</p>
    </div>
    <div style=""flex: 1;"">
        <p style=""margin: 0 0 5px 0; color: #f97316; font-size: 13px;"">Invoice Number</p>
        <p style=""margin: 0;"">INV-2026-00001</p>
    </div>
    <div style=""flex: 1; text-align: right;"">
        <p style=""margin: 0 0 5px 0; color: #f97316; font-size: 13px;"">Amount Due (USD)</p>
        <p style=""margin: 0; font-size: 24px; font-weight: bold; color: #333333;"">$1,500.00</p>
    </div>
</div>

</body></html>";
}
