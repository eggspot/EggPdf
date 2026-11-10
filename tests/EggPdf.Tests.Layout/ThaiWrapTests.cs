using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class ThaiWrapTests
{
    [Fact]
    public void LongThaiParagraph_WrapsIntoMultipleLines()
    {
        var thai = "ภาษาไทยเป็นภาษาที่ไม่มีช่องว่างระหว่างคำและประโยคจึงต้องแบ่งบรรทัดอย่างระมัดระวังเพื่อให้อ่านง่าย";
        var root = LayoutTestHelper.Layout("<div style='width:200px'><p>" + thai + "</p></div>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Height.Should().BeGreaterThan(19.2f * 2,
            "Thai has no spaces, so without syllable break opportunities the paragraph would stay on one overflowing line");
    }

    [Fact]
    public void LatinParagraph_WrappingUnchanged()
    {
        var root = LayoutTestHelper.Layout("<div style='width:200px'><p>short text</p></div>", 600, 800);
        root.FindByTag("p")!.Height.Should().BeApproximately(19.2f, 0.5f);
    }

    [Fact]
    public void ThaiParagraphWithInlineElement_WrapsAcrossLines()
    {
        var thai = "ภาษาไทยเป็นภาษาที่ไม่มีช่องว่างระหว่างคำและประโยคจึงต้องแบ่งบรรทัดอย่างระมัดระวังเพื่อให้อ่านง่าย";
        var root = LayoutTestHelper.Layout(
            "<div style='width:200px'><p>" + thai + " <b>ตัวหนา</b> " + thai + "</p></div>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Height.Should().BeGreaterThan(19.2f * 3,
            "mixed inline content goes through a different word loop than plain text, and Thai must wrap there too");
        foreach (var child in p.Children)
            (child.X + child.Width).Should().BeLessThanOrEqualTo(8f + 200f + 1f, "no word box may overflow the 200px container");
    }
}
