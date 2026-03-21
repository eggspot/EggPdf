using System.Linq;
using EggPdf.Css;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class CssInlineParserTests
{
    [Fact]
    public void Parse_SingleProperty_ReturnsDeclaration()
    {
        var decls = CssInlineParser.Parse("color: red");

        decls.Should().HaveCount(1);
        decls[0].Property.Should().Be("color");
        decls[0].Value.Should().Be("red");
    }

    [Fact]
    public void Parse_MultipleProperties_ReturnsAll()
    {
        var decls = CssInlineParser.Parse("color: red; font-size: 16px; margin: 10px");

        decls.Should().HaveCount(3);
        decls[0].Property.Should().Be("color");
        decls[0].Value.Should().Be("red");
        decls[1].Property.Should().Be("font-size");
        decls[1].Value.Should().Be("16px");
        decls[2].Property.Should().Be("margin");
        decls[2].Value.Should().Be("10px");
    }

    [Fact]
    public void Parse_WithWhitespace_TrimsValues()
    {
        var decls = CssInlineParser.Parse("  color :  blue  ;  font-weight :  bold  ");

        decls.Should().HaveCount(2);
        decls[0].Property.Should().Be("color");
        decls[0].Value.Should().Be("blue");
        decls[1].Property.Should().Be("font-weight");
        decls[1].Value.Should().Be("bold");
    }

    [Fact]
    public void Parse_Important_Detected()
    {
        var decls = CssInlineParser.Parse("color: red !important");

        decls.Should().HaveCount(1);
        decls[0].Property.Should().Be("color");
        decls[0].Value.Should().Be("red");
        decls[0].Important.Should().BeTrue();
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmpty()
    {
        var decls = CssInlineParser.Parse("");
        decls.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NullString_ReturnsEmpty()
    {
        var decls = CssInlineParser.Parse(null!);
        decls.Should().BeEmpty();
    }

    [Fact]
    public void Parse_InvalidSyntax_SkipsInvalid()
    {
        var decls = CssInlineParser.Parse("not-valid; color: red; :::; font-size: 12px");

        // Should still get the valid ones
        decls.Should().Contain(d => d.Property == "color");
        decls.Should().Contain(d => d.Property == "font-size");
    }

    [Fact]
    public void Parse_ValueWithSpaces_PreservesSpaces()
    {
        var decls = CssInlineParser.Parse("font-family: Arial, Helvetica, sans-serif");

        decls[0].Property.Should().Be("font-family");
        decls[0].Value.Should().Be("Arial, Helvetica, sans-serif");
    }

    [Fact]
    public void Parse_ValueWithUrl_PreservesUrl()
    {
        var decls = CssInlineParser.Parse("background-image: url('image.png')");

        decls[0].Property.Should().Be("background-image");
        decls[0].Value.Should().Be("url('image.png')");
    }

    [Fact]
    public void Parse_TrailingSemicolon_NoExtraDeclaration()
    {
        var decls = CssInlineParser.Parse("color: red;");

        decls.Should().HaveCount(1);
    }
}
