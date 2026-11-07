using EggPdf.Css;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class MediaQueryEvaluatorTests
{
    private static readonly MediaQueryEvaluator PortraitA4Print = new("print", 794f, 1123f);

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("all", true)]
    [InlineData("print", true)]
    [InlineData("PRINT", true)]
    [InlineData("screen", false)]
    [InlineData("speech", false)]
    [InlineData("not screen", true)]
    [InlineData("not print", false)]
    [InlineData("only print", true)]
    [InlineData("screen, print", true)]
    [InlineData("screen, speech", false)]
    public void MediaType_MatchesPrint(string? query, bool expected)
    {
        PortraitA4Print.Matches(query).Should().Be(expected);
    }

    [Theory]
    [InlineData("(max-width: 600px)", false)]
    [InlineData("(max-width: 1000px)", true)]
    [InlineData("(min-width: 700px)", true)]
    [InlineData("(min-width: 49em)", true)]
    [InlineData("(min-width: 50em)", false)]
    [InlineData("only screen and (max-width: 600px)", false)]
    [InlineData("print and (max-width: 1000px)", true)]
    [InlineData("print and (min-width: 1000px)", false)]
    [InlineData("print and (orientation: portrait)", true)]
    [InlineData("(orientation: landscape)", false)]
    [InlineData("(min-height: 1000px) and (max-width: 800px)", true)]
    [InlineData("not (max-width: 600px)", true)]
    [InlineData("(width >= 700px)", true)]
    [InlineData("(width < 700px)", false)]
    [InlineData("(400px <= width <= 800px)", true)]
    [InlineData("(min-aspect-ratio: 1/1)", false)]
    [InlineData("(max-aspect-ratio: 1/1)", true)]
    [InlineData("(hover: hover)", false)]
    [InlineData("(hover: none)", true)]
    [InlineData("(prefers-color-scheme: dark)", false)]
    [InlineData("(color)", true)]
    [InlineData("(unknown-feature: 1)", false)]
    [InlineData("(max-width: 600px), print", true)]
    public void Features_EvaluatedAgainstPage(string query, bool expected)
    {
        PortraitA4Print.Matches(query).Should().Be(expected);
    }

    [Theory]
    [InlineData("(max-width:")]
    [InlineData("print and")]
    [InlineData("((")]
    [InlineData("and and and")]
    public void MalformedQuery_DoesNotMatchAndDoesNotThrow(string query)
    {
        PortraitA4Print.Matches(query).Should().BeFalse();
    }

    [Fact]
    public void LandscapePage_OrientationLandscapeMatches()
    {
        new MediaQueryEvaluator("print", 1123f, 794f).Matches("(orientation: landscape)").Should().BeTrue();
    }
}
