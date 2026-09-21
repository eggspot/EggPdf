using System.Linq;
using EggPdf.Css;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class FontVariationMarkerTests
{
    private static ComputedStyle Style(string family, params (string k, string v)[] props)
    {
        var s = new ComputedStyle();
        s.Set("font-family", family);
        foreach (var (k, v) in props) s.Set(k, v);
        return s;
    }

    private static string Axes(ComputedStyle s)
    {
        FontVariationMarker.Split(s.Get("font-family"), out var axes);
        return axes == null ? "" : string.Join(";", axes.Select(kv => kv.Key + "=" + kv.Value));
    }

    [Fact]
    public void NoVariationProperties_LeavesTheFamilyUntouched()
    {
        var s = Style("Arial, sans-serif");
        FontVariationMarker.Apply(s);
        s.Get("font-family").Should().Be("Arial, sans-serif");
    }

    [Theory]
    [InlineData("75%", "wdth=75")]
    [InlineData("condensed", "wdth=75")]
    [InlineData("ultra-expanded", "wdth=200")]
    [InlineData("87.5%", "wdth=87.5")]
    public void FontStretch_BecomesTheWdthAxis(string stretch, string expected)
    {
        var s = Style("Bahnschrift", ("font-stretch", stretch));
        FontVariationMarker.Apply(s);
        Axes(s).Should().Be(expected);
    }

    [Fact]
    public void NormalStretch_AddsNothing()
    {
        var s = Style("Bahnschrift", ("font-stretch", "100%"));
        FontVariationMarker.Apply(s);
        Axes(s).Should().BeEmpty();
        s.Get("font-family").Should().Be("Bahnschrift");
    }

    [Fact]
    public void ObliqueAngle_BecomesANegativeSlntAxis()
    {
        var s = Style("Inter", ("font-style", "oblique 10deg"));
        FontVariationMarker.Apply(s);
        Axes(s).Should().Be("slnt=-10");
    }

    [Fact]
    public void BareOblique_DrivesNoAxis()
    {
        var s = Style("Inter", ("font-style", "oblique"));
        FontVariationMarker.Apply(s);
        Axes(s).Should().BeEmpty();
    }

    [Fact]
    public void FontVariationSettings_AreParsed_AndOverrideStretch()
    {
        var s = Style("Bahnschrift", ("font-stretch", "75%"), ("font-variation-settings", "\"wdth\" 90, \"wght\" 650, 'GRAD' 20"));
        FontVariationMarker.Apply(s);
        Axes(s).Should().Be("GRAD=20;wdth=90;wght=650");
    }

    [Fact]
    public void MalformedSettings_AreSkipped()
    {
        var s = Style("X", ("font-variation-settings", "\"abc\" 1, wdth 5, \"wght\" oops, \"opsz\" 12"));
        FontVariationMarker.Apply(s);
        Axes(s).Should().Be("opsz=12");
    }

    [Fact]
    public void AStaleInheritedMarker_IsReplacedByThisElementsOwnAxes()
    {
        var s = Style("Bahnschrift, \"__vf:wdth=75\"");
        FontVariationMarker.Apply(s);
        s.Get("font-family").Should().Be("Bahnschrift", "a child that resets its settings must not keep its parent's axes");

        var s2 = Style("Bahnschrift, \"__vf:wdth=75\"", ("font-variation-settings", "\"wght\" 500"));
        FontVariationMarker.Apply(s2);
        Axes(s2).Should().Be("wght=500");
    }

    [Fact]
    public void Split_ReturnsTheCleanFamilyList()
    {
        var clean = FontVariationMarker.Split("Foo, \"Bar Baz\", \"__vf:wdth=75;slnt=-5\", serif", out var axes);
        clean.Should().Be("Foo, \"Bar Baz\", serif");
        axes!["wdth"].Should().Be(75f);
        axes["slnt"].Should().Be(-5f);
    }

    [Fact]
    public void Split_WithoutAMarker_ReturnsTheInputAndNoAxes()
    {
        FontVariationMarker.Split("Arial, serif", out var axes).Should().Be("Arial, serif");
        axes.Should().BeNull();
        FontVariationMarker.Split(null, out _).Should().BeEmpty();
    }

    [Fact]
    public void NameSuffix_IsStable_AndSafeForPdfNames()
    {
        var suffix = FontVariationMarker.NameSuffix("Foo, \"__vf:slnt=-10;wdth=75\"");
        suffix.Should().Be("-VFslnt_-10_wdth_75");
        suffix.Should().MatchRegex("^[A-Za-z0-9_-]+$");
        FontVariationMarker.NameSuffix("Foo").Should().BeEmpty();
    }
}
