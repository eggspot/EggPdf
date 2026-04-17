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

        // Pre-process @scope rules — flatten to regular rules before tokenizing
        css = ScopeResolver.PreprocessScope(css);

        var tokenizer = new CssTokenizer(css);
        var tokens = ConsumeAllTokens(tokenizer);
        int pos = 0;
        // layerCounter[0] tracks the next layer index; rules in layers get this value.
        // Unlayered rules keep int.MaxValue (assigned by CssStyleRule default).
        var layerCounter = new int[1]; // shared mutable counter via array reference

        while (pos < tokens.Count)
        {
            SkipWhitespace(tokens, ref pos);
            if (pos >= tokens.Count) break;

            var token = tokens[pos];

            // At-rule
            if (token.Type == CssTokenType.AtKeyword)
            {
                ParseAtRule(sheet, tokens, ref pos, layerCounter);
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
                // @layer: include rules with layer priority ordering
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
                // Skip unknown at-rule: consume until { } or ;
                SkipAtRule(tokens, ref pos);
                break;
        }
    }

    private static void ParseImportRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos)
    {
        // @import url("...") [media];
        // @import "...";
        // @import url(...);
        string? url = null;

        if (pos >= tokens.Count) return;

        var token = tokens[pos];

        // url("...") or url(...)
        if (token.Type == CssTokenType.Url)
        {
            url = token.Value;
            pos++;
        }
        else if (token.Type == CssTokenType.Function &&
                 string.Equals(token.Value, "url", StringComparison.OrdinalIgnoreCase))
        {
            pos++; // skip function token
            SkipWhitespace(tokens, ref pos);
            if (pos < tokens.Count && tokens[pos].Type == CssTokenType.String)
            {
                url = tokens[pos].Value;
                pos++;
            }
            // skip until closing paren
            while (pos < tokens.Count && tokens[pos].Type != CssTokenType.RightParen) pos++;
            if (pos < tokens.Count) pos++; // skip )
        }
        else if (token.Type == CssTokenType.String)
        {
            url = token.Value;
            pos++;
        }

        SkipWhitespace(tokens, ref pos);

        // Optional media query (everything until ;)
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

        // Skip the semicolon
        if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Semicolon) pos++;

        if (!string.IsNullOrEmpty(url))
        {
            sheet.ImportRules.Add(new CssImportRule { Url = url, MediaQuery = mediaQuery });
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

            // Handle nested at-rules inside @media (e.g. @page { margin: 5mm; })
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

        // Parse @page body: a mix of declarations and margin-box at-rules
        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.RightCurly)
        {
            SkipWhitespace(tokens, ref pos);
            if (pos >= tokens.Count || tokens[pos].Type == CssTokenType.RightCurly) break;

            if (tokens[pos].Type == CssTokenType.AtKeyword)
            {
                // Nested margin-box at-rule: @top-center { ... }, @bottom-left { ... }, etc.
                string mbPosition = tokens[pos].Value?.ToLowerInvariant() ?? "";
                pos++;
                SkipWhitespace(tokens, ref pos);

                if (pos < tokens.Count && tokens[pos].Type == CssTokenType.LeftCurly)
                {
                    pos++; // skip {
                    var mb = new CssPageMarginBox { Position = mbPosition };
                    ParseDeclarations(mb.Declarations, tokens, ref pos);
                    if (pos < tokens.Count && tokens[pos].Type == CssTokenType.RightCurly) pos++; // skip }
                    if (mb.Declarations.Count > 0)
                        rule.MarginBoxes.Add(mb);
                }
                else
                {
                    // No block — skip to ; or }
                    while (pos < tokens.Count && tokens[pos].Type != CssTokenType.Semicolon && tokens[pos].Type != CssTokenType.RightCurly) pos++;
                    if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Semicolon) pos++;
                }
            }
            else if (tokens[pos].Type == CssTokenType.Ident)
            {
                // Regular declaration: property: value;
                var property = tokens[pos].Value!.ToLowerInvariant();
                pos++;
                SkipWhitespace(tokens, ref pos);
                if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Colon)
                {
                    pos++; // skip :
                    SkipWhitespace(tokens, ref pos);
                    var valueParts = new StringBuilder();
                    bool important = false;
                    while (pos < tokens.Count &&
                           tokens[pos].Type != CssTokenType.Semicolon &&
                           tokens[pos].Type != CssTokenType.RightCurly &&
                           tokens[pos].Type != CssTokenType.AtKeyword)
                    {
                        var t = tokens[pos];
                        if (t.Type == CssTokenType.Whitespace) valueParts.Append(' ');
                        else if (t.Type == CssTokenType.Function) valueParts.Append(t.Value ?? "").Append('(');
                        else if (t.Type == CssTokenType.String) valueParts.Append('\'').Append(t.Value ?? "").Append('\'');
                        else valueParts.Append(t.Value ?? "");
                        pos++;
                    }
                    var value = valueParts.ToString().Trim();
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
                    // Malformed — skip to ; or }
                    while (pos < tokens.Count && tokens[pos].Type != CssTokenType.Semicolon && tokens[pos].Type != CssTokenType.RightCurly) pos++;
                    if (pos < tokens.Count && tokens[pos].Type == CssTokenType.Semicolon) pos++;
                }
            }
            else
            {
                // Unknown token — skip it to avoid infinite loop
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

    /// <summary>
    /// @supports (condition) { rules }
    /// Strategy: evaluate whether EggPdf supports the tested property.
    /// Unknown or vendor-prefixed properties → false.
    /// If condition evaluates true, include the nested rules; otherwise skip the block.
    /// </summary>
    private static void ParseSupportsRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos)
    {
        // Collect condition tokens up to the opening {
        var condBuilder = new StringBuilder();
        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.LeftCurly)
        {
            condBuilder.Append(tokens[pos].Value ?? "");
            pos++;
        }
        if (pos >= tokens.Count) return;
        pos++; // skip {

        bool supported = EvaluateSupportsCondition(condBuilder.ToString().Trim());

        if (supported)
        {
            // Parse nested rules into the sheet directly (like a transparent wrapper)
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

    /// <summary>
    /// @layer name { rules } — include rules unconditionally (cascade layers not tracked).
    /// </summary>
    private static void ParseCounterStyleRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos)
    {
        // @counter-style <name> { system: ...; symbols: ...; suffix: ...; prefix: ...; }
        SkipWhitespace(tokens, ref pos);
        if (pos >= tokens.Count) return;

        // Read the name
        var name = tokens[pos].Type == CssTokenType.Ident ? tokens[pos].Value ?? "" : "";
        if (string.IsNullOrEmpty(name)) { SkipAtRule(tokens, ref pos); return; }
        pos++;
        SkipWhitespace(tokens, ref pos);

        // Expect {
        if (pos >= tokens.Count || tokens[pos].Type != CssTokenType.LeftCurly)
        { SkipAtRule(tokens, ref pos); return; }
        pos++; // skip {

        var rule = new CssCounterStyleRule { Name = name };

        // Parse declarations inside the block
        while (pos < tokens.Count && tokens[pos].Type != CssTokenType.RightCurly)
        {
            SkipWhitespace(tokens, ref pos);
            if (pos >= tokens.Count || tokens[pos].Type == CssTokenType.RightCurly) break;

            // Read property name
            if (tokens[pos].Type != CssTokenType.Ident) { pos++; continue; }
            var propName = tokens[pos].Value?.ToLowerInvariant() ?? "";
            pos++;
            SkipWhitespace(tokens, ref pos);

            // Expect :
            if (pos >= tokens.Count || tokens[pos].Type != CssTokenType.Colon) continue;
            pos++;
            SkipWhitespace(tokens, ref pos);

            // Read value tokens until ; or }
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
                    // Parse space/comma-separated symbols; quoted strings are unquoted
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

        // Default suffix is ". " if not specified and system is not cyclic/symbolic
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
                char q = remaining[0];
                int end = remaining.IndexOf(q, 1);
                if (end < 0) end = remaining.Length - 1;
                symbols.Add(remaining.Substring(1, end - 1));
                remaining = remaining.Substring(end + 1).TrimStart(',').TrimStart();
            }
            else if (remaining[0] == '\\')
            {
                // Unicode escape like \1F44D
                int end = 1;
                while (end < remaining.Length && end < 7 &&
                       IsHexDigit(remaining[end])) end++;
                var hex = remaining.Substring(1, end - 1);
                if (hex.Length > 0 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out int cp))
                {
                    symbols.Add(char.ConvertFromUtf32(cp));
                }
                remaining = remaining.Substring(end).TrimStart(',').TrimStart();
            }
            else
            {
                // Identifier token
                int end = 0;
                while (end < remaining.Length && remaining[end] != ' ' &&
                       remaining[end] != ',' && remaining[end] != '\t') end++;
                if (end > 0) symbols.Add(remaining.Substring(0, end));
                remaining = remaining.Substring(end).TrimStart(',').TrimStart();
            }
        }
    }

    private static bool IsHexDigit(char c)
        => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static string UnquoteString(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0])
            return value.Substring(1, value.Length - 2);
        return value;
    }

    private static void ParseContainerRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos)
    {
        // @container [name] (condition) { rules }
        // Collect everything before the opening { as the header
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
        pos++; // skip {

        var header = headerSb.ToString().Trim();
        var containerRule = new CssContainerRule();

        // Parse header: may be "(condition)" or "name (condition)"
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

        // Parse nested rules
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
        // Skip layer name/order declaration until { or ;
        while (pos < tokens.Count &&
               tokens[pos].Type != CssTokenType.LeftCurly &&
               tokens[pos].Type != CssTokenType.Semicolon)
            pos++;

        if (pos >= tokens.Count) return;
        if (tokens[pos].Type == CssTokenType.Semicolon) { pos++; return; } // @layer name; declaration
        pos++; // skip {

        // Assign a layer order index to all rules inside this layer.
        // Unlayered rules default to int.MaxValue; layered rules get a smaller number.
        int thisLayerOrder = layerCounter != null ? layerCounter[0]++ : 0;

        // Parse nested rules and tag them with this layer's order
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

    /// <summary>
    /// Evaluate a @supports condition string.
    /// Returns true if EggPdf claims to support the tested property/value.
    /// Handles: (prop: val), not (...), (...) and (...), (...) or (...)
    /// </summary>
    private static bool EvaluateSupportsCondition(string condition)
    {
        condition = condition.Trim();
        if (string.IsNullOrEmpty(condition)) return true;

        // Handle "not (...)"
        if (condition.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
            return !EvaluateSupportsCondition(condition.Substring(4).Trim());

        // Handle "not(...)" without space
        if (condition.StartsWith("not(", StringComparison.OrdinalIgnoreCase))
            return !EvaluateSupportsCondition(StripOuterParens(condition.Substring(3)));

        // Split by top-level " and " / " or "
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

        // Strip outer parens
        condition = StripOuterParens(condition);

        // At this point, expect "(property: value)" or "property: value"
        int colon = condition.IndexOf(':');
        if (colon < 0) return true; // no colon — be permissive

        var propName = condition.Substring(0, colon).Trim().ToLowerInvariant();

        // Vendor-prefixed properties → false
        if (propName.StartsWith("-webkit-", StringComparison.Ordinal) ||
            propName.StartsWith("-moz-", StringComparison.Ordinal) ||
            propName.StartsWith("-ms-", StringComparison.Ordinal) ||
            propName.StartsWith("-o-", StringComparison.Ordinal))
            return false;

        // Known CSS properties → true (be permissive, assume we support standard props)
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

    /// <summary>
    /// Parse: @property --name { syntax: '...'; inherits: true|false; initial-value: ...; }
    /// </summary>
    private static void ParsePropertyRule(CssStyleSheet sheet, List<CssToken> tokens, ref int pos)
    {
        SkipWhitespace(tokens, ref pos);
        if (pos >= tokens.Count) return;

        // Property name must start with "--"
        if (tokens[pos].Type != CssTokenType.Ident && tokens[pos].Type != CssTokenType.Delim)
        { SkipAtRule(tokens, ref pos); return; }

        // Reconstruct the property name (may be tokenized as multiple tokens: "--" + ident)
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
        pos++; // skip {

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
            pos++; // skip :
            SkipWhitespace(tokens, ref pos);

            var valueSb = new StringBuilder();
            while (pos < tokens.Count &&
                   tokens[pos].Type != CssTokenType.Semicolon &&
                   tokens[pos].Type != CssTokenType.RightCurly)
            {
                var t = tokens[pos];
                if (t.Type == CssTokenType.String)
                    valueSb.Append(t.Value ?? ""); // strip quotes from syntax value
                else
                    valueSb.Append(t.Value ?? "");
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
