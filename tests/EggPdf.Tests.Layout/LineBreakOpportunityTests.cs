using System;
using System.Collections.Generic;
using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class LineBreakOpportunityTests
{
    private static readonly string ZeroWidthSpace = ((char)0x200B).ToString();
    private static readonly string SoftHyphen = ((char)0x00AD).ToString();

    private static List<string> Lines(string css, string body)
    {
        var root = LayoutTestHelper.Layout("<html><head><style>body{margin:0} p{margin:0}" + css + "</style></head><body>" + body + "</body></html>", 600, 800);
        var p = root.FindById("p")!;
        return p.FindAll(b => !string.IsNullOrEmpty(b.Text) && b.Children.Count == 0)
            .GroupBy(b => (int)Math.Round(b.Y))
            .OrderBy(g => g.Key)
            .Select(g => string.Concat(g.OrderBy(b => b.X).Select(b => b.Text)).Trim())
            .ToList();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" <b>x</b>")]
    public void LongHyphenatedWord_BreaksAfterTheHyphen(string tail)
    {
        var lines = Lines("p{width:110px}", "<p id='p'>Supercalifragilistic-expialidocious" + tail + "</p>");

        lines.Count.Should().BeGreaterThan(1);
        lines[0].Should().EndWith("-");
        string.Concat(lines).Should().Contain("Supercalifragilistic-");
    }

    [Fact]
    public void DateWithHyphens_IsNotBrokenBeforeDigits()
    {
        var lines = Lines("p{width:40px}", "<p id='p'>2026-11-08</p>");

        lines.Should().ContainSingle().Which.Should().Be("2026-11-08");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" <b>x</b>")]
    public void ZeroWidthSpace_IsABreakOpportunityAndIsNotPainted(string tail)
    {
        var lines = Lines("p{width:80px}", "<p id='p'>aaaaaaaaaa" + ZeroWidthSpace + "bbbbbbbbbb" + tail + "</p>");

        lines.Count.Should().BeGreaterThan(1);
        string.Concat(lines).Should().NotContain(ZeroWidthSpace);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" <b>x</b>")]
    public void SoftHyphen_BreaksWithAVisibleHyphen(string tail)
    {
        var lines = Lines("p{width:90px}", "<p id='p'>Donau" + SoftHyphen + "dampf" + SoftHyphen + "schiff" + SoftHyphen + "fahrt" + tail + "</p>");

        lines.Count.Should().BeGreaterThan(1);
        lines[0].Should().EndWith("-");
        string.Concat(lines).Should().NotContain(SoftHyphen);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" <b>x</b>")]
    public void SoftHyphen_WithoutABreak_IsInvisible(string tail)
    {
        var lines = Lines("p{width:500px}", "<p id='p'>Donau" + SoftHyphen + "dampf" + SoftHyphen + "schiff" + tail + "</p>");

        lines.Should().ContainSingle();
        lines[0].Should().StartWith("Donaudampfschiff");
    }

    [Fact]
    public void NoWrapText_DoesNotPaintSoftHyphens()
    {
        var lines = Lines("p{white-space:nowrap}", "<p id='p'>Donau" + SoftHyphen + "dampf</p>");

        lines.Should().ContainSingle().Which.Should().Be("Donaudampf");
    }
}
