using System;
using System.Collections.Generic;
using System.Text;
using EggPdf.Css.Tokenizer;

namespace EggPdf.Css.Parser;

/// <summary>
/// Parses CSS text into a structured stylesheet. Handles rules, at-rules, declarations.
/// Infallible: never throws. Invalid syntax is skipped.
/// </summary>
public static class CssStyleSheetParser
{
    public static CssStyleSheet Parse(string? css)
    {
        var sheet = new CssStyleSheet();
        if (string.IsNullOrWhiteSpace(css)) return sheet;

        var tokenizer = new CssTokenizer(css);
        var tokens = ConsumeAllTokens(tokenizer);
        int pos = 0;

        while (pos < tokens.Count)
        {
            SkipWhitespace(tokens, ref pos);
            if (pos >= tokens.Count) break;

            var token = tokens[pos];

            // At-rule
            if (token.Type == CssTokenType.AtKeyword)
            {
                ParseAtRule(sheet, tokens, ref pos);
                continue;
            }

            // Style rule
            if (token.Type == CssTokenType.Ident || token.Type == CssTokenType.Hash ||
                token.Type == CssTokenType.Delim || token.Type == CssTokenType.Colon ||
                token.Type == CssTokenType.LeftBracket)
            {
                ParseStyleRule(sheet, tokens, ref pos);
                continue;
            }

            // Skip unexpected tokens
            pos++;
        }

        return sheet;
    }

    private static void ParseAtRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos)
    {
        var keyword = tokens[pos].Value?.ToLowerInvariant() ?? "";
        pos++;
        SkipWhitespace(tokens, ref pos);

        switch (keyword)
        {
            case "media":
                ParseMediaRule(sheet, tokens, ref pos);
                break;
            case "font-face":
                ParseFontFaceRule(sheet, tokens, ref pos);
                break;
            case "page":
                ParsePageRule(sheet, tokens, ref pos);
                break;
            default:
                // Skip unknown at-rule: consume until { } or ;
                SkipAtRule(tokens, ref pos);
                break;
        }
    }

    private static void ParseMediaRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos)
    {
        // Consume media query (everything until {)
        var queryParts = new StringBuilder();
        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.LeftCurly)
        {
            if (tokens[pos].Type != CssTokenType.Whitespace || queryParts.Length > 0)
                queryParts.Append(tokens[pos].Value ?? "");
            pos++;
        }

        if (pos >= tokens.Count) return;
        pos++; // skip {

        var rule = new CssMediaRule { MediaQuery = queryParts.ToString().Trim() };

        // Parse inner rules until }
        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.RightCurly)
        {
            SkipWhitespace(tokens, ref pos);
            if (pos >= tokens.Count || tokens[pos].Type == CssTokenType.RightCurly) break;

            var innerRule = ParseSingleStyleRule(tokens, ref pos);
            if (innerRule != null)
                rule.Rules.Add(innerRule);
        }

        if (pos < tokens.Count) pos++; // skip }
        sheet.MediaRules.Add(rule);
    }

    private static void ParseFontFaceRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos)
    {
        SkipWhitespace(tokens, ref pos);
        if (pos >= tokens.Count || tokens[pos].Type != CssTokenType.LeftCurly)
        {
            SkipAtRule(tokens, ref pos);
            return;
        }
        pos++; // skip {

        var rule = new CssFontFaceRule();
        ParseDeclarations(rule.Declarations, tokens, ref pos);

        if (pos < tokens.Count && tokens[pos].Type == CssTokenType.RightCurly) pos++;
        sheet.FontFaceRules.Add(rule);
    }

    private static void ParsePageRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos)
    {
        SkipWhitespace(tokens, ref pos);

        string? pageSelector = null;
        // Optional page selector (e.g., :first, :left)
        if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Colon)
        {
            var sb = new StringBuilder(":");
            pos++;
            while (pos < tokens.Count && tokens[pos].Type == CssTokenType.Ident)
            {
                sb.Append(tokens[pos].Value);
                pos++;
            }
            pageSelector = sb.ToString();
            SkipWhitespace(tokens, ref pos);
        }

        if (pos >= tokens.Count || tokens[pos].Type != CssTokenType.LeftCurly)
        {
            SkipAtRule(tokens, ref pos);
            return;
        }
        pos++; // skip {

        var rule = new CssPageRule { PageSelector = pageSelector };
        ParseDeclarations(rule.Declarations, tokens, ref pos);

        if (pos < tokens.Count && tokens[pos].Type == CssTokenType.RightCurly) pos++;
        sheet.PageRules.Add(rule);
    }

    private static void ParseStyleRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos)
    {
        var rule = ParseSingleStyleRule(tokens, ref pos);
        if (rule != null)
            sheet.Rules.Add(rule);
    }

    private static CssStyleRule? ParseSingleStyleRule(List<CssToken> tokens, ref int pos)
    {
        // Consume selector (everything until {)
        var selectorParts = new StringBuilder();
        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.LeftCurly)
        {
            var t = tokens[pos];
            if (t.Type == CssTokenType.Whitespace)
                selectorParts.Append(' ');
            else if (t.Type == CssTokenType.Hash)
                selectorParts.Append('#').Append(t.Value ?? "");
            else if (t.Type == CssTokenType.Function)
                selectorParts.Append(t.Value ?? "").Append('(');
            else
                selectorParts.Append(t.Value ?? "");
            pos++;
        }

        if (pos >= tokens.Count) return null;
        pos++; // skip {

        var selector = selectorParts.ToString().Trim();
        if (string.IsNullOrEmpty(selector)) { SkipBlock(tokens, ref pos); return null; }

        var rule = new CssStyleRule { SelectorText = selector };
        ParseDeclarations(rule.Declarations, tokens, ref pos);

        if (pos < tokens.Count && tokens[pos].Type == CssTokenType.RightCurly) pos++;

        return rule;
    }

    private static void ParseDeclarations(List<CssDeclaration> declarations, List<CssToken> tokens, ref int pos)
    {
        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.RightCurly)
        {
            SkipWhitespace(tokens, ref pos);
            if (pos >= tokens.Count || tokens[pos].Type == CssTokenType.RightCurly) break;

            // Property name
            if (tokens[pos].Type != CssTokenType.Ident)
            {
                // Skip to next ; or }
                while (pos < tokens.Count && tokens[pos].Type != CssTokenType.Semicolon && tokens[pos].Type != CssTokenType.RightCurly) pos++;
                if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Semicolon) pos++;
                continue;
            }

            var property = tokens[pos].Value!.ToLowerInvariant();
            pos++;
            SkipWhitespace(tokens, ref pos);

            // Expect colon
            if (pos >= tokens.Count || tokens[pos].Type != CssTokenType.Colon)
            {
                while (pos < tokens.Count && tokens[pos].Type != CssTokenType.Semicolon && tokens[pos].Type != CssTokenType.RightCurly) pos++;
                if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Semicolon) pos++;
                continue;
            }
            pos++; // skip colon
            SkipWhitespace(tokens, ref pos);

            // Value: everything until ; or }
            var valueParts = new StringBuilder();
            bool important = false;
            while (pos < tokens.Count && tokens[pos].Type != CssTokenType.Semicolon && tokens[pos].Type != CssTokenType.RightCurly)
            {
                var t = tokens[pos];
                if (t.Type == CssTokenType.Whitespace)
                    valueParts.Append(' ');
                else if (t.Type == CssTokenType.Hash)
                    valueParts.Append('#').Append(t.Value ?? "");
                else if (t.Type == CssTokenType.Function)
                    valueParts.Append(t.Value ?? "").Append('(');
                else if (t.Type == CssTokenType.Dimension)
                    valueParts.Append(t.Value ?? "");
                else if (t.Type == CssTokenType.String)
                    valueParts.Append('\'').Append(t.Value ?? "").Append('\'');
                else
                    valueParts.Append(t.Value ?? "");
                pos++;
            }

            var value = valueParts.ToString().Trim();

            // Check !important
            if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
            {
                important = true;
                value = value.Substring(0, value.Length - 10).Trim();
                if (value.EndsWith("!")) value = value.Substring(0, value.Length - 1).Trim();
            }

            if (!string.IsNullOrEmpty(value))
                declarations.Add(new CssDeclaration(property, value, important));

            if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Semicolon) pos++;
        }
    }

    private static void SkipAtRule(List<CssToken> tokens, ref int pos)
    {
        // Skip until { } block or ;
        while (pos < tokens.Count)
        {
            if (tokens[pos].Type == CssTokenType.Semicolon) { pos++; return; }
            if (tokens[pos].Type == CssTokenType.LeftCurly) { pos++; SkipBlock(tokens, ref pos); return; }
            pos++;
        }
    }

    private static void SkipBlock(List<CssToken> tokens, ref int pos)
    {
        int depth = 1;
        while (pos < tokens.Count && depth > 0)
        {
            if (tokens[pos].Type == CssTokenType.LeftCurly) depth++;
            if (tokens[pos].Type == CssTokenType.RightCurly) depth--;
            pos++;
        }
    }

    private static void SkipWhitespace(List<CssToken> tokens, ref int pos)
    {
        while (pos < tokens.Count && tokens[pos].Type == CssTokenType.Whitespace) pos++;
    }

    private static List<CssToken> ConsumeAllTokens(CssTokenizer tokenizer)
    {
        var tokens = new List<CssToken>();
        CssToken token;
        while ((token = tokenizer.NextToken()).Type != CssTokenType.EOF)
            tokens.Add(token);
        return tokens;
    }
}
