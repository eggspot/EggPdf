using System.Text;
using EggPdf.Css;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class CssCounterContextTests
{
    private static CssCounterContext NestedSections()
    {
        var context = new CssCounterContext();
        context.ApplyReset("section");
        context.ApplyIncrement("section");
        context.ApplyIncrement("section");
        context.ApplyReset("section");
        context.ApplyIncrement("section");
        context.ApplyReset("section 3");
        return context;
    }

    private static ComputedStyle Quotes(string quotes)
    {
        var style = new ComputedStyle();
        style.Set("quotes", quotes);
        return style;
    }

    [Fact]
    public void Counters_NestedScopes_JoinsEveryLevelOutermostFirst()
    {
        NestedSections().ResolveContent("counters(section, \".\")", null).Should().Be("2.1.3");
    }

    [Fact]
    public void Counters_WithStyle_FormatsEveryLevel()
    {
        NestedSections().ResolveContent("counters(section, '-', upper-roman)", null).Should().Be("II-I-III");
    }

    [Fact]
    public void Counters_SeparatorWithCommaAndParenthesis_IsKeptWhole()
    {
        NestedSections().ResolveContent("counters(section, ', (') ' end'", null).Should().Be("2, (1, (3 end");
    }

    [Fact]
    public void Counters_UndefinedCounter_ReadsZero()
    {
        new CssCounterContext().ResolveContent("counters(missing, '.')", null).Should().Be("0");
    }

    [Fact]
    public void Counter_ShowsOnlyTheInnermostLevel()
    {
        NestedSections().ResolveContent("counter(section, lower-alpha) '.'", null).Should().Be("c.");
    }

    [Fact]
    public void OpenQuote_Nested_UsesTheSecondPair()
    {
        var context = new CssCounterContext();
        var style = Quotes("'«' '»' '‹' '›'");

        var marks = new[]
        {
            context.ResolveContent("open-quote", null, style),
            context.ResolveContent("open-quote", null, style),
            context.ResolveContent("close-quote", null, style),
            context.ResolveContent("close-quote", null, style),
        };

        marks.Should().Equal("«", "‹", "›", "»");
    }

    [Fact]
    public void OpenQuote_DeeperThanTheList_KeepsUsingTheLastPair()
    {
        var context = new CssCounterContext();
        var style = Quotes("'«' '»'");

        context.ResolveContent("open-quote", null, style);
        context.ResolveContent("open-quote", null, style).Should().Be("«");
    }

    [Fact]
    public void DefaultQuotes_NestedLevel_UsesSingleQuotes()
    {
        var context = new CssCounterContext();

        context.ResolveContent("open-quote", null).Should().Be("“");
        context.ResolveContent("open-quote", null).Should().Be("‘");
        context.ResolveContent("close-quote", null).Should().Be("’");
        context.ResolveContent("close-quote", null).Should().Be("”");
    }

    [Fact]
    public void NoOpenQuote_RaisesTheNestingLevelWithoutAMark()
    {
        var context = new CssCounterContext();

        context.ResolveContent("no-open-quote", null).Should().BeNull();
        context.ResolveContent("open-quote", null).Should().Be("‘");
    }

    [Fact]
    public void CloseQuote_WithoutAnOpenQuote_ProducesNothing()
    {
        var context = new CssCounterContext();

        context.ResolveContent("close-quote", null).Should().BeNull();
        context.ResolveContent("open-quote", null).Should().Be("“");
    }

    [Fact]
    public void QuotesNone_OpenQuoteProducesNoMark()
    {
        new CssCounterContext().ResolveContent("open-quote 'x'", null, Quotes("none")).Should().Be("x");
    }

    [Fact]
    public void NestedQElements_InnerQuoteUsesTheNestedPair()
    {
        var root = LayoutTestHelper.Layout(
            "<style>q::before { content: open-quote; } q::after { content: close-quote; }</style>" +
            "<p><q>outer <q>inner</q> end</q></p>", 600, 600);

        var text = new StringBuilder();
        AppendText(root.FindByTag("p")!, text);

        text.ToString().Should().Contain("‘inner").And.Contain("’");
        text.ToString().Should().StartWith("“").And.EndWith("”");
    }

    private static void AppendText(LayoutBox box, StringBuilder text)
    {
        if (!string.IsNullOrEmpty(box.Text)) text.Append(box.Text);
        foreach (var child in box.Children) AppendText(child, text);
    }
}
