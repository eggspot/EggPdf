using System.Collections.Generic;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class FontFeatureSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("normal")]
    [InlineData("NORMAL")]
    public void ParseActiveTags_EmptyOrNormal_ReturnsNoTags(string? value)
    {
        FontFeatureSettings.ParseActiveTags(value).Should().BeEmpty();
    }

    [Fact]
    public void ParseActiveTags_TagWithNoValue_DefaultsActive()
    {
        FontFeatureSettings.ParseActiveTags("\"liga\"").Should().Equal("liga");
    }

    [Fact]
    public void ParseActiveTags_TagOn_IsActive()
    {
        FontFeatureSettings.ParseActiveTags("\"smcp\" on").Should().Equal("smcp");
    }

    [Fact]
    public void ParseActiveTags_TagOff_IsInactive()
    {
        FontFeatureSettings.ParseActiveTags("\"liga\" off").Should().BeEmpty();
    }

    [Fact]
    public void ParseActiveTags_TagZero_IsInactive()
    {
        FontFeatureSettings.ParseActiveTags("\"liga\" 0").Should().BeEmpty();
    }

    [Fact]
    public void ParseActiveTags_TagNonZeroInteger_IsActive()
    {
        FontFeatureSettings.ParseActiveTags("\"ss01\" 1").Should().Equal("ss01");
    }

    [Fact]
    public void ParseActiveTags_MultipleTags_ReturnsOnlyActiveOnesSorted()
    {
        var result = FontFeatureSettings.ParseActiveTags("\"zero\" 1, \"liga\" 0, \"smcp\" on");
        result.Should().Equal("smcp", "zero"); // sorted, "liga" excluded (off)
    }

    [Fact]
    public void ParseActiveTags_SingleQuotedTag_IsParsed()
    {
        FontFeatureSettings.ParseActiveTags("'tnum' 1").Should().Equal("tnum");
    }

    [Fact]
    public void ParseActiveTags_DuplicateTags_Deduplicated()
    {
        FontFeatureSettings.ParseActiveTags("\"liga\" 1, \"liga\" 1").Should().Equal("liga");
    }

    [Fact]
    public void ParseActiveTags_MalformedValue_YieldsNoTags_AndKeepsTheValidOnes()
    {
        FontFeatureSettings.ParseActiveTags("garbage, \"unterminated").Should().BeEmpty();
        FontFeatureSettings.ParseActiveTags("\"smcp\" 1, garbage").Should().Equal("smcp");
    }

    [Fact]
    public void BuildFontNameSuffix_NoActiveFeatures_ReturnsEmptyString()
    {
        FontFeatureSettings.BuildFontNameSuffix("normal").Should().Be("");
        FontFeatureSettings.BuildFontNameSuffix(null).Should().Be("");
    }

    [Fact]
    public void BuildFontNameSuffix_ActiveFeatures_ReturnsSortedDashJoinedSuffix()
    {
        FontFeatureSettings.BuildFontNameSuffix("\"zero\" 1, \"smcp\" on").Should().Be("-Feat-smcp-zero");
    }

    [Fact]
    public void BuildFontNameSuffix_SameLogicalSetDifferentOrder_ProducesIdenticalSuffix()
    {
        var a = FontFeatureSettings.BuildFontNameSuffix("\"zero\" 1, \"smcp\" on");
        var b = FontFeatureSettings.BuildFontNameSuffix("\"smcp\" 1, \"zero\" on");
        a.Should().Be(b, "declaration order must not affect the embedded-font cache key");
    }
}
