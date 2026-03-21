using EggPdf.Text;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class LineBreakingTests
{
    [Fact]
    public void ShortText_NoBreak()
    {
        var lines = SimpleLineBreaker.Break("Hello", 500, 16);
        lines.Should().HaveCount(1);
        lines[0].Should().Be("Hello");
    }

    [Fact]
    public void LongText_BreaksAtWordBoundary()
    {
        var lines = SimpleLineBreaker.Break(
            "The quick brown fox jumps over the lazy dog", 200, 16);

        lines.Should().HaveCountGreaterThan(1);
        // Each line should be a valid substring
        foreach (var line in lines)
            line.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void EmptyText_ReturnsEmpty()
    {
        var lines = SimpleLineBreaker.Break("", 500, 16);
        lines.Should().BeEmpty();
    }

    [Fact]
    public void SingleLongWord_NotBroken()
    {
        // By default, a single long word is not broken
        var lines = SimpleLineBreaker.Break("Supercalifragilisticexpialidocious", 100, 16);
        lines.Should().HaveCount(1);
    }

    [Fact]
    public void MultipleSpaces_Collapsed()
    {
        var lines = SimpleLineBreaker.Break("Hello    World", 500, 16);
        lines.Should().HaveCount(1);
        lines[0].Should().Be("Hello World");
    }

    [Fact]
    public void NewlinesInNormalMode_TreatedAsSpaces()
    {
        var lines = SimpleLineBreaker.Break("Hello\nWorld", 500, 16);
        lines.Should().HaveCount(1);
        lines[0].Should().Be("Hello World");
    }

    [Fact]
    public void PreserveMode_KeepsNewlines()
    {
        var lines = SimpleLineBreaker.Break("Hello\nWorld", 500, 16, preserveNewlines: true);
        lines.Should().HaveCount(2);
        lines[0].Should().Be("Hello");
        lines[1].Should().Be("World");
    }
}
