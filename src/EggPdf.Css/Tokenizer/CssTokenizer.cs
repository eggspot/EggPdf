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
    private const string CdoText = "<!--";
    private const string CdcText = "-->";

    private readonly string _input;
    private int _pos;

    public CssTokenizer(string input)
    {
        _input = input ?? "";
    }

    public CssToken NextToken()
    {
        SkipComments();

        if (_pos >= _input.Length)
            return CssToken.Eof();

        char c = _input[_pos];

        if (IsWhitespace(c))
        {
            while (_pos < _input.Length && IsWhitespace(_input[_pos])) _pos++;
            return new CssToken { Type = CssTokenType.Whitespace, Value = " " };
        }

        if (c == '"' || c == '\'')
            return ConsumeString(c);

        if (c == '#')
        {
            _pos++;
            if (_pos < _input.Length && (IsIdentChar(_input[_pos]) || StartsValidEscape(_pos)))
                return new CssToken { Type = CssTokenType.Hash, Value = ConsumeIdent() };
            return Delim('#');
        }

        if (c == '@')
        {
            _pos++;
            if (WouldStartIdentifier(_pos))
                return new CssToken { Type = CssTokenType.AtKeyword, Value = ConsumeIdent() };
            return Delim('@');
        }

        if (StartsNumber(_pos))
            return ConsumeNumeric();

        if (c == '<' && StartsWithAt(_pos, CdoText))
        {
            _pos += CdoText.Length;
            return new CssToken { Type = CssTokenType.CDO, Value = CdoText };
        }

        if (c == '-' && StartsWithAt(_pos, CdcText))
        {
            _pos += CdcText.Length;
            return new CssToken { Type = CssTokenType.CDC, Value = CdcText };
        }

        if (WouldStartIdentifier(_pos))
            return ConsumeIdentLike();

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
            _ => Delim(c)
        };
    }

    private static CssToken Delim(char c) => new() { Type = CssTokenType.Delim, Value = c.ToString() };

    private bool StartsWithAt(int pos, string text)
        => pos + text.Length <= _input.Length && string.CompareOrdinal(_input, pos, text, 0, text.Length) == 0;

    private void SkipComments()
    {
        while (_pos + 1 < _input.Length && _input[_pos] == '/' && _input[_pos + 1] == '*')
        {
            int end = _input.IndexOf("*/", _pos + 2, StringComparison.Ordinal);
            _pos = end < 0 ? _input.Length : end + 2;
        }
    }

    private CssToken ConsumeString(char quote)
    {
        _pos++;
        var sb = new StringBuilder();
        while (_pos < _input.Length)
        {
            char c = _input[_pos];
            if (c == quote) { _pos++; break; }
            if (c == '\\')
            {
                _pos++;
                CssStringEscapes.AppendEscape(_input, ref _pos, sb);
                continue;
            }
            if (c == '\n') break;
            sb.Append(c);
            _pos++;
        }
        return new CssToken { Type = CssTokenType.String, Value = sb.ToString() };
    }

    private CssToken ConsumeNumeric()
    {
        int start = _pos;

        if (_input[_pos] == '-' || _input[_pos] == '+')
            _pos++;

        while (_pos < _input.Length && IsDigit(_input[_pos]))
            _pos++;

        if (_pos + 1 < _input.Length && _input[_pos] == '.' && IsDigit(_input[_pos + 1]))
        {
            _pos++;
            while (_pos < _input.Length && IsDigit(_input[_pos]))
                _pos++;
        }

        if (StartsExponent(_pos))
        {
            _pos++;
            if (_input[_pos] == '-' || _input[_pos] == '+')
                _pos++;
            while (_pos < _input.Length && IsDigit(_input[_pos]))
                _pos++;
        }

        string number = _input.Substring(start, _pos - start);
        double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double numValue);

        if (_pos < _input.Length && _input[_pos] == '%')
        {
            _pos++;
            return new CssToken { Type = CssTokenType.Percentage, NumericValue = numValue, Value = number + "%" };
        }

        if (WouldStartIdentifier(_pos))
        {
            var unit = ConsumeIdent();
            return new CssToken { Type = CssTokenType.Dimension, NumericValue = numValue, Unit = unit, Value = number + unit };
        }

        return new CssToken { Type = CssTokenType.Number, NumericValue = numValue, Value = number };
    }

    /// <summary>An 'e' or 'E' followed by digits, optionally signed, continues a number as its exponent.</summary>
    private bool StartsExponent(int pos)
    {
        if (pos >= _input.Length || (_input[pos] != 'e' && _input[pos] != 'E'))
            return false;
        int next = pos + 1;
        if (next < _input.Length && (_input[next] == '-' || _input[next] == '+'))
            next++;
        return next < _input.Length && IsDigit(_input[next]);
    }

    private CssToken ConsumeIdentLike()
    {
        var name = ConsumeIdent();

        if (_pos < _input.Length && _input[_pos] == '(')
        {
            _pos++;
            if (string.Equals(name, "url", StringComparison.OrdinalIgnoreCase))
                return ConsumeUrl();
            return new CssToken { Type = CssTokenType.Function, Value = name };
        }

        return new CssToken { Type = CssTokenType.Ident, Value = name };
    }

    private CssToken ConsumeUrl()
    {
        while (_pos < _input.Length && IsWhitespace(_input[_pos])) _pos++;

        if (_pos >= _input.Length)
            return new CssToken { Type = CssTokenType.Url, Value = "" };

        if (_input[_pos] == '"' || _input[_pos] == '\'')
        {
            var str = ConsumeString(_input[_pos]);
            while (_pos < _input.Length && _input[_pos] != ')') _pos++;
            if (_pos < _input.Length) _pos++;
            return new CssToken { Type = CssTokenType.Url, Value = str.Value };
        }

        var sb = new StringBuilder();
        while (_pos < _input.Length && _input[_pos] != ')' && !IsWhitespace(_input[_pos]))
        {
            sb.Append(_input[_pos]);
            _pos++;
        }
        while (_pos < _input.Length && IsWhitespace(_input[_pos])) _pos++;
        if (_pos < _input.Length && _input[_pos] == ')') _pos++;

        return new CssToken { Type = CssTokenType.Url, Value = sb.ToString() };
    }

    private string ConsumeIdent()
    {
        var sb = new StringBuilder();
        while (_pos < _input.Length)
        {
            char c = _input[_pos];
            if (IsIdentChar(c))
            {
                sb.Append(c);
                _pos++;
            }
            else if (StartsValidEscape(_pos))
            {
                _pos++;
                CssStringEscapes.AppendEscape(_input, ref _pos, sb);
            }
            else
            {
                break;
            }
        }
        return sb.ToString();
    }

    /// <summary>CSS Syntax "check if three code points would start a number".</summary>
    private bool StartsNumber(int pos)
    {
        char c = _input[pos];
        if (c == '+' || c == '-')
        {
            if (pos + 1 >= _input.Length) return false;
            if (IsDigit(_input[pos + 1])) return true;
            return _input[pos + 1] == '.' && pos + 2 < _input.Length && IsDigit(_input[pos + 2]);
        }
        if (c == '.')
            return pos + 1 < _input.Length && IsDigit(_input[pos + 1]);
        return IsDigit(c);
    }

    /// <summary>CSS Syntax "check if three code points would start an ident sequence".</summary>
    private bool WouldStartIdentifier(int pos)
    {
        if (pos >= _input.Length) return false;
        char c = _input[pos];
        if (c == '-')
        {
            if (pos + 1 >= _input.Length) return false;
            char next = _input[pos + 1];
            return IsIdentStart(next) || next == '-' || StartsValidEscape(pos + 1);
        }
        return IsIdentStart(c) || StartsValidEscape(pos);
    }

    private bool StartsValidEscape(int pos) =>
        pos + 1 < _input.Length && _input[pos] == '\\' &&
        _input[pos + 1] != '\n' && _input[pos + 1] != '\r' && _input[pos + 1] != '\f';

    private static bool IsWhitespace(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f';
    private static bool IsDigit(char c) => c >= '0' && c <= '9';
    private static bool IsIdentStart(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || c > 127;
    private static bool IsIdentChar(char c) => IsIdentStart(c) || IsDigit(c) || c == '-';
}
