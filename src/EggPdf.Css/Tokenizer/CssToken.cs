namespace EggPdf.Css.Tokenizer;

public enum CssTokenType
{
    Ident,
    Function,
    AtKeyword,
    Hash,
    String,
    Url,
    Number,
    Percentage,
    Dimension,
    Whitespace,
    Colon,
    Semicolon,
    Comma,
    LeftBracket,
    RightBracket,
    LeftParen,
    RightParen,
    LeftCurly,
    RightCurly,
    Delim,
    EOF
}

/// <summary>A CSS token per CSS Syntax Level 3.</summary>
public class CssToken
{
    public CssTokenType Type { get; set; }
    public string? Value { get; set; }
    public double NumericValue { get; set; }
    public string? Unit { get; set; }

    public static CssToken Eof() => new() { Type = CssTokenType.EOF };
}
