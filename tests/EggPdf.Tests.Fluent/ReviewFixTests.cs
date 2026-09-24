using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using EggPdf.Fluent;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Fluent;

/// <summary>Regression tests for defects found in code review: silent loss of styles/classes, unescaped CSS, exponent-notation numbers, dropped head markup.</summary>
public class ReviewFixTests
{
    private static string Pdf(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // ── numbers ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.00001f, "0.00001mm")]
    [InlineData(1.5f, "1.5mm")]
    [InlineData(0f, "0mm")]
    public void Length_NeverUsesExponentNotation_SmallValues(float value, string expected)
        => Length.Mm(value).ToString().Should().Be(expected);

    [Fact]
    public void Length_NeverUsesExponentNotation_HugeAndTinyValues()
    {
        Length.Px(1e20f).ToString().Should().NotContain("E").And.EndWith("px");
        Length.Px(1e-9f).ToString().Should().NotContain("E");
        CssTransform.Rotate(1e-7f).Then(CssTransform.Scale(1e21f)).ToCss().Should().NotContain("E");
        GridTrack.Fr(1e-8f).ToCss().Should().NotContain("E");
    }

    // ── free text is escaped ─────────────────────────────────────────────────

    [Fact]
    public void FontFace_QuotesBackslashesAndInjectionAttempts_CannotBreakTheRule()
    {
        byte[] pdf = Document.Create(doc => doc
                .FontFace("My \"Odd\" Font", @"C:\fonts\a.ttf")
                .FontFace("Evil", "x\"); } body{display:none} .y{z:url(\"")
                .Page(p => p.Content(c => c.Item().Text("StillVisible"))))
            .Render();

        Pdf(pdf).Should().Contain("StillVisible", "an injected 'body{display:none}' must not take effect");
    }

    [Fact]
    public void CssQuote_EscapesBackslashQuoteAndLineBreaks()
    {
        CssText.Quote("a\"b\\c\nd").Should().Be("\"a\\\"b\\\\c\\a d\"");
        CssText.Quote(null).Should().Be("\"\"");
    }

    // ── RawAttribute vs the builder's own class/style bookkeeping ────────────

    [Fact]
    public void RawAttribute_Style_IsRejected_PointingToRawStyle()
    {
        Action act = () => Document.Create(doc => doc.Page(p => p.Content(c => c.Item().RawAttribute("style", "color:red"))));

        act.Should().Throw<ArgumentException>().WithMessage("*RawStyle*");
    }

    [Fact]
    public void RawAttribute_Class_CombinesWithTypedClassesInsteadOfBeingLost()
    {
        byte[] pdf = Document.Create(doc => doc
                .Css(".hot{color:red} .cold{background-color:blue}")
                .Page(p => p.Content(c => c.Item().Class("cold").RawAttribute("class", "hot extra").Text("Both"))))
            .Render();

        var text = Pdf(pdf);
        text.Should().Contain("1.00 0.00 0.00 rg", "the raw class 'hot' applies");
        text.Should().Contain("0.00 0.00 1.00 rg", "the typed class 'cold' is not overwritten by the raw one");
    }

    [Fact]
    public void RawAttribute_Class_DoesNotSuppressGeneratedPageNumbers()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(p => p
                    .Footer(f => f.RawAttribute("class", "mine").PageNumberOfTotal())
                    .Content(c =>
                    {
                        for (int i = 0; i < 80; i++) c.Item().Height(20).Text("Line " + i);
                    })))
            .Render();

        var streams = new List<string>();
        foreach (Match m in Regex.Matches(Pdf(pdf), "stream\r?\n(.*?)endstream", RegexOptions.Singleline)) streams.Add(m.Groups[1].Value);
        streams.Count.Should().BeGreaterThan(1);
        streams[0].Should().NotBe(streams[streams.Count - 1], "the page-number ::after class must survive a raw class attribute");
    }

    // ── Raw() keeps head-level markup ────────────────────────────────────────

    [Fact]
    public void Raw_StyleElementInTheFragment_IsAppliedNotDiscarded()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(p => p.Content(c => c.Item().Raw("<style>.x{color:red}</style><div class='x'>styled</div>"))))
            .Render();

        Pdf(pdf).Should().Contain("1.00 0.00 0.00 rg");
    }

    // ── robustness ───────────────────────────────────────────────────────────

    [Fact]
    public void Build_VeryDeeplyNestedGeneratedTree_DoesNotOverflowTheStack()
    {
        var doc = Document.New();
        var column = doc.AddPage().Content();
        for (int i = 0; i < 30_000; i++)
            column = column.Item().Column();

        Action build = () => doc.Build();
        build.Should().NotThrow();
    }

    [Fact]
    public void Text_AppendsEachCall_AsDocumented()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(p => p.Content(c => c.Item().Text("Alpha").Text("Beta"))))
            .Render();

        var text = Pdf(pdf);
        text.Should().Contain("Alpha");
        text.Should().Contain("Beta");
    }
}
