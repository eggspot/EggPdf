using System.Collections.Generic;
using EggPdf.Css.Parser;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit;

public class PageRuleResolverTests
{
    private static PageSettings Resolve(string css) =>
        PageRuleResolver.Resolve(new List<CssStyleSheet> { CssStyleSheetParser.Parse(css) });

    [Fact]
    public void PercentageMargins_AreOfThePageWidthAndHeight()
    {
        var settings = Resolve("@page { size: 800px 1000px; margin: 10% 5%; }");

        settings.MarginTop.Should().BeApproximately(100f, 0.01f);
        settings.MarginBottom.Should().BeApproximately(100f, 0.01f);
        settings.MarginLeft.Should().BeApproximately(40f, 0.01f);
        settings.MarginRight.Should().BeApproximately(40f, 0.01f);
    }

    [Fact]
    public void PercentageMargin_DeclaredBeforeSize_UsesTheFinalSize()
    {
        var settings = Resolve("@page { margin-left: 10%; } @page { size: 500px 700px; }");

        settings.MarginLeft.Should().BeApproximately(50f, 0.01f);
    }

    [Fact]
    public void LaterMarginLonghand_OverridesTheShorthandSide()
    {
        var settings = Resolve("@page { margin: 20px; margin-left: 5px; }");

        settings.MarginTop.Should().Be(20f);
        settings.MarginLeft.Should().Be(5f);
    }

    [Theory]
    [InlineData("B5", 665.20f, 944.88f)]
    [InlineData("B4", 944.88f, 1334.17f)]
    [InlineData("JIS-B5", 687.87f, 971.34f)]
    [InlineData("JIS-B4", 971.34f, 1375.75f)]
    [InlineData("ledger", 1056f, 1632f)]
    public void StandardPageSizeNames_SetThePageSize(string name, float width, float height)
    {
        var settings = Resolve("@page { size: " + name + "; }");

        settings.PageWidthPx.Should().BeApproximately(width, 0.01f);
        settings.PageHeightPx.Should().BeApproximately(height, 0.01f);
    }

    [Fact]
    public void NamedPage_PercentageMargin_UsesTheNamedPageSize()
    {
        var baseSettings = Resolve("@page { margin: 10%; } @page wide { size: 2000px 1000px; }");

        var wide = PageRuleResolver.ForNamedPage(baseSettings, "wide");

        wide.MarginLeft.Should().BeApproximately(200f, 0.01f);
        wide.MarginTop.Should().BeApproximately(100f, 0.01f);
    }
}
