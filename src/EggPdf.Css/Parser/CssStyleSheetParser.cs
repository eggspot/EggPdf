using System;
using System.Collections.Generic;
using System.Text;
using EggPdf.Css.Tokenizer;

namespace EggPdf.Css.Parser;

/// <summary>
/// Parses CSS text into a structured stylesheet. Handles rules, at-rules, declarations.
/// Infallible: never throws. Invalid syntax is skipped.
/// </summary>
public static partial class CssStyleSheetParser
{
    private const string ImportantKeyword = "important";

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
        var state = new ParseState();
        var topLevel = new BlockContext(sheet.Rules, null);

        while (pos < tokens.Count)
        {
            SkipWhitespace(tokens, ref pos);
            if (pos >= tokens.Count) break;

            var token = tokens[pos];

            if (token.Type == CssTokenType.AtKeyword)
            {
                ParseAtRule(sheet, tokens, ref pos, state, topLevel);
                continue;
            }

            if (token.Type == CssTokenType.Ident || token.Type == CssTokenType.Hash ||
                token.Type == CssTokenType.Delim || token.Type == CssTokenType.Colon ||
                token.Type == CssTokenType.LeftBracket)
            {
                var rule = ParseSingleStyleRule(tokens, ref pos, state);
                if (rule != null) sheet.Rules.Add(rule);
                continue;
            }

            pos++;
        }

        return sheet;
    }

    private static void ParseAtRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos, ParseState state,
        BlockContext context)
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
                ParseMediaRule(sheet, tokens, ref pos, state, context);
                break;
            case "font-face":
                ParseFontFaceRule(sheet, tokens, ref pos);
                break;
            case "page":
                ParsePageRule(sheet, tokens, ref pos);
                break;
            case "supports":
                ParseSupportsRule(sheet, tokens, ref pos, state, context);
                break;
            case "layer":
                ParseLayerRule(sheet, tokens, ref pos, state, context);
                break;
            case "container":
                ParseContainerRule(sheet, tokens, ref pos, state);
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

    private static void ParseBlockContents(CssStyleSheet sheet, List<CssToken> tokens, ref int pos, ParseState state,
        BlockContext context)
    {
        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.RightCurly)
        {
            SkipWhitespace(tokens, ref pos);
            if (pos >= tokens.Count || tokens[pos].Type == CssTokenType.RightCurly) break;

            if (tokens[pos].Type == CssTokenType.AtKeyword)
            {
                ParseAtRule(sheet, tokens, ref pos, state, context);
                continue;
            }

            var rule = ParseSingleStyleRule(tokens, ref pos, state);
            if (rule != null) context.Rules.Add(rule);
        }
        if (pos < tokens.Count && tokens[pos].Type == CssTokenType.RightCurly) pos++;
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

        bool supported = true;
        var media = new StringBuilder();
        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.Semicolon)
        {
            var t = tokens[pos];
            if (media.Length == 0 && t.Type == CssTokenType.Whitespace)
            {
                pos++;
            }
            else if (media.Length == 0 && t.Type == CssTokenType.Ident &&
                     string.Equals(t.Value, "layer", StringComparison.OrdinalIgnoreCase))
            {
                pos++;
            }
            else if (media.Length == 0 && t.Type == CssTokenType.Function &&
                     string.Equals(t.Value, "layer", StringComparison.OrdinalIgnoreCase))
            {
                pos++;
                ReadUntilMatchingParen(tokens, ref pos);
            }
            else if (media.Length == 0 && t.Type == CssTokenType.Function &&
                     string.Equals(t.Value, "supports", StringComparison.OrdinalIgnoreCase))
            {
                pos++;
                supported &= EvaluateSupportsCondition(ReadUntilMatchingParen(tokens, ref pos));
            }
            else
            {
                AppendPreludeToken(media, t);
                pos++;
            }
        }

        if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Semicolon) pos++;

        var mediaQuery = media.ToString().Trim();
        if (!string.IsNullOrEmpty(url) && supported)
            sheet.ImportRules.Add(new CssImportRule { Url = url, MediaQuery = mediaQuery.Length > 0 ? mediaQuery : null });
    }

    private static string ReadUntilMatchingParen(List<CssToken> tokens, ref int pos)
    {
        var text = new StringBuilder();
        int depth = 1;
        while (pos < tokens.Count)
        {
            var t = tokens[pos];
            if (t.Type == CssTokenType.LeftParen || t.Type == CssTokenType.Function) depth++;
            else if (t.Type == CssTokenType.RightParen && --depth == 0)
            {
                pos++;
                break;
            }
            AppendPreludeToken(text, t);
            pos++;
        }
        return text.ToString().Trim();
    }

    private static void ParseMediaRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos, ParseState state,
        BlockContext context)
    {
        var queryParts = new StringBuilder();
        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.LeftCurly)
        {
            if (tokens[pos].Type != CssTokenType.Whitespace || queryParts.Length > 0)
                AppendPreludeToken(queryParts, tokens[pos]);
            pos++;
        }

        if (pos >= tokens.Count) return;
        pos++;

        var query = MediaQueryCombiner.Combine(context.MediaQuery, queryParts.ToString().Trim());
        var rule = new CssMediaRule { MediaQuery = query };
        sheet.MediaRules.Add(rule);
        ParseBlockContents(sheet, tokens, ref pos, state, new BlockContext(rule.Rules, query));
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
                    SkipToDeclarationEnd(tokens, ref pos);
                }
            }
            else if (tokens[pos].Type == CssTokenType.Ident)
            {
                var property = CssPropertyNames.Normalize(tokens[pos].Value!);
                pos++;
                SkipWhitespace(tokens, ref pos);
                if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Colon)
                {
                    pos++;
                    SkipWhitespace(tokens, ref pos);
                    var declaration = ReadDeclarationValue(property, tokens, ref pos, stopAtAtKeyword: true);
                    if (declaration != null) rule.Declarations.Add(declaration);
                }
                else
                {
                    SkipToDeclarationEnd(tokens, ref pos);
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

    private static CssStyleRule? ParseSingleStyleRule(List<CssToken> tokens, ref int pos, ParseState state)
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

        var rule = new CssStyleRule
        {
            SelectorText = selector,
            LayerOrder = state.CurrentLayerOrder,
            SourceIndex = state.NextSourceIndex()
        };
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
                SkipToDeclarationEnd(tokens, ref pos);
                continue;
            }

            var property = CssPropertyNames.Normalize(tokens[pos].Value!);
            pos++;
            SkipWhitespace(tokens, ref pos);

            if (pos >= tokens.Count || tokens[pos].Type != CssTokenType.Colon)
            {
                SkipToDeclarationEnd(tokens, ref pos);
                continue;
            }
            pos++;
            SkipWhitespace(tokens, ref pos);

            var declaration = ReadDeclarationValue(property, tokens, ref pos, stopAtAtKeyword: false);
            if (declaration != null) declarations.Add(declaration);
        }
    }

    private static CssDeclaration? ReadDeclarationValue(string property, List<CssToken> tokens, ref int pos,
        bool stopAtAtKeyword)
    {
        var valueParts = new StringBuilder();
        bool hasControlCharacter = false;
        while (pos < tokens.Count &&
               tokens[pos].Type != CssTokenType.Semicolon &&
               tokens[pos].Type != CssTokenType.RightCurly &&
               !(stopAtAtKeyword && tokens[pos].Type == CssTokenType.AtKeyword))
        {
            var t = tokens[pos];
            hasControlCharacter |= DecodesToControlCharacter(t);
            if (t.Type == CssTokenType.Whitespace)
                valueParts.Append(' ');
            else
                AppendPreludeToken(valueParts, t);
            pos++;
        }
        if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Semicolon) pos++;

        var value = hasControlCharacter ? "" : valueParts.ToString().Trim();
        bool important = StripImportant(ref value);
        return string.IsNullOrEmpty(value) ? null : new CssDeclaration(property, value, important);
    }

    internal static bool StripImportant(ref string value)
    {
        int bang = value.LastIndexOf('!');
        if (bang < 0) return false;
        var flag = value.Substring(bang + 1).Trim();
        if (!string.Equals(flag, ImportantKeyword, StringComparison.OrdinalIgnoreCase)) return false;
        value = value.Substring(0, bang).TrimEnd();
        return true;
    }

    private static void AppendPreludeToken(StringBuilder sb, CssToken t)
    {
        switch (t.Type)
        {
            case CssTokenType.Hash:
                sb.Append('#').Append(t.Value ?? "");
                break;
            case CssTokenType.Function:
                sb.Append(t.Value ?? "").Append('(');
                break;
            case CssTokenType.String:
                sb.Append(CssStringEscapes.Quote(t.Value ?? ""));
                break;
            case CssTokenType.Url:
                sb.Append("url(").Append(CssStringEscapes.Quote(t.Value ?? "")).Append(')');
                break;
            default:
                sb.Append(t.Value ?? "");
                break;
        }
    }

    private static void SkipToDeclarationEnd(List<CssToken> tokens, ref int pos)
    {
        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.Semicolon && tokens[pos].Type != CssTokenType.RightCurly) pos++;
        if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Semicolon) pos++;
    }

    private static void ParseSupportsRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos, ParseState state,
        BlockContext context)
    {
        var condBuilder = new StringBuilder();
        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.LeftCurly)
        {
            AppendPreludeToken(condBuilder, tokens[pos]);
            pos++;
        }
        if (pos >= tokens.Count) return;
        pos++;

        if (EvaluateSupportsCondition(condBuilder.ToString().Trim()))
            ParseBlockContents(sheet, tokens, ref pos, state, context);
        else
            SkipBlock(tokens, ref pos);
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

    private static void ParseContainerRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos, ParseState state)
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

        sheet.ContainerRules.Add(containerRule);
        ParseBlockContents(sheet, tokens, ref pos, state, new BlockContext(containerRule.Rules, null));
    }

    private static void ParseLayerRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos, ParseState state,
        BlockContext context)
    {
        var names = new List<string>();
        var current = new StringBuilder();
        while (pos < tokens.Count &&
               tokens[pos].Type != CssTokenType.LeftCurly &&
               tokens[pos].Type != CssTokenType.Semicolon)
        {
            var t = tokens[pos];
            if (t.Type == CssTokenType.Comma)
            {
                if (current.Length > 0) names.Add(current.ToString());
                current.Clear();
            }
            else if (t.Type != CssTokenType.Whitespace)
            {
                current.Append(t.Value ?? "");
            }
            pos++;
        }
        if (current.Length > 0) names.Add(current.ToString());

        if (pos >= tokens.Count) return;
        if (tokens[pos].Type == CssTokenType.Semicolon)
        {
            pos++;
            foreach (var name in names)
                state.LayerOrderOf(state.QualifiedLayerName(name));
            return;
        }
        pos++;

        var layerName = names.Count > 0 ? state.QualifiedLayerName(names[0]) : null;
        int outerOrder = state.CurrentLayerOrder;
        string? outerName = state.CurrentLayerName;
        state.CurrentLayerOrder = state.LayerOrderOf(layerName);
        state.CurrentLayerName = layerName;
        ParseBlockContents(sheet, tokens, ref pos, state, context);
        state.CurrentLayerOrder = outerOrder;
        state.CurrentLayerName = outerName;
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

        var inner = StripOuterParens(condition);
        if (inner.Length < condition.Length &&
            (inner.StartsWith("(", StringComparison.Ordinal) || inner.StartsWith("not", StringComparison.OrdinalIgnoreCase)))
            return EvaluateSupportsCondition(inner);
        condition = inner;

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
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        int kLen = keyword.Length;

        for (int i = 0; i <= s.Length - kLen; i++)
        {
            char c = s[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (depth == 0 && string.Compare(s, i, keyword, 0, kLen, StringComparison.OrdinalIgnoreCase) == 0)
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
