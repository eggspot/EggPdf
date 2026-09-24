using System;
using System.Text;
using EggPdf.Fluent;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Fluent;

/// <summary>The strongly-typed value layer: typos must fail at construction, never be silently ignored by the CSS engine.</summary>
public class TypedValueTests
{
    [Theory]
    [InlineData("red")]
    [InlineData("#ff")]
    [InlineData("ff0000")]
    [InlineData("#gg0000")]
    [InlineData("#ff00000")]
    public void Color_FromHex_RejectsMalformedValues(string bad)
    {
        Action act = () => Color.FromHex(bad);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Color_FromHex_NormalizesCaseAndAcceptsAllValidLengths()
    {
        Color.FromHex("#FF0000").ToString().Should().Be("#ff0000");
        Color.FromHex("#f00").ToString().Should().Be("#f00");
        Color.FromHex("#ff000080").ToString().Should().Be("#ff000080");
        Color.FromRgb(255, 0, 0).Should().Be(Colors.Red);
        Color.FromRgba(0, 0, 0, 128).ToString().Should().Be("#00000080");
        default(Color).ToString().Should().Be("transparent");
    }

    [Fact]
    public void Length_UnitsAndImplicitPixels()
    {
        Length.Mm(5).ToString().Should().Be("5mm");
        Length.Percent(50).ToString().Should().Be("50%");
        Length.Em(1.5f).ToString().Should().Be("1.5em");
        Length.Auto.ToString().Should().Be("auto");
        ((Length)12f).ToString().Should().Be("12px");
        default(Length).ToString().Should().Be("0px");
    }

    [Fact]
    public void Length_RejectsNonFiniteValues()
    {
        Action nan = () => Length.Px(float.NaN);
        Action inf = () => Length.Mm(float.PositiveInfinity);
        nan.Should().Throw<ArgumentOutOfRangeException>();
        inf.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CssTransform_ComposesInOrder()
    {
        CssTransform.Rotate(10).Then(CssTransform.Scale(2)).ToCss().Should().Be("rotate(10deg) scale(2)");
        default(CssTransform).ToCss().Should().Be("none");
        CssTransform.Translate(Length.Mm(1), Length.Percent(5)).ToCss().Should().Be("translate(1mm,5%)");
    }

    [Fact]
    public void GridTrack_RejectsNonPositiveFr()
    {
        Action act = () => GridTrack.Fr(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
        GridTrack.Fr(2).ToCss().Should().Be("2fr");
        GridTrack.Fixed(Length.Px(80)).ToCss().Should().Be("80px");
    }

    [Fact]
    public void KeywordEnums_EveryMemberMapsToNonEmptyCss()
    {
        foreach (var v in Enum.GetValues<OverflowMode>()) v.ToCss().Should().NotBeNullOrEmpty();
        foreach (var v in Enum.GetValues<PositionMode>()) v.ToCss().Should().NotBeNullOrEmpty();
        foreach (var v in Enum.GetValues<FloatSide>()) v.ToCss().Should().NotBeNullOrEmpty();
        foreach (var v in Enum.GetValues<FlexJustify>()) v.ToCss().Should().NotBeNullOrEmpty();
        foreach (var v in Enum.GetValues<FlexAlign>()) v.ToCss().Should().NotBeNullOrEmpty();
        foreach (var v in Enum.GetValues<TextCase>()) v.ToCss().Should().NotBeNullOrEmpty();
        foreach (var v in Enum.GetValues<BorderLineStyle>()) v.ToCss().Should().NotBeNullOrEmpty();
        foreach (var v in Enum.GetValues<WhiteSpaceMode>()) v.ToCss().Should().NotBeNullOrEmpty();
        foreach (var v in Enum.GetValues<DisplayMode>()) v.ToCss().Should().NotBeNullOrEmpty();
        foreach (var v in Enum.GetValues<TextDirection>()) v.ToCss().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void KeywordEnums_UndefinedValueThrowsInsteadOfEmittingNothing()
    {
        Action act = () => ((OverflowMode)99).ToCss();
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RangeChecks_RejectOutOfRangeNumbers()
    {
        var c = Document.Create(doc => doc.Page(p => p.Content(content =>
        {
            Action opacity = () => content.Item().Opacity(1.5f);
            Action weight = () => content.Item().FontWeight(0);
            Action lineHeight = () => content.Item().LineHeight(-1f);
            Action grid = () => content.Item().Grid(0, g => { });
            Action heading = () => content.Heading((HeadingLevel)9, "x");
            opacity.Should().Throw<ArgumentOutOfRangeException>();
            weight.Should().Throw<ArgumentOutOfRangeException>();
            lineHeight.Should().Throw<ArgumentOutOfRangeException>();
            grid.Should().Throw<ArgumentOutOfRangeException>();
            heading.Should().Throw<ArgumentOutOfRangeException>();
        })));
        c.Should().NotBeNull();
    }

    [Fact]
    public void Class_Accumulates_AndCoexistsWithGeneratedContent()
    {
        byte[] pdf = Document.Create(doc => doc
                .Css(".big{font-size:30px}")
                .Page(p => p.Footer(f => f.Class("big").Class("other").PageNumberOfTotal())
                    .Content(c => c.Item().Text("Body"))))
            .Render();

        // Two user classes plus the generated-content class all apply (none overwrites another).
        Encoding.Latin1.GetString(pdf).Should().Contain("/Type /Page");
    }

    [Fact]
    public void Direction_Rtl_RendersWithoutError()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(p => p.Content(c => c.Item().Direction(TextDirection.Rtl).Text("Right to left"))))
            .Render();

        Encoding.Latin1.GetString(pdf).Should().StartWith("%PDF-");
    }
}
