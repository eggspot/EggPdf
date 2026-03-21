using System;
using System.Globalization;
using System.Text;

namespace EggPdf.Css.Tokenizer;

/// <summary>
/// CSS Syntax Level 3 tokenizer. Converts CSS text to a token stream.
/// Infallible: never throws on any input.
/// </summary>
public class CssTokenizer
{
    private readonly string _input;
    private int _pos;

    public CssTokenizer(string input)
    {
        _input = input ?? "";
    }

    public CssToken NextToken()
    {
        // Skip comments
        SkipComments();

        if (_pos >= _input.Length)
            return CssToken.Eof();

        char c = _input[_pos];

        // Whitespace
        if (IsWhitespace(c))
        {
            while (_pos < _input.Length && IsWhitespace(_input[_pos])) _pos++;
            return new CssToken { Type = CssTokenType.Whitespace, Value = " " };
        }

        // String
        if (c == '"' || c == '\'')
            return ConsumeString(c);

        // Hash
        if (c == '#')
        {
            _pos++;
            var name = ConsumeIdent();
            return new CssToken { Type = CssTokenType.Hash, Value = name };
        }

        // At-keyword
        if (c == '@')
        {
            _pos++;
            var name = ConsumeIdent();
            return new CssToken { Type = CssTokenType.AtKeyword, Value = name };
        }

        // Number, dimension, percentage
        if (IsDigit(c) || (c == '.' && _pos + 1 < _input.Length && IsDigit(_input[_pos + 1])) ||
            ((c == '-' || c == '+') && _pos + 1 < _input.Length && (IsDigit(_input[_pos + 1]) || _input[_pos + 1] == '.')))
        {
            return ConsumeNumeric();
        }

        // Ident or function or url
        if (IsIdentStart(c) || c == '-')
        {
            return ConsumeIdentLike();
        }

        // Single-character tokens
        _pos++;
        return c switch
        {
            ':' => new CssToken { Type = CssTokenType.Colon, Value = ":" },
            ';' => new CssToken { Type = CssTokenType.Semicolon, Value = ";" },
            ',' => new CssToken { Type = CssTokenType.Comma, Value = "," },
            '{' => new CssToken { Type = CssTokenType.LeftCurly, Value = "{" },
            '}' => new CssToken { Type = CssTokenType.RightCurly, Value = "}" },
            '(' => new CssToken { Type = CssTokenType.LeftParen, Value = "(" },
            ')' => new CssToken { Type = CssTokenType.RightParen, Value = ")" },
            '[' => new CssToken { Type = CssTokenType.LeftBracket, Value = "[" },
            ']' => new CssToken { Type = CssTokenType.RightBracket, Value = "]" },
            _ => new CssToken { Type = CssTokenType.Delim, Value = c.ToString() }
        };
    }

    private void SkipComments()
    {
        while (_pos + 1 < _input.Length && _input[_pos] == '/' && _input[_pos + 1] == '*')
        {
            _pos += 2;
            while (_pos + 1 < _input.Length)
            {
                if (_input[_pos] == '*' && _input[_pos + 1] == '/')
                {
                    _pos += 2;
                    break;
                }
                _pos++;
            }
            if (_pos >= _input.Length) break;
        }
    }

    private CssToken ConsumeString(char quote)
    {
        _pos++; // skip opening quote
        var sb = new StringBuilder();
        while (_pos < _input.Length)
        {
            char c = _input[_pos];
            if (c == quote) { _pos++; break; }
            if (c == '\\' && _pos + 1 < _input.Length)
            {
                _pos++;
                sb.Append(_input[_pos]);
                _pos++;
                continue;
            }
            if (c == '\n') break; // bad string
            sb.Append(c);
            _pos++;
        }
        return new CssToken { Type = CssTokenType.String, Value = sb.ToString() };
    }

    private CssToken ConsumeNumeric()
    {
        var numStr = new StringBuilder();

        // Optional sign
        if (_pos < _input.Length && (_input[_pos] == '-' || _input[_pos] == '+'))
        {
            numStr.Append(_input[_pos]);
            _pos++;
        }

        // Integer part
        while (_pos < _input.Length && IsDigit(_input[_pos]))
        {
            numStr.Append(_input[_pos]);
            _pos++;
        }

        // Decimal part
        if (_pos + 1 < _input.Length && _input[_pos] == '.' && IsDigit(_input[_pos + 1]))
        {
            numStr.Append('.');
            _pos++;
            while (_pos < _input.Length && IsDigit(_input[_pos]))
            {
                numStr.Append(_input[_pos]);
                _pos++;
            }
        }

        double numValue = 0;
        double.TryParse(numStr.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out numValue);

        // Check for % (percentage)
        if (_pos < _input.Length && _input[_pos] == '%')
        {
            _pos++;
            return new CssToken { Type = CssTokenType.Percentage, NumericValue = numValue, Value = numStr.ToString() + "%" };
        }

        // Check for unit (dimension)
        if (_pos < _input.Length && (IsIdentStart(_input[_pos]) || _input[_pos] == '-'))
        {
            var unit = ConsumeIdent();
            return new CssToken { Type = CssTokenType.Dimension, NumericValue = numValue, Unit = unit, Value = numStr.ToString() + unit };
        }

        return new CssToken { Type = CssTokenType.Number, NumericValue = numValue, Value = numStr.ToString() };
    }

    private CssToken ConsumeIdentLike()
    {
        var name = ConsumeIdent();

        // Check for function: ident followed by (
        if (_pos < _input.Length && _input[_pos] == '(')
        {
            _pos++; // consume (

            // Special case: url(
            if (string.Equals(name, "url", StringComparison.OrdinalIgnoreCase))
            {
                return ConsumeUrl();
            }

            return new CssToken { Type = CssTokenType.Function, Value = name };
        }

        return new CssToken { Type = CssTokenType.Ident, Value = name };
    }

    private CssToken ConsumeUrl()
    {
        // Skip whitespace
        while (_pos < _input.Length && IsWhitespace(_input[_pos])) _pos++;

        if (_pos >= _input.Length)
            return new CssToken { Type = CssTokenType.Url, Value = "" };

        // Quoted URL
        if (_input[_pos] == '"' || _input[_pos] == '\'')
        {
            var str = ConsumeString(_input[_pos]);
            // Skip to )
            while (_pos < _input.Length && _input[_pos] != ')') _pos++;
            if (_pos < _input.Length) _pos++; // skip )
            return new CssToken { Type = CssTokenType.Url, Value = str.Value };
        }

        // Unquoted URL
        var sb = new StringBuilder();
        while (_pos < _input.Length && _input[_pos] != ')' && !IsWhitespace(_input[_pos]))
        {
            sb.Append(_input[_pos]);
            _pos++;
        }
        // Skip whitespace and )
        while (_pos < _input.Length && IsWhitespace(_input[_pos])) _pos++;
        if (_pos < _input.Length && _input[_pos] == ')') _pos++;

        return new CssToken { Type = CssTokenType.Url, Value = sb.ToString() };
    }

    private string ConsumeIdent()
    {
        var sb = new StringBuilder();
        while (_pos < _input.Length && IsIdentChar(_input[_pos]))
        {
            sb.Append(_input[_pos]);
            _pos++;
        }
        return sb.ToString();
    }

    private static bool IsWhitespace(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f';
    private static bool IsDigit(char c) => c >= '0' && c <= '9';
    private static bool IsIdentStart(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || c > 127;
    private static bool IsIdentChar(char c) => IsIdentStart(c) || IsDigit(c) || c == '-';
}
