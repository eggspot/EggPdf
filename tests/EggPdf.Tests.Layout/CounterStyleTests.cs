using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class CounterStyleTests
{
    [Fact]
    public void CounterStyle_Cyclic_FirstItemUsesFirstSymbol()
    {
        var root = LayoutTestHelper.Layout(
            "<html><head><style>" +
            "@counter-style stars { system: cyclic; symbols: '★' '☆'; suffix: ' '; }" +
            "</style></head><body>" +
            "<ol style='list-style-type: stars'>" +
            "<li>Item 1</li>" +
            "<li>Item 2</li>" +
            "<li>Item 3</li>" +
            "</ol></body></html>", 400, 600);

        var lis = root.FindAllByTag("li");
        lis.Should().HaveCount(3);

        bool found1 = false, found2 = false, found3 = false;
        foreach (var box in lis[0].Children)
            if (box.Text?.Contains("★") == true) { found1 = true; break; }
        foreach (var box in lis[1].Children)
            if (box.Text?.Contains("☆") == true) { found2 = true; break; }
        foreach (var box in lis[2].Children)
            if (box.Text?.Contains("★") == true) { found3 = true; break; }

        found1.Should().BeTrue("first li should have ★ marker");
        found2.Should().BeTrue("second li should have ☆ marker (cyclic)");
        found3.Should().BeTrue("third li cycles back to ★");
    }

    [Fact]
    public void CounterStyle_Symbolic_RepeatsSymbols()
    {
        var root = LayoutTestHelper.Layout(
            "<html><head><style>" +
            "@counter-style dots { system: symbolic; symbols: '•'; suffix: ' '; }" +
            "</style></head><body>" +
            "<ol style='list-style-type: dots'>" +
            "<li>A</li>" +
            "<li>B</li>" +
            "<li>C</li>" +
            "</ol></body></html>", 400, 600);

        var lis = root.FindAllByTag("li");
        lis.Should().HaveCount(3);

        foreach (var li in lis)
        {
            bool hasMarker = false;
            foreach (var box in li.Children)
                if (box.Text?.Contains("•") == true) { hasMarker = true; break; }
            hasMarker.Should().BeTrue("each li should have a • marker");
        }
    }

    [Fact]
    public void CounterStyle_CustomSuffix_AppliedToMarkers()
    {
        var root = LayoutTestHelper.Layout(
            "<html><head><style>" +
            "@counter-style parens { system: numeric; symbols: '0' '1' '2' '3' '4' '5' '6' '7' '8' '9'; prefix: '('; suffix: ')'; }" +
            "</style></head><body>" +
            "<ol style='list-style-type: parens'><li>X</li></ol>" +
            "</body></html>", 400, 600);

        var li = root.FindByTag("li");
        li.Should().NotBeNull();

        bool hasOpen = false;
        foreach (var box in li!.Children)
            if (box.Text?.Contains("(") == true) { hasOpen = true; break; }
        hasOpen.Should().BeTrue("custom prefix '(' should appear in marker");
    }

    [Fact]
    public void CounterStyle_Extends_UsesParentSystem()
    {
        var root = LayoutTestHelper.Layout(
            "<html><head><style>" +
            "@counter-style my-decimal { system: extends decimal; suffix: '. '; }" +
            "</style></head><body>" +
            "<ol style='list-style-type: my-decimal'>" +
            "<li>A</li><li>B</li>" +
            "</ol></body></html>", 400, 600);

        var lis = root.FindAllByTag("li");
        lis.Should().HaveCount(2);

        bool found1 = false;
        foreach (var box in lis[0].Children)
            if (box.Text != null && box.Text.Contains("1")) { found1 = true; break; }
        found1.Should().BeTrue("extends decimal: first marker should contain '1'");
    }

    [Fact]
    public void CounterSet_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='counter-set: myCount 5'>x</div>", 400, 600);
        var div = root.FindByTag("div");
        div!.Style.Get("counter-set").Should().Be("myCount 5",
            "counter-set should be preserved in computed style");
    }

    [Fact]
    public void CounterSet_SetsCounterValue()
    {
        var root = LayoutTestHelper.Layout(
            "<style>" +
            "ol { counter-reset: n; }" +
            "li { counter-increment: n; }" +
            "li::before { content: counter(n) '. '; }" +
            "</style>" +
            "<ol><li style='counter-set: n 9'>item</li></ol>", 400, 600);

        var li = root.FindByTag("li");
        li.Should().NotBeNull();

        var allText = string.Concat(li!.Children
            .Where(b => !string.IsNullOrEmpty(b.Text))
            .Select(b => b.Text));

        allText.Should().Contain("10",
            "counter-set: n 9 then counter-increment: n 1 should produce 10");
    }
}
