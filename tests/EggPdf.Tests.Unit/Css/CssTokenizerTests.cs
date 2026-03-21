using System.Linq;
using EggPdf.Css.Tokenizer;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class CssTokenizerTests
{
    private static List<CssToken> Tokenize(string css)
    {
        var tokenizer = new CssTokenizer(css);
        var tokens = new List<CssToken>();
        CssToken token;
        while ((token = tokenizer.NextToken()).Type != CssTokenType.EOF)
            tokens.Add(token);
        return tokens;
    }

    [Fact]
    public void EmptyInput_ReturnsNoTokens()
    {
        Tokenize("").Should().BeEmpty();
    }

    [Fact]
    public void Ident_Recognized()
    {
        var tokens = Tokenize("color");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Value.Should().Be("color");
    }

    [Fact]
    public void Number_Integer()
    {
        var tokens = Tokenize("42");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(CssTokenType.Number);
        tokens[0].NumericValue.Should().Be(42);
    }

    [Fact]
    public void Number_Float()
    {
        var tokens = Tokenize("3.14");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(CssTokenType.Number);
        tokens[0].NumericValue.Should().BeApproximately(3.14, 0.001);
    }

    [Fact]
    public void Number_Negative()
    {
        var tokens = Tokenize("-5");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(CssTokenType.Number);
        tokens[0].NumericValue.Should().Be(-5);
    }

    [Fact]
    public void Dimension_Px()
    {
        var tokens = Tokenize("16px");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(CssTokenType.Dimension);
        tokens[0].NumericValue.Should().Be(16);
        tokens[0].Unit.Should().Be("px");
    }

    [Fact]
    public void Dimension_Em()
    {
        var tokens = Tokenize("1.5em");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(CssTokenType.Dimension);
        tokens[0].NumericValue.Should().BeApproximately(1.5, 0.001);
        tokens[0].Unit.Should().Be("em");
    }

    [Fact]
    public void Percentage()
    {
        var tokens = Tokenize("50%");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(CssTokenType.Percentage);
        tokens[0].NumericValue.Should().Be(50);
    }

    [Fact]
    public void String_DoubleQuoted()
    {
        var tokens = Tokenize("\"hello world\"");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(CssTokenType.String);
        tokens[0].Value.Should().Be("hello world");
    }

    [Fact]
    public void String_SingleQuoted()
    {
        var tokens = Tokenize("'hello'");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(CssTokenType.String);
        tokens[0].Value.Should().Be("hello");
    }

    [Fact]
    public void Hash_Id()
    {
        var tokens = Tokenize("#main");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(CssTokenType.Hash);
        tokens[0].Value.Should().Be("main");
    }

    [Fact]
    public void Hash_Color()
    {
        var tokens = Tokenize("#ff0000");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(CssTokenType.Hash);
        tokens[0].Value.Should().Be("ff0000");
    }

    [Fact]
    public void AtKeyword()
    {
        var tokens = Tokenize("@media");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(CssTokenType.AtKeyword);
        tokens[0].Value.Should().Be("media");
    }

    [Fact]
    public void Function()
    {
        var tokens = Tokenize("rgb(");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(CssTokenType.Function);
        tokens[0].Value.Should().Be("rgb");
    }

    [Fact]
    public void Url_Unquoted()
    {
        var tokens = Tokenize("url(image.png)");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(CssTokenType.Url);
        tokens[0].Value.Should().Be("image.png");
    }

    [Fact]
    public void Delimiters()
    {
        var tokens = Tokenize("{ } : ; , > + ~");
        var types = tokens.Where(t => t.Type != CssTokenType.Whitespace).Select(t => t.Type).ToList();
        types.Should().Contain(CssTokenType.LeftCurly);
        types.Should().Contain(CssTokenType.RightCurly);
        types.Should().Contain(CssTokenType.Colon);
        types.Should().Contain(CssTokenType.Semicolon);
        types.Should().Contain(CssTokenType.Comma);
    }

    [Fact]
    public void Whitespace_Collapsed()
    {
        var tokens = Tokenize("  color  :  red  ");
        tokens.Should().Contain(t => t.Type == CssTokenType.Whitespace);
    }

    [Fact]
    public void Comment_Stripped()
    {
        var tokens = Tokenize("color /* this is a comment */ : red");
        tokens.Where(t => t.Value != null && t.Value.Contains("comment")).Should().BeEmpty();
        tokens.Should().Contain(t => t.Type == CssTokenType.Ident && t.Value == "color");
        tokens.Should().Contain(t => t.Type == CssTokenType.Ident && t.Value == "red");
    }

    [Fact]
    public void FullDeclaration_AllTokens()
    {
        var tokens = Tokenize("margin-top: 10px;");
        var nonWs = tokens.Where(t => t.Type != CssTokenType.Whitespace).ToList();

        nonWs[0].Type.Should().Be(CssTokenType.Ident);
        nonWs[0].Value.Should().Be("margin-top");
        nonWs[1].Type.Should().Be(CssTokenType.Colon);
        nonWs[2].Type.Should().Be(CssTokenType.Dimension);
        nonWs[2].NumericValue.Should().Be(10);
        nonWs[2].Unit.Should().Be("px");
        nonWs[3].Type.Should().Be(CssTokenType.Semicolon);
    }

    [Fact]
    public void SelectorTokens_Correct()
    {
        var tokens = Tokenize("div.container > p.highlight");
        var nonWs = tokens.Where(t => t.Type != CssTokenType.Whitespace).ToList();

        nonWs.Should().Contain(t => t.Type == CssTokenType.Ident && t.Value == "div");
        nonWs.Should().Contain(t => t.Type == CssTokenType.Delim && t.Value == ".");
        nonWs.Should().Contain(t => t.Type == CssTokenType.Ident && t.Value == "container");
        nonWs.Should().Contain(t => t.Type == CssTokenType.Delim && t.Value == ">");
    }

    [Fact]
    public void NeverThrows_OnAnyInput()
    {
        var inputs = new[] { "", "  ", "\"unclosed", "'unclosed", "/*unclosed", "@", "#", "url(", "123abc", "---" };
        foreach (var input in inputs)
        {
            var act = () => Tokenize(input);
            act.Should().NotThrow($"input '{input}' should not throw");
        }
    }
}
