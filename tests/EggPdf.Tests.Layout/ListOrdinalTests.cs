using System.Linq;
using System.Text;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class ListOrdinalTests
{
    private static string[] Markers(string html) =>
        LayoutTestHelper.Layout(html, 600, 800)
            .FindAllByTag("li")
            .Select(li => li.Children.First(c => c.IsListMarker).Text!)
            .ToArray();

    [Fact]
    public void OrderedList_StartAttribute_NumbersFromStart()
    {
        Markers("<ol start='3'><li>Three</li><li>Four</li></ol>").Should().Equal("3.", "4.");
    }

    [Fact]
    public void OrderedList_NegativeStart_CountsUpThroughZero()
    {
        Markers("<ol start='-1'><li>a</li><li>b</li><li>c</li></ol>").Should().Equal("-1.", "0.", "1.");
    }

    [Fact]
    public void OrderedList_Reversed_CountsDownFromItemCount()
    {
        Markers("<ol reversed><li>a</li><li>b</li><li>c</li></ol>").Should().Equal("3.", "2.", "1.");
    }

    [Fact]
    public void OrderedList_ReversedWithStart_CountsDownFromStart()
    {
        Markers("<ol reversed start='10'><li>a</li><li>b</li></ol>").Should().Equal("10.", "9.");
    }

    [Fact]
    public void OrderedList_ItemValue_ResetsFollowingItems()
    {
        Markers("<ol><li>a</li><li value='7'>b</li><li>c</li></ol>").Should().Equal("1.", "7.", "8.");
    }

    [Fact]
    public void OrderedList_InvalidStart_FallsBackToOne()
    {
        Markers("<ol start='abc'><li>a</li><li>b</li></ol>").Should().Equal("1.", "2.");
    }

    [Fact]
    public void UnorderedListWithDecimalStyle_IgnoresItemValue()
    {
        Markers("<ul style='list-style-type:decimal'><li>a</li><li value='7'>b</li></ul>").Should().Equal("1.", "2.");
    }

    [Fact]
    public void LowerAlpha_PastZ_ContinuesWithTwoLetters()
    {
        Markers("<ol type='a' start='26' style='list-style-type:lower-alpha'><li>z</li><li>aa</li><li>ab</li></ol>")
            .Should().Equal("z.", "aa.", "ab.");
    }

    [Fact]
    public void UpperRoman_WithStart_UsesStartOrdinal()
    {
        Markers("<ol start='4' style='list-style-type:upper-roman'><li>a</li><li>b</li></ol>").Should().Equal("IV.", "V.");
    }

    [Fact]
    public void NestedOrderedLists_EachKeepsItsOwnNumbering()
    {
        var markers = Markers("<ol start='5'><li>a<ol><li>x</li><li>y</li></ol></li><li>b</li></ol>");

        markers.Should().Equal("5.", "1.", "2.", "6.");
    }

    [Fact]
    public void LongOrderedList_LastItemHasItsOrdinal()
    {
        var html = new StringBuilder("<ol>");
        for (int i = 0; i < 3000; i++) html.Append("<li>x</li>");
        html.Append("</ol>");

        Markers(html.ToString()).Last().Should().Be("3000.");
    }
}
