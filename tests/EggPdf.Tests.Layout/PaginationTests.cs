using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class PaginationTests
{
    private LayoutBox Layout(string html)
        => LayoutTestHelper.Layout(html);

    [Fact]
    public void Orphans_ValueStoredInStyle()
    {
        var root = Layout("<style>p { orphans: 3; }</style><p>text</p>");
        var p = root.FindAllByTag("p")[0];
        p.Style.Get("orphans").Should().Be("3");
    }

    [Fact]
    public void Widows_ValueStoredInStyle()
    {
        var root = Layout("<style>p { widows: 4; }</style><p>text</p>");
        var p = root.FindAllByTag("p")[0];
        p.Style.Get("widows").Should().Be("4");
    }

    [Fact]
    public void OrphansWidows_BothStoredInStyle()
    {
        var root = Layout("<style>p { orphans: 2; widows: 3; }</style><p>text</p>");
        var p = root.FindAllByTag("p")[0];
        p.Style.Get("orphans").Should().Be("2");
        p.Style.Get("widows").Should().Be("3");
    }

    [Fact]
    public void BreakInsideAvoid_StoredInStyle()
    {
        var root = Layout("<div style='break-inside: avoid'>content</div>");
        var div = root.FindAllByTag("div")[0];
        div.Style.Get("break-inside").Should().Be("avoid");
    }

    [Fact]
    public void PageBreakBefore_StoredInStyle()
    {
        var root = Layout("<div style='break-before: page'>content</div>");
        var div = root.FindAllByTag("div")[0];
        div.Style.Get("break-before").Should().Be("page");
    }

    [Fact]
    public void PageBreakAfter_StoredInStyle()
    {
        var root = Layout("<div style='break-after: page'>content</div>");
        var div = root.FindAllByTag("div")[0];
        div.Style.Get("break-after").Should().Be("page");
    }
}
