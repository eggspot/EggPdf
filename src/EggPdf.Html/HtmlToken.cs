using System.Collections.Generic;

namespace EggPdf.Html;

public enum HtmlTokenType
{
    StartTag,
    EndTag,
    Character,
    Comment,
    Doctype,
    EndOfFile
}

/// <summary>
/// Represents a token produced by the HTML tokenizer.
/// </summary>
public class HtmlToken
{
    public HtmlTokenType Type { get; set; }
    public string? TagName { get; set; }
    public string? Data { get; set; }
    public bool SelfClosing { get; set; }
    public List<HtmlAttribute> Attributes { get; set; } = new();

    // Doctype fields
    public string? DoctypeName { get; set; }
    public string? PublicId { get; set; }
    public string? SystemId { get; set; }

    public static HtmlToken Eof() => new() { Type = HtmlTokenType.EndOfFile };
}

public class HtmlAttribute
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}
