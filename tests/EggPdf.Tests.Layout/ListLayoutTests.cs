using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class ListLayoutTests
{
    [Fact]
    public void UnorderedList_GeneratesDiscMarkers()
    {
        var root = LayoutTestHelper.Layout("<ul><li>Item 1</li><li>Item 2</li></ul>", 600, 800);

        var lis = root.FindAllByTag("li");
        lis.Should().HaveCount(2);

        // Each li should have a marker child
        foreach (var li in lis)
        {
            var marker = li.Children.FirstOrDefault(c => c.IsListMarker);
            marker.Should().NotBeNull("each li should have a list marker");
            marker!.Text.Should().Be("\u2022", "unordered list should use bullet marker");
        }
    }

    [Fact]
    public void OrderedList_GeneratesDecimalMarkers()
    {
        var root = LayoutTestHelper.Layout("<ol><li>First</li><li>Second</li><li>Third</li></ol>", 600, 800);

        var lis = root.FindAllByTag("li");
        lis.Should().HaveCount(3);

        var markers = lis.Select(li => li.Children.FirstOrDefault(c => c.IsListMarker)).ToList();
        markers[0]!.Text.Should().Be("1.");
        markers[1]!.Text.Should().Be("2.");
        markers[2]!.Text.Should().Be("3.");
    }

    [Fact]
    public void ListMarker_PositionedLeftOfContent()
    {
        var root = LayoutTestHelper.Layout("<ul><li>Content</li></ul>", 600, 800);

        var li = root.FindByTag("li");
        li.Should().NotBeNull();

        var marker = li!.Children.FirstOrDefault(c => c.IsListMarker);
        marker.Should().NotBeNull();
        marker!.X.Should().BeLessThan(li.X, "marker should be positioned left of the list item");
    }

    [Fact]
    public void ListStyleTypeNone_NoMarkers()
    {
        var root = LayoutTestHelper.Layout(
            "<ul style='list-style-type: none'><li>Item</li></ul>", 600, 800);

        var li = root.FindByTag("li");
        li.Should().NotBeNull();

        var marker = li!.Children.FirstOrDefault(c => c.IsListMarker);
        marker.Should().BeNull("list-style-type:none should suppress markers");
    }

    [Fact]
    public void ListStyleTypeCircle_UsesCircleMarker()
    {
        var root = LayoutTestHelper.Layout(
            "<ul style='list-style-type: circle'><li>Item</li></ul>", 600, 800);

        var li = root.FindByTag("li");
        var marker = li!.Children.FirstOrDefault(c => c.IsListMarker);
        marker.Should().NotBeNull();
        marker!.Text.Should().Be("o");
    }

    [Fact]
    public void NestedList_IndentsCorrectly()
    {
        var root = LayoutTestHelper.Layout(
            "<ul><li>Outer<ul><li>Inner</li></ul></li></ul>", 600, 800);

        var lis = root.FindAllByTag("li");
        lis.Count.Should().BeGreaterOrEqualTo(2);

        // Inner li should be indented more than outer
        var outer = lis.First();
        var inner = lis.Last();
        inner.X.Should().BeGreaterThan(outer.X, "nested list should be indented further");
    }

    [Fact]
    public void OrderedList_LowerAlpha_UsesLetters()
    {
        var root = LayoutTestHelper.Layout(
            "<ol style='list-style-type: lower-alpha'><li>A</li><li>B</li><li>C</li></ol>", 600, 800);

        var lis = root.FindAllByTag("li");
        var markers = lis.Select(li => li.Children.FirstOrDefault(c => c.IsListMarker)).ToList();
        markers[0]!.Text.Should().Be("a.");
        markers[1]!.Text.Should().Be("b.");
        markers[2]!.Text.Should().Be("c.");
    }
}
