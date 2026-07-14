using EggPdf.Html;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Html;

public class HtmlTokenizerTests
{
    private static List<HtmlToken> Tokenize(string html)
    {
        var tokenizer = new HtmlTokenizer(html);
        var tokens = new List<HtmlToken>();
        HtmlToken token;
        while ((token = tokenizer.NextToken()).Type != HtmlTokenType.EndOfFile)
            tokens.Add(token);
        return tokens;
    }

    [Fact]
    public void EmptyInput_ReturnsNoTokens()
    {
        var tokens = Tokenize("");
        tokens.Should().BeEmpty();
    }

    [Theory]
    [InlineData("&middot;", "·")]
    [InlineData("&hellip;", "…")]
    [InlineData("&copy;", "©")]
    [InlineData("&reg;", "®")]
    [InlineData("&trade;", "™")]
    [InlineData("&ndash;", "–")]
    [InlineData("&mdash;", "—")]
    [InlineData("&bull;", "•")]
    [InlineData("&lsquo;", "‘")]
    [InlineData("&rsquo;", "’")]
    [InlineData("&ldquo;", "“")]
    [InlineData("&rdquo;", "”")]
    [InlineData("&deg;", "°")]
    [InlineData("&plusmn;", "±")]
    [InlineData("&laquo;", "«")]
    [InlineData("&raquo;", "»")]
    [InlineData("&sect;", "§")]
    [InlineData("&para;", "¶")]
    [InlineData("&euro;", "€")]
    [InlineData("&pound;", "£")]
    [InlineData("&yen;", "¥")]
    [InlineData("&cent;", "¢")]
    [InlineData("&eacute;", "é")]
    [InlineData("&egrave;", "è")]
    [InlineData("&agrave;", "à")]
    [InlineData("&uuml;", "ü")]
    [InlineData("&ouml;", "ö")]
    [InlineData("&ntilde;", "ñ")]
    [InlineData("&ccedil;", "ç")]
    [InlineData("&szlig;", "ß")]
    [InlineData("&times;", "×")]
    [InlineData("&divide;", "÷")]
    [InlineData("&frac12;", "½")]
    [InlineData("&iexcl;", "¡")]
    [InlineData("&iquest;", "¿")]
    [InlineData("&dagger;", "†")]
    [InlineData("&Dagger;", "‡")]
    [InlineData("&permil;", "‰")]
    [InlineData("&lsaquo;", "‹")]
    [InlineData("&rsaquo;", "›")]
    [InlineData("&oelig;", "œ")]
    [InlineData("&OElig;", "Œ")]
    [InlineData("&larr;", "←")]
    [InlineData("&rarr;", "→")]
    [InlineData("&uarr;", "↑")]
    [InlineData("&darr;", "↓")]
    [InlineData("&infin;", "∞")]
    [InlineData("&ne;", "≠")]
    [InlineData("&le;", "≤")]
    [InlineData("&ge;", "≥")]
    [InlineData("&minus;", "−")]
    [InlineData("&shy;", "­")]
    public void NamedEntity_DecodesToUnicodeCharacter(string entity, string expected)
    {
        var tokens = Tokenize($"a{entity}b");
        tokens.Should().HaveCount(1);
        tokens[0].Data.Should().Be($"a{expected}b");
    }

    [Fact]
    public void NamedEntity_LongestMatchWins()
    {
        // "&not" is a valid entity (¬), but "&notin;" must match the longer name (∉)
        var tokens = Tokenize("a&notin;b");
        tokens.Should().HaveCount(1);
        tokens[0].Data.Should().Be("a∉b");
    }

    [Fact]
    public void UnknownNamedEntity_EmitsAmpersandLiterally()
    {
        var tokens = Tokenize("a&bogusentity;b");
        tokens.Should().HaveCount(1);
        tokens[0].Data.Should().StartWith("a&");
    }

    [Fact]
    public void PlainText_ReturnsCharacterToken()
    {
        var tokens = Tokenize("hello");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(HtmlTokenType.Character);
        tokens[0].Data.Should().Be("hello");
    }

    [Fact]
    public void SimpleTag_ReturnsStartTag()
    {
        var tokens = Tokenize("<div>");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(HtmlTokenType.StartTag);
        tokens[0].TagName.Should().Be("div");
    }

    [Fact]
    public void EndTag_ReturnsEndTag()
    {
        var tokens = Tokenize("</div>");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(HtmlTokenType.EndTag);
        tokens[0].TagName.Should().Be("div");
    }

    [Fact]
    public void SelfClosingTag_Recognized()
    {
        var tokens = Tokenize("<br/>");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(HtmlTokenType.StartTag);
        tokens[0].TagName.Should().Be("br");
        tokens[0].SelfClosing.Should().BeTrue();
    }

    [Fact]
    public void TagWithAttributes_ParsedCorrectly()
    {
        var tokens = Tokenize("<a href=\"https://example.com\" class='link'>");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(HtmlTokenType.StartTag);
        tokens[0].TagName.Should().Be("a");
        tokens[0].Attributes.Should().HaveCount(2);
        tokens[0].Attributes[0].Name.Should().Be("href");
        tokens[0].Attributes[0].Value.Should().Be("https://example.com");
        tokens[0].Attributes[1].Name.Should().Be("class");
        tokens[0].Attributes[1].Value.Should().Be("link");
    }

    [Fact]
    public void UnquotedAttribute_ParsedCorrectly()
    {
        var tokens = Tokenize("<div id=main>");
        tokens[0].Attributes[0].Name.Should().Be("id");
        tokens[0].Attributes[0].Value.Should().Be("main");
    }

    [Fact]
    public void BooleanAttribute_EmptyValue()
    {
        var tokens = Tokenize("<input disabled>");
        tokens[0].Attributes[0].Name.Should().Be("disabled");
        tokens[0].Attributes[0].Value.Should().Be("");
    }

    [Fact]
    public void Comment_Parsed()
    {
        var tokens = Tokenize("<!-- hello -->");
        var comments = tokens.Where(t => t.Type == HtmlTokenType.Comment).ToList();
        comments.Should().HaveCount(1);
        comments[0].Data.Should().Be(" hello ");
    }

    [Fact]
    public void Doctype_Parsed()
    {
        var tokens = Tokenize("<!DOCTYPE html>");
        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(HtmlTokenType.Doctype);
        tokens[0].DoctypeName.Should().Be("html");
    }

    [Fact]
    public void MixedContent_AllTokenTypes()
    {
        var tokens = Tokenize("<!DOCTYPE html><html><body><h1>Hello</h1></body></html>");

        tokens.Should().Contain(t => t.Type == HtmlTokenType.Doctype);
        tokens.Should().Contain(t => t.Type == HtmlTokenType.StartTag && t.TagName == "html");
        tokens.Should().Contain(t => t.Type == HtmlTokenType.StartTag && t.TagName == "body");
        tokens.Should().Contain(t => t.Type == HtmlTokenType.StartTag && t.TagName == "h1");
        tokens.Should().Contain(t => t.Type == HtmlTokenType.Character && t.Data == "Hello");
        tokens.Should().Contain(t => t.Type == HtmlTokenType.EndTag && t.TagName == "h1");
    }

    [Fact]
    public void TagNameIsCaseInsensitive_LowercaseOutput()
    {
        var tokens = Tokenize("<DIV><SPAN></SPAN></DIV>");
        tokens[0].TagName.Should().Be("div");
        tokens[1].TagName.Should().Be("span");
    }

    [Fact]
    public void Entity_Amp_Decoded()
    {
        var tokens = Tokenize("A&amp;B");
        var text = string.Concat(tokens.Where(t => t.Type == HtmlTokenType.Character).Select(t => t.Data));
        text.Should().Be("A&B");
    }

    [Fact]
    public void Entity_Lt_Decoded()
    {
        var tokens = Tokenize("&lt;div&gt;");
        var text = string.Concat(tokens.Where(t => t.Type == HtmlTokenType.Character).Select(t => t.Data));
        text.Should().Be("<div>");
    }

    [Fact]
    public void Entity_Numeric_Decoded()
    {
        var tokens = Tokenize("&#65;&#x42;");
        var text = string.Concat(tokens.Where(t => t.Type == HtmlTokenType.Character).Select(t => t.Data));
        text.Should().Be("AB");
    }

    [Fact]
    public void MultipleAttributes_DuplicateKept()
    {
        // Per HTML5 spec, first attribute wins on duplicate names
        var tokens = Tokenize("<div class=\"a\" class=\"b\">");
        tokens[0].Attributes.Should().HaveCount(2);
        tokens[0].Attributes[0].Value.Should().Be("a");
    }

    [Fact]
    public void VoidElement_NoSelfClosingSlash()
    {
        var tokens = Tokenize("<br><hr><img src='x'>");
        tokens.Should().HaveCount(3);
        tokens.Should().OnlyContain(t => t.Type == HtmlTokenType.StartTag);
    }

    [Fact]
    public void StyleTag_ContentAsRawText()
    {
        // After <style>, tokenizer should enter raw text mode
        // For Phase 1 tokenizer, we just verify it doesn't crash
        var tokens = Tokenize("<style>body { color: red; }</style>");
        tokens.Should().NotBeEmpty();
        tokens.Should().Contain(t => t.Type == HtmlTokenType.StartTag && t.TagName == "style");
    }
}
