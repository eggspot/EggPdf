using System.Linq;
using EggPdf.Html;
using EggPdf.Html.Dom;
using EggPdf.Pdf;
using EggPdf.Svg;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Svg;

public class SvgFilterFillRuleTests
{
    private const float PxPerUnit = 2f;
    private const string Identity = "<defs><filter id='i'><feOffset dx='0' dy='0'/></filter></defs>";
    private const string SameDirectionSquares = "M20 20 H80 V80 H20 Z M40 40 H60 V60 H40 Z";

    private static byte CenterAlpha(string inner)
    {
        var html = "<html><body><svg width='100' height='100' viewBox='0 0 100 100'>" + Identity + inner + "</svg></body></html>";
        var doc = HtmlParser.Parse(html);
        var svg = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "svg");
        var pdf = new PdfDocument();
        var (_, images) = SvgRenderer.Render(SvgParser.Parse(svg)!, 0, 0, 100, 100, pdf);
        var img = pdf.GetImage(images[0])!;
        int center = (int)((50 - 14) * PxPerUnit);
        return img.SMaskData![center * img.Width + center];
    }

    [Fact]
    public void DefaultNonzeroRule_FillsAnInnerContourWoundTheSameWay()
    {
        CenterAlpha("<path d='" + SameDirectionSquares + "' fill='black' filter='url(#i)'/>")
            .Should().BeGreaterThan(240, "nonzero winding counts the inner square twice, so it is filled");
    }

    [Fact]
    public void EvenOddRule_LeavesTheInnerContourOpen()
    {
        CenterAlpha("<path d='" + SameDirectionSquares + "' fill='black' fill-rule='evenodd' filter='url(#i)'/>")
            .Should().Be(0);
    }

    [Fact]
    public void FillRule_IsInheritedFromTheGroup()
    {
        CenterAlpha("<g fill-rule='evenodd' filter='url(#i)'><path d='" + SameDirectionSquares + "' fill='black'/></g>")
            .Should().Be(0);
    }
}
