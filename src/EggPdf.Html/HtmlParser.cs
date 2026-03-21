using EggPdf.Html.Dom;

namespace EggPdf.Html;

/// <summary>
/// Parses an HTML string into a DOM tree.
/// Infallible: never throws on any input. Always produces a valid HtmlDocument.
/// </summary>
public static class HtmlParser
{
    /// <summary>
    /// Parse an HTML string into a DOM tree.
    /// </summary>
    /// <param name="html">The HTML string to parse. Can be null, empty, or malformed.</param>
    /// <returns>A valid HtmlDocument with at minimum html, head, and body elements.</returns>
    public static HtmlDocument Parse(string html)
    {
        var tokenizer = new HtmlTokenizer(html ?? "");
        var builder = new HtmlTreeBuilder();
        return builder.Build(tokenizer);
    }
}
