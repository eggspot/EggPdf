using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class FlexAbsolutePositionTests
{
    private static (float x, float y) FindTextPosition(byte[] pdf, string text)
    {
        var content = Encoding.ASCII.GetString(pdf);
        var match = Regex.Match(content,
            @"(-?\d+\.\d+) (-?\d+\.\d+) Td \(" + Regex.Escape(text) + @"\) Tj");
        match.Success.Should().BeTrue($"text '{text}' should appear in the content stream");
        return (float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task FlexColumn_AbsoluteChild_DoesNotConsumeFlexSpace()
    {
        const string flexOpen =
            "<div style=\"display:flex;flex-direction:column;position:relative;width:300px;height:300px\">";
        const string absChild =
            "<div style=\"position:absolute;top:10px;right:10px;width:50px;height:50px;background:red\"></div>";

        var withAbs = await HtmlToPdf.RenderAsync(
            $"<html><body>{flexOpen}{absChild}<p>FIRSTITEM</p></div></body></html>");
        var withoutAbs = await HtmlToPdf.RenderAsync(
            $"<html><body>{flexOpen}<p>FIRSTITEM</p></div></body></html>");

        var posWith = FindTextPosition(withAbs, "FIRSTITEM");
        var posWithout = FindTextPosition(withoutAbs, "FIRSTITEM");

        posWith.y.Should().BeApproximately(posWithout.y, 2f,
            "an absolutely positioned sibling must not push flex content down");
        posWith.x.Should().BeApproximately(posWithout.x, 2f);
    }

    [Fact]
    public async Task AbsoluteChild_ContainingBlockIsPaddingBox_InsideBorder()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body style=\"margin:0;padding:0\">" +
            "<div style=\"position:relative;border:10px solid #000;width:100px;height:100px\">" +
            "<div style=\"position:absolute;left:0;top:0;width:20px;height:20px;background:#ff0000\"></div>" +
            "</div></body></html>");

        var content = Encoding.ASCII.GetString(pdf);
        var m = Regex.Match(content,
            @"1\.00 0\.00 0\.00 rg\s+(-?\d+\.\d+) (-?\d+\.\d+) (-?\d+\.\d+) (-?\d+\.\d+) re f");
        m.Success.Should().BeTrue("the absolute child's red background must be painted");

        float x = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        x.Should().BeApproximately(7.5f, 1f,
            "an absolute box's containing block is the ancestor's padding box (CSS 2.1 §10.1), so left:0 lands inside " +
            "the 10px (7.5pt) border, not at the border-box edge");
    }

    [Fact]
    public async Task FlexContainer_AbsoluteChild_HonorsTopRightOffsets()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"display:flex;position:relative;width:300px;height:300px\">" +
            "<div style=\"position:absolute;top:10px;right:10px;width:50px;height:50px;background:red\"></div>" +
            "</div></body></html>");

        var content = Encoding.ASCII.GetString(pdf);

        var match = Regex.Match(content,
            @"1\.00 0\.00 0\.00 rg\s+(-?\d+\.\d+) (-?\d+\.\d+) (-?\d+\.\d+) (-?\d+\.\d+) re f");
        match.Success.Should().BeTrue("the absolute child's red background must be painted");

        float x = float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        float w = float.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);

        w.Should().BeApproximately(37.5f, 1f, "50px = 37.5pt");
        x.Should().BeGreaterThan(150f,
            "right:10px puts the box near the right edge of the 300px (225pt) container that starts at ~6pt: " +
            "6 + 225 - 7.5 - 37.5 = 186, not at its left origin");
    }
}
