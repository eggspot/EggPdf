using System;
using System.Collections.Generic;
using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class InlineTextFlowTests
{
    private static LayoutBox Layout(string body, string css = "") =>
        LayoutTestHelper.Layout("<html><head><style>body{margin:0} p,pre{margin:0}" + css + "</style></head><body>" + body + "</body></html>", 600, 800);

    private static List<LayoutBox> TextBoxes(LayoutBox container) =>
        container.FindAll(b => !string.IsNullOrEmpty(b.Text) && b.Children.Count == 0).ToList();

    [Fact]
    public void SmallerInlineRun_SharesTheBaselineOfTheSurroundingText()
    {
        var root = Layout("<p id='p' style='font-size:20px;line-height:1.2'>Big <span style='font-size:10px'>small</span></p>");
        var boxes = TextBoxes(root.FindById("p")!);
        var big = boxes.First(b => b.Text!.Contains("Big"));
        var small = boxes.First(b => b.Text!.Contains("small"));

        float bigBaseline = big.Y + TextMeasurer.GetBaselineOffset(20, big.Height, null, null, null);
        float smallBaseline = small.Y + TextMeasurer.GetBaselineOffset(10, small.Height, null, null, null);
        smallBaseline.Should().BeApproximately(bigBaseline, 0.5f);
    }

    [Fact]
    public void PreWithInlineChildren_KeepsSpacesAndLineBreaksWithoutOverlap()
    {
        var root = Layout("<pre id='p'>a  <b>bold</b>  c\nnext line</pre>");
        var boxes = TextBoxes(root.FindById("p")!);

        var lines = boxes.GroupBy(b => (int)Math.Round(b.Y)).OrderBy(g => g.Key)
            .Select(g => g.OrderBy(b => b.X).ToList()).ToList();
        lines.Should().HaveCount(2);
        string.Concat(lines[0].Select(b => b.Text)).Should().Be("a  bold  c");
        string.Concat(lines[1].Select(b => b.Text)).Should().Be("next line");
        for (int i = 1; i < lines[0].Count; i++)
            lines[0][i].X.Should().BeGreaterThanOrEqualTo(lines[0][i - 1].X + lines[0][i - 1].Width - 0.5f);
    }

    [Fact]
    public void FlexRowItem_DoesNotShrinkBelowItsLongestWord()
    {
        var root = Layout("<div style='display:flex;width:100px'><div id='a'>Supercalifragilistic</div><div style='width:200px'>x</div></div>");

        float word = TextMeasurer.MeasureWidth("Supercalifragilistic", 16, null);
        root.FindById("a")!.Width.Should().BeGreaterThanOrEqualTo(word - 0.5f);
    }

    [Fact]
    public void FlexRowItem_WithOverflowHidden_MayShrinkBelowItsContent()
    {
        var root = Layout("<div style='display:flex;width:100px'><div id='a' style='overflow:hidden'>Supercalifragilistic</div><div style='width:200px'>x</div></div>");

        float word = TextMeasurer.MeasureWidth("Supercalifragilistic", 16, null);
        root.FindById("a")!.Width.Should().BeLessThan(word);
    }

    [Fact]
    public void FlexShrink_RedistributesWhatAClampedItemCannotGive()
    {
        var root = Layout("<div style='display:flex;width:300px'>" +
                          "<div id='a' style='width:200px;min-width:190px'></div>" +
                          "<div id='b' style='width:200px'></div></div>");

        root.FindById("a")!.Width.Should().BeApproximately(190, 0.5f);
        root.FindById("b")!.Width.Should().BeApproximately(110, 0.5f);
    }
}
