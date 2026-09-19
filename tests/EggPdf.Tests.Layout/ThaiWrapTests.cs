using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class ThaiWrapTests
{
    [Fact]
    public void LongThaiParagraph_WrapsIntoMultipleLines()
    {
        // Thai has no spaces; without break opportunities the whole paragraph stayed on one line
        // and overflowed its container.
        var thai = "ภาษาไทยเป็นภาษาที่ไม่มีช่องว่างระหว่างคำและประโยคจึงต้องแบ่งบรรทัดอย่างระมัดระวังเพื่อให้อ่านง่าย";
        var root = LayoutTestHelper.Layout("<div style='width:200px'><p>" + thai + "</p></div>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Height.Should().BeGreaterThan(19.2f * 2, "the paragraph must wrap onto several lines");
    }

    [Fact]
    public void LatinParagraph_WrappingUnchanged()
    {
        var root = LayoutTestHelper.Layout("<div style='width:200px'><p>short text</p></div>", 600, 800);
        root.FindByTag("p")!.Height.Should().BeApproximately(19.2f, 0.5f);
    }
}
