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
    /// <summary>Parse <paramref name="css"/> into a stylesheet; invalid syntax is skipped, never thrown.</summary>
    public static CssStyleSheet Parse(string? css)
    {
        var sheet = new CssStyleSheet();
        if (string.IsNullOrWhiteSpace(css)) return sheet;

        css = ScopeResolver.PreprocessScope(css);
        css = CssNestingResolver.PreprocessNesting(css);

        var tokenizer = new CssTokenizer(css);
        var tokens = ConsumeAllTokens(tokenizer);
        int pos = 0;
        var layerCounter = new int[1];

        while (pos < tokens.Count)
        {
            SkipWhitespace(tokens, ref pos);
            if (pos >= tokens.Count) break;

            var token = tokens[pos];

            if (token.Type == CssTokenType.AtKeyword)
            {
                ParseAtRule(sheet, tokens, ref pos, layerCounter);
                continue;
            }

            if (token.Type == CssTokenType.Ident || token.Type == CssTokenType.Hash ||
                token.Type == CssTokenType.Delim || token.Type == CssTokenType.Colon ||
                token.Type == CssTokenType.LeftBracket)
            {
                ParseStyleRule(sheet, tokens, ref pos);
                continue;
            }

            pos++;
        }

        return sheet;
    }

    private static void ParseAtRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos,
        int[]? layerCounter = null)
    {
        var keyword = tokens[pos].Value?.ToLowerInvariant() ?? "";
        pos++;
        SkipWhitespace(tokens, ref pos);

        switch (keyword)
        {
            case "import":
                ParseImportRule(sheet, tokens, ref pos);
                break;
            case "media":
                ParseMediaRule(sheet, tokens, ref pos);
                break;
            case "font-face":
                ParseFontFaceRule(sheet, tokens, ref pos);
                break;
            case "page":
                ParsePageRule(sheet, tokens, ref pos);
                break;
            case "supports":
                ParseSupportsRule(sheet, tokens, ref pos);
                break;
            case "layer":
                ParseLayerRule(sheet, tokens, ref pos, layerCounter);
                break;
            case "container":
                ParseContainerRule(sheet, tokens, ref pos);
                break;
            case "counter-style":
                ParseCounterStyleRule(sheet, tokens, ref pos);
                break;
            case "property":
                ParsePropertyRule(sheet, tokens, ref pos);
                break;
            default:
                SkipAtRule(tokens, ref pos);
                break;
        }
    }

    private static void ParseImportRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos)
    {
        string? url = null;

        if (pos >= tokens.Count) return;

        var token = tokens[pos];

        if (token.Type == CssTokenType.Url)
        {
            url = token.Value;
            pos++;
        }
        else if (token.Type == CssTokenType.Function &&
                 string.Equals(token.Value, "url", StringComparison.OrdinalIgnoreCase))
        {
            pos++;
            SkipWhitespace(tokens, ref pos);
            if (pos < tokens.Count && tokens[pos].Type == CssTokenType.String)
            {
                url = tokens[pos].Value;
                pos++;
            }
            while (pos < tokens.Count && tokens[pos].Type != CssTokenType.RightParen) pos++;
            if (pos < tokens.Count) pos++;
        }
        else if (token.Type == CssTokenType.String)
        {
            url = token.Value;
            pos++;
        }

        SkipWhitespace(tokens, ref pos);

        string? mediaQuery = null;
        if (pos < tokens.Count && tokens[pos].Type != CssTokenType.Semicolon)
        {
            var sb = new StringBuilder();
            while (pos < tokens.Count && tokens[pos].Type != CssTokenType.Semicolon)
            {
                if (tokens[pos].Type != CssTokenType.Whitespace || sb.Length > 0)
                    sb.Append(tokens[pos].Value ?? "");
                pos++;
            }
            var mq = sb.ToString().Trim();
            if (mq.Length > 0) mediaQuery = mq;
        }

        if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Semicolon) pos++;

        if (!string.IsNullOrEmpty(url))
        {
            sheet.ImportRules.Add(new CssImportRule { Url = url, MediaQuery = mediaQuery });
        }
    }

    private static void ParseMediaRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos)
    {
        var queryParts = new StringBuilder();
        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.LeftCurly)
        {
            if (tokens[pos].Type != CssTokenType.Whitespace || queryParts.Length > 0)
                queryParts.Append(tokens[pos].Value ?? "");
            pos++;
        }

        if (pos >= tokens.Count) return;
        pos++;

        var rule = new CssMediaRule { MediaQuery = queryParts.ToString().Trim() };

        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.RightCurly)
        {
            SkipWhitespace(tokens, ref pos);
            if (pos >= tokens.Count || tokens[pos].Type == CssTokenType.RightCurly) break;

            if (tokens[pos].Type == CssTokenType.AtKeyword)
            {
                var innerKeyword = tokens[pos].Value?.ToLowerInvariant() ?? "";
                pos++;
                SkipWhitespace(tokens, ref pos);
                if (innerKeyword == "page")
                    ParsePageRule(sheet, tokens, ref pos);
                else
                    SkipAtRule(tokens, ref pos);
                continue;
            }

            var innerRule = ParseSingleStyleRule(tokens, ref pos);
            if (innerRule != null)
                rule.Rules.Add(innerRule);
        }

        if (pos < tokens.Count) pos++;
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
        pos++;

        var rule = new CssFontFaceRule();
        ParseDeclarations(rule.Declarations, tokens, ref pos);

        if (pos < tokens.Count && tokens[pos].Type == CssTokenType.RightCurly) pos++;
        sheet.FontFaceRules.Add(rule);
    }

    private static void ParsePageRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos)
    {
        SkipWhitespace(tokens, ref pos);

        string? pageSelector = null;
        if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Ident)
        {
            pageSelector = tokens[pos].Value;
            pos++;
            SkipWhitespace(tokens, ref pos);
        }
        if (pageSelector != null && pos < tokens.Count && tokens[pos].Type == CssTokenType.Colon)
        {
            SkipAtRule(tokens, ref pos);
            return;
        }
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
        pos++;

        var rule = new CssPageRule { PageSelector = pageSelector };

        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.RightCurly)
        {
            SkipWhitespace(tokens, ref pos);
            if (pos >= tokens.Count || tokens[pos].Type == CssTokenType.RightCurly) break;

            if (tokens[pos].Type == CssTokenType.AtKeyword)
            {
                string mbPosition = tokens[pos].Value?.ToLowerInvariant() ?? "";
                pos++;
                SkipWhitespace(tokens, ref pos);

                if (pos < tokens.Count && tokens[pos].Type == CssTokenType.LeftCurly)
                {
                    pos++;
                    var mb = new CssPageMarginBox { Position = mbPosition };
                    ParseDeclarations(mb.Declarations, tokens, ref pos);
                    if (pos < tokens.Count && tokens[pos].Type == CssTokenType.RightCurly) pos++;
                    if (mb.Declarations.Count > 0)
                        rule.MarginBoxes.Add(mb);
                }
                else
                {
                    while (pos < tokens.Count && tokens[pos].Type != CssTokenType.Semicolon && tokens[pos].Type != CssTokenType.RightCurly) pos++;
                    if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Semicolon) pos++;
                }
            }
            else if (tokens[pos].Type == CssTokenType.Ident)
            {
                var property = tokens[pos].Value!.ToLowerInvariant();
                pos++;
                SkipWhitespace(tokens, ref pos);
                if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Colon)
                {
                    pos++;
                    SkipWhitespace(tokens, ref pos);
                    var valueParts = new StringBuilder();
                    bool important = false;
                    bool hasControlCharacter = false;
                    while (pos < tokens.Count &&
                           tokens[pos].Type != CssTokenType.Semicolon &&
                           tokens[pos].Type != CssTokenType.RightCurly &&
                           tokens[pos].Type != CssTokenType.AtKeyword)
                    {
                        var t = tokens[pos];
                        hasControlCharacter |= DecodesToControlCharacter(t);
                        if (t.Type == CssTokenType.Whitespace) valueParts.Append(' ');
                        else if (t.Type == CssTokenType.Function) valueParts.Append(t.Value ?? "").Append('(');
                        else if (t.Type == CssTokenType.String) valueParts.Append(CssStringEscapes.Quote(t.Value ?? ""));
                        else valueParts.Append(t.Value ?? "");
                        pos++;
                    }
                    var value = hasControlCharacter ? "" : valueParts.ToString().Trim();
                    if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
                    {
                        important = true;
                        value = value.Substring(0, value.Length - 10).Trim();
                    }
                    if (!string.IsNullOrEmpty(value))
                        rule.Declarations.Add(new CssDeclaration(property, value, important));
                    if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Semicolon) pos++;
                }
                else
                {
                    while (pos < tokens.Count && tokens[pos].Type != CssTokenType.Semicolon && tokens[pos].Type != CssTokenType.RightCurly) pos++;
                    if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Semicolon) pos++;
                }
            }
            else
            {
                pos++;
            }
        }

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
        var selectorParts = new StringBuilder();
        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.LeftCurly)
        {
            var t = tokens[pos];
            if (t.Type == CssTokenType.Whitespace)
                selectorParts.Append(' ');
            else if (t.Type == CssTokenType.Hash)
                CssStringEscapes.AppendIdentifier(selectorParts.Append('#'), t.Value ?? "");
            else if (t.Type == CssTokenType.Ident)
                CssStringEscapes.AppendIdentifier(selectorParts, t.Value ?? "");
            else if (t.Type == CssTokenType.Function)
                selectorParts.Append(t.Value ?? "").Append('(');
            else if (t.Type == CssTokenType.String)
                selectorParts.Append(CssStringEscapes.Quote(t.Value ?? ""));
            else
                selectorParts.Append(t.Value ?? "");
            pos++;
        }

        if (pos >= tokens.Count) return null;
        pos++;

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

            if (tokens[pos].Type != CssTokenType.Ident)
            {
                while (pos < tokens.Count && tokens[pos].Type != CssTokenType.Semicolon && tokens[pos].Type != CssTokenType.RightCurly) pos++;
                if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Semicolon) pos++;
                continue;
            }

            var property = tokens[pos].Value!.ToLowerInvariant();
            pos++;
            SkipWhitespace(tokens, ref pos);

            if (pos >= tokens.Count || tokens[pos].Type != CssTokenType.Colon)
            {
                while (pos < tokens.Count && tokens[pos].Type != CssTokenType.Semicolon && tokens[pos].Type != CssTokenType.RightCurly) pos++;
                if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Semicolon) pos++;
                continue;
            }
            pos++;
            SkipWhitespace(tokens, ref pos);

            var valueParts = new StringBuilder();
            bool important = false;
            bool hasControlCharacter = false;
            while (pos < tokens.Count && tokens[pos].Type != CssTokenType.Semicolon && tokens[pos].Type != CssTokenType.RightCurly)
            {
                var t = tokens[pos];
                hasControlCharacter |= DecodesToControlCharacter(t);
                if (t.Type == CssTokenType.Whitespace)
                    valueParts.Append(' ');
                else if (t.Type == CssTokenType.Hash)
                    valueParts.Append('#').Append(t.Value ?? "");
                else if (t.Type == CssTokenType.Function)
                    valueParts.Append(t.Value ?? "").Append('(');
                else if (t.Type == CssTokenType.Dimension)
                    valueParts.Append(t.Value ?? "");
                else if (t.Type == CssTokenType.String)
                    valueParts.Append(CssStringEscapes.Quote(t.Value ?? ""));
                else
                    valueParts.Append(t.Value ?? "");
                pos++;
            }

            var value = hasControlCharacter ? "" : valueParts.ToString().Trim();

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

    private static void ParseSupportsRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos)
    {
        var condBuilder = new StringBuilder();
        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.LeftCurly)
        {
            condBuilder.Append(tokens[pos].Value ?? "");
            pos++;
        }
        if (pos >= tokens.Count) return;
        pos++;

        bool supported = EvaluateSupportsCondition(condBuilder.ToString().Trim());

        if (supported)
        {
            while (pos < tokens.Count && tokens[pos].Type != CssTokenType.RightCurly)
            {
                SkipWhitespace(tokens, ref pos);
                if (pos >= tokens.Count || tokens[pos].Type == CssTokenType.RightCurly) break;

                if (tokens[pos].Type == CssTokenType.Delim && tokens[pos].Value == "@")
                {
                    pos++;
                    ParseAtRule(sheet, tokens, ref pos);
                }
                else
                {
                    var rule = ParseSingleStyleRule(tokens, ref pos);
                    if (rule != null) sheet.Rules.Add(rule);
                }
            }
        }
        else
        {
            SkipBlock(tokens, ref pos);
            return;
        }

        if (pos < tokens.Count && tokens[pos].Type == CssTokenType.RightCurly) pos++;
    }

    private static void ParseCounterStyleRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos)
    {
        SkipWhitespace(tokens, ref pos);
        if (pos >= tokens.Count) return;

        var name = tokens[pos].Type == CssTokenType.Ident ? tokens[pos].Value ?? "" : "";
        if (string.IsNullOrEmpty(name)) { SkipAtRule(tokens, ref pos); return; }
        pos++;
        SkipWhitespace(tokens, ref pos);

        if (pos >= tokens.Count || tokens[pos].Type != CssTokenType.LeftCurly)
        { SkipAtRule(tokens, ref pos); return; }
        pos++;

        var rule = new CssCounterStyleRule { Name = name };

        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.RightCurly)
        {
            SkipWhitespace(tokens, ref pos);
            if (pos >= tokens.Count || tokens[pos].Type == CssTokenType.RightCurly) break;

            if (tokens[pos].Type != CssTokenType.Ident) { pos++; continue; }
            var propName = tokens[pos].Value?.ToLowerInvariant() ?? "";
            pos++;
            SkipWhitespace(tokens, ref pos);

            if (pos >= tokens.Count || tokens[pos].Type != CssTokenType.Colon) continue;
            pos++;
            SkipWhitespace(tokens, ref pos);

            var valueSb = new StringBuilder();
            while (pos < tokens.Count &&
                   tokens[pos].Type != CssTokenType.Semicolon &&
                   tokens[pos].Type != CssTokenType.RightCurly)
            {
                var token = tokens[pos];
                valueSb.Append(token.Type == CssTokenType.String ? CssStringEscapes.Quote(token.Value ?? "") : token.Value ?? "");
                pos++;
            }
            if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Semicolon) pos++;

            var value = valueSb.ToString().Trim();
            switch (propName)
            {
                case "system":
                    if (value.StartsWith("extends", StringComparison.OrdinalIgnoreCase))
                    {
                        rule.System = "extends";
                        var ext = value.Substring(7).Trim();
                        rule.Extends = ext;
                    }
                    else rule.System = value;
                    break;
                case "symbols":
                    ParseSymbolList(value, rule.Symbols);
                    break;
                case "suffix":
                    rule.Suffix = UnquoteString(value);
                    break;
                case "prefix":
                    rule.Prefix = UnquoteString(value);
                    break;
                case "negative":
                    rule.Negative = value;
                    break;
                case "fallback":
                    rule.Fallback = value;
                    break;
            }
        }
        if (pos < tokens.Count && tokens[pos].Type == CssTokenType.RightCurly) pos++;

        sheet.CounterStyleRules.Add(rule);
    }

    private static void ParseSymbolList(string value, List<string> symbols)
    {
        var remaining = value.Trim();
        while (remaining.Length > 0)
        {
            remaining = remaining.TrimStart();
            if (remaining.Length == 0) break;

            if (remaining[0] == '"' || remaining[0] == '\'')
            {
                int afterString = 0;
                symbols.Add(CssStringEscapes.ReadQuoted(remaining, ref afterString));
                remaining = remaining.Substring(afterString).TrimStart(',').TrimStart();
            }
            else
            {
                int end = 0;
                while (end < remaining.Length && remaining[end] != ' ' &&
                       remaining[end] != ',' && remaining[end] != '\t') end++;
                if (end > 0) symbols.Add(remaining.Substring(0, end));
                remaining = remaining.Substring(end).TrimStart(',').TrimStart();
            }
        }
    }

    private static string UnquoteString(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\''))
        {
            int pos = 0;
            return CssStringEscapes.ReadQuoted(value, ref pos);
        }
        return value;
    }

    private static void ParseContainerRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos)
    {
        var headerSb = new StringBuilder();
        while (pos < tokens.Count &&
               tokens[pos].Type != CssTokenType.LeftCurly &&
               tokens[pos].Type != CssTokenType.Semicolon)
        {
            headerSb.Append(tokens[pos].Value ?? "");
            if (tokens[pos].Type != CssTokenType.Whitespace) headerSb.Append(' ');
            pos++;
        }

        if (pos >= tokens.Count || tokens[pos].Type == CssTokenType.Semicolon) { if (pos < tokens.Count) pos++; return; }
        pos++;

        var header = headerSb.ToString().Trim();
        var containerRule = new CssContainerRule();

        int parenStart = header.IndexOf('(');
        int parenEnd = header.LastIndexOf(')');
        if (parenStart >= 0 && parenEnd > parenStart)
        {
            containerRule.ContainerName = header.Substring(0, parenStart).Trim();
            containerRule.Condition = header.Substring(parenStart, parenEnd - parenStart + 1).Trim();
        }
        else
        {
            containerRule.Condition = header;
        }

        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.RightCurly)
        {
            SkipWhitespace(tokens, ref pos);
            if (pos >= tokens.Count || tokens[pos].Type == CssTokenType.RightCurly) break;

            if (tokens[pos].Type == CssTokenType.Delim && tokens[pos].Value == "@")
            {
                pos++;
                ParseAtRule(sheet, tokens, ref pos);
            }
            else
            {
                var rule = ParseSingleStyleRule(tokens, ref pos);
                if (rule != null) containerRule.Rules.Add(rule);
            }
        }
        if (pos < tokens.Count && tokens[pos].Type == CssTokenType.RightCurly) pos++;

        sheet.ContainerRules.Add(containerRule);
    }

    private static void ParseLayerRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos,
        int[]? layerCounter = null)
    {
        while (pos < tokens.Count &&
               tokens[pos].Type != CssTokenType.LeftCurly &&
               tokens[pos].Type != CssTokenType.Semicolon)
            pos++;

        if (pos >= tokens.Count) return;
        if (tokens[pos].Type == CssTokenType.Semicolon) { pos++; return; }
        pos++;

        int thisLayerOrder = layerCounter != null ? layerCounter[0]++ : 0;

        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.RightCurly)
        {
            SkipWhitespace(tokens, ref pos);
            if (pos >= tokens.Count || tokens[pos].Type == CssTokenType.RightCurly) break;

            if (tokens[pos].Type == CssTokenType.Delim && tokens[pos].Value == "@")
            {
                pos++;
                ParseAtRule(sheet, tokens, ref pos, layerCounter);
            }
            else
            {
                var rule = ParseSingleStyleRule(tokens, ref pos);
                if (rule != null)
                {
                    rule.LayerOrder = thisLayerOrder;
                    sheet.Rules.Add(rule);
                }
            }
        }
        if (pos < tokens.Count && tokens[pos].Type == CssTokenType.RightCurly) pos++;
    }

    private static bool EvaluateSupportsCondition(string condition)
    {
        condition = condition.Trim();
        if (string.IsNullOrEmpty(condition)) return true;

        if (condition.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
            return !EvaluateSupportsCondition(condition.Substring(4).Trim());

        if (condition.StartsWith("not(", StringComparison.OrdinalIgnoreCase))
            return !EvaluateSupportsCondition(StripOuterParens(condition.Substring(3)));

        var andParts = SplitByKeyword(condition, " and ");
        if (andParts.Length > 1)
        {
            foreach (var part in andParts)
                if (!EvaluateSupportsCondition(part.Trim())) return false;
            return true;
        }

        var orParts = SplitByKeyword(condition, " or ");
        if (orParts.Length > 1)
        {
            foreach (var part in orParts)
                if (EvaluateSupportsCondition(part.Trim())) return true;
            return false;
        }

        condition = StripOuterParens(condition);

        int colon = condition.IndexOf(':');
        if (colon < 0) return true;

        var propName = condition.Substring(0, colon).Trim().ToLowerInvariant();

        if (propName.StartsWith("-webkit-", StringComparison.Ordinal) ||
            propName.StartsWith("-moz-", StringComparison.Ordinal) ||
            propName.StartsWith("-ms-", StringComparison.Ordinal) ||
            propName.StartsWith("-o-", StringComparison.Ordinal))
            return false;

        return true;
    }

    private static string StripOuterParens(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '(' && s[s.Length - 1] == ')')
            return s.Substring(1, s.Length - 2).Trim();
        return s;
    }

    private static string[] SplitByKeyword(string s, string keyword)
    {
        var parts = new System.Collections.Generic.List<string>();
        int depth = 0;
        int start = 0;
        int kLen = keyword.Length;

        for (int i = 0; i <= s.Length - kLen; i++)
        {
            char c = s[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (depth == 0 && string.Equals(s.Substring(i, kLen), keyword, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(s.Substring(start, i - start));
                start = i + kLen;
                i += kLen - 1;
            }
        }
        parts.Add(s.Substring(start));
        return parts.ToArray();
    }

    private static void SkipAtRule(List<CssToken> tokens, ref int pos)
    {
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

    private static bool DecodesToControlCharacter(CssToken token)
    {
        if (token.Type == CssTokenType.String || token.Type == CssTokenType.Whitespace || token.Value == null) return false;
        foreach (char c in token.Value)
            if (c < ' ' || c == (char)0x7F) return true;
        return false;
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

    private static void ParsePropertyRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos)
    {
        SkipWhitespace(tokens, ref pos);
        if (pos >= tokens.Count) return;

        if (tokens[pos].Type != CssTokenType.Ident && tokens[pos].Type != CssTokenType.Delim)
        { SkipAtRule(tokens, ref pos); return; }

        var nameSb = new StringBuilder();
        while (pos < tokens.Count &&
               tokens[pos].Type != CssTokenType.LeftCurly &&
               tokens[pos].Type != CssTokenType.Whitespace)
        {
            nameSb.Append(tokens[pos].Value ?? "");
            pos++;
        }
        string propertyName = nameSb.ToString().Trim();

        SkipWhitespace(tokens, ref pos);

        if (pos >= tokens.Count || tokens[pos].Type != CssTokenType.LeftCurly)
        { SkipAtRule(tokens, ref pos); return; }
        pos++;

        var rule = new CssPropertyRule { Name = propertyName };

        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.RightCurly)
        {
            SkipWhitespace(tokens, ref pos);
            if (pos >= tokens.Count || tokens[pos].Type == CssTokenType.RightCurly) break;

            if (tokens[pos].Type != CssTokenType.Ident) { pos++; continue; }
            var propName = tokens[pos].Value?.ToLowerInvariant() ?? "";
            pos++;
            SkipWhitespace(tokens, ref pos);

            if (pos >= tokens.Count || tokens[pos].Type != CssTokenType.Colon) continue;
            pos++;
            SkipWhitespace(tokens, ref pos);

            var valueSb = new StringBuilder();
            while (pos < tokens.Count &&
                   tokens[pos].Type != CssTokenType.Semicolon &&
                   tokens[pos].Type != CssTokenType.RightCurly)
            {
                valueSb.Append(tokens[pos].Value ?? "");
                pos++;
            }
            if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Semicolon) pos++;

            var value = valueSb.ToString().Trim();
            switch (propName)
            {
                case "syntax":
                    rule.Syntax = value;
                    break;
                case "inherits":
                    rule.Inherits = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "initial-value":
                    rule.InitialValue = value;
                    break;
            }
        }

        if (pos < tokens.Count && tokens[pos].Type == CssTokenType.RightCurly) pos++;

        if (!string.IsNullOrEmpty(propertyName))
            sheet.PropertyRules.Add(rule);
    }
}
