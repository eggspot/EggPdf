using System;
using System.Collections.Generic;
using System.Text;

namespace EggPdf.Html;

/// <summary>
/// HTML5 tokenizer. Converts an HTML string into a sequence of tokens.
/// Implements the core states of the WHATWG HTML tokenizer spec.
/// The tokenizer is infallible -- it never throws on any input.
/// </summary>
public class HtmlTokenizer
{
    private readonly string _input;
    private int _pos;
    private TokenizerState _state = TokenizerState.Data;
    private readonly StringBuilder _buffer = new();
    private HtmlToken? _currentToken;
    private HtmlAttribute? _currentAttribute;
    private readonly Queue<HtmlToken> _pendingTokens = new();

    public HtmlTokenizer(string input)
    {
        _input = input ?? "";
    }

    public HtmlToken NextToken()
    {
        if (_pendingTokens.Count > 0)
            return _pendingTokens.Dequeue();

        while (_pos <= _input.Length)
        {
            if (_pos == _input.Length)
            {
                // Emit any buffered text before EOF
                if (_buffer.Length > 0 && _state == TokenizerState.Data)
                {
                    var textToken = new HtmlToken { Type = HtmlTokenType.Character, Data = _buffer.ToString() };
                    _buffer.Clear();
                    _pendingTokens.Enqueue(HtmlToken.Eof());
                    return textToken;
                }
                _pos++;
                return HtmlToken.Eof();
            }

            char c = _input[_pos];

            switch (_state)
            {
                case TokenizerState.Data:
                    if (c == '<')
                    {
                        if (_buffer.Length > 0)
                        {
                            var textToken = new HtmlToken { Type = HtmlTokenType.Character, Data = _buffer.ToString() };
                            _buffer.Clear();
                            _state = TokenizerState.TagOpen;
                            _pos++;
                            return textToken;
                        }
                        _state = TokenizerState.TagOpen;
                        _pos++;
                    }
                    else if (c == '&')
                    {
                        _buffer.Append(ConsumeCharacterReference());
                    }
                    else
                    {
                        _buffer.Append(c);
                        _pos++;
                    }
                    break;

                case TokenizerState.TagOpen:
                    if (c == '!')
                    {
                        _pos++;
                        _state = TokenizerState.MarkupDeclarationOpen;
                    }
                    else if (c == '/')
                    {
                        _pos++;
                        _state = TokenizerState.EndTagOpen;
                    }
                    else if (char.IsLetter(c))
                    {
                        _currentToken = new HtmlToken { Type = HtmlTokenType.StartTag };
                        _buffer.Clear();
                        _state = TokenizerState.TagName;
                    }
                    else
                    {
                        // Invalid: emit '<' as text
                        _buffer.Append('<');
                        _state = TokenizerState.Data;
                    }
                    break;

                case TokenizerState.EndTagOpen:
                    if (char.IsLetter(c))
                    {
                        _currentToken = new HtmlToken { Type = HtmlTokenType.EndTag };
                        _buffer.Clear();
                        _state = TokenizerState.TagName;
                    }
                    else
                    {
                        _buffer.Append("</");
                        _state = TokenizerState.Data;
                    }
                    break;

                case TokenizerState.TagName:
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f')
                    {
                        _currentToken!.TagName = _buffer.ToString().ToLowerInvariant();
                        _buffer.Clear();
                        _pos++;
                        _state = TokenizerState.BeforeAttributeName;
                    }
                    else if (c == '/')
                    {
                        _currentToken!.TagName = _buffer.ToString().ToLowerInvariant();
                        _buffer.Clear();
                        _pos++;
                        _state = TokenizerState.SelfClosingStartTag;
                    }
                    else if (c == '>')
                    {
                        _currentToken!.TagName = _buffer.ToString().ToLowerInvariant();
                        _buffer.Clear();
                        _pos++;
                        _state = TokenizerState.Data;
                        return _currentToken;
                    }
                    else
                    {
                        _buffer.Append(c);
                        _pos++;
                    }
                    break;

                case TokenizerState.BeforeAttributeName:
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f')
                    {
                        _pos++;
                    }
                    else if (c == '/' || c == '>')
                    {
                        _state = TokenizerState.AfterAttributeName;
                    }
                    else
                    {
                        _currentAttribute = new HtmlAttribute();
                        _currentToken!.Attributes.Add(_currentAttribute);
                        _buffer.Clear();
                        _state = TokenizerState.AttributeName;
                    }
                    break;

                case TokenizerState.AttributeName:
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f')
                    {
                        _currentAttribute!.Name = _buffer.ToString().ToLowerInvariant();
                        _buffer.Clear();
                        _pos++;
                        _state = TokenizerState.AfterAttributeName;
                    }
                    else if (c == '=')
                    {
                        _currentAttribute!.Name = _buffer.ToString().ToLowerInvariant();
                        _buffer.Clear();
                        _pos++;
                        _state = TokenizerState.BeforeAttributeValue;
                    }
                    else if (c == '/' || c == '>')
                    {
                        _currentAttribute!.Name = _buffer.ToString().ToLowerInvariant();
                        _buffer.Clear();
                        _state = TokenizerState.AfterAttributeName;
                    }
                    else
                    {
                        _buffer.Append(c);
                        _pos++;
                    }
                    break;

                case TokenizerState.AfterAttributeName:
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f')
                    {
                        _pos++;
                    }
                    else if (c == '=')
                    {
                        _pos++;
                        _state = TokenizerState.BeforeAttributeValue;
                    }
                    else if (c == '>')
                    {
                        _pos++;
                        _state = TokenizerState.Data;
                        return _currentToken!;
                    }
                    else if (c == '/')
                    {
                        _pos++;
                        _state = TokenizerState.SelfClosingStartTag;
                    }
                    else
                    {
                        _currentAttribute = new HtmlAttribute();
                        _currentToken!.Attributes.Add(_currentAttribute);
                        _buffer.Clear();
                        _state = TokenizerState.AttributeName;
                    }
                    break;

                case TokenizerState.BeforeAttributeValue:
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f')
                    {
                        _pos++;
                    }
                    else if (c == '"')
                    {
                        _pos++;
                        _buffer.Clear();
                        _state = TokenizerState.AttributeValueDoubleQuoted;
                    }
                    else if (c == '\'')
                    {
                        _pos++;
                        _buffer.Clear();
                        _state = TokenizerState.AttributeValueSingleQuoted;
                    }
                    else if (c == '>')
                    {
                        _pos++;
                        _state = TokenizerState.Data;
                        return _currentToken!;
                    }
                    else
                    {
                        _buffer.Clear();
                        _state = TokenizerState.AttributeValueUnquoted;
                    }
                    break;

                case TokenizerState.AttributeValueDoubleQuoted:
                    if (c == '"')
                    {
                        _currentAttribute!.Value = _buffer.ToString();
                        _buffer.Clear();
                        _pos++;
                        _state = TokenizerState.AfterAttributeValueQuoted;
                    }
                    else if (c == '&')
                    {
                        _buffer.Append(ConsumeCharacterReference());
                    }
                    else
                    {
                        _buffer.Append(c);
                        _pos++;
                    }
                    break;

                case TokenizerState.AttributeValueSingleQuoted:
                    if (c == '\'')
                    {
                        _currentAttribute!.Value = _buffer.ToString();
                        _buffer.Clear();
                        _pos++;
                        _state = TokenizerState.AfterAttributeValueQuoted;
                    }
                    else if (c == '&')
                    {
                        _buffer.Append(ConsumeCharacterReference());
                    }
                    else
                    {
                        _buffer.Append(c);
                        _pos++;
                    }
                    break;

                case TokenizerState.AttributeValueUnquoted:
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f')
                    {
                        _currentAttribute!.Value = _buffer.ToString();
                        _buffer.Clear();
                        _pos++;
                        _state = TokenizerState.BeforeAttributeName;
                    }
                    else if (c == '>')
                    {
                        _currentAttribute!.Value = _buffer.ToString();
                        _buffer.Clear();
                        _pos++;
                        _state = TokenizerState.Data;
                        return _currentToken!;
                    }
                    else if (c == '&')
                    {
                        _buffer.Append(ConsumeCharacterReference());
                    }
                    else
                    {
                        _buffer.Append(c);
                        _pos++;
                    }
                    break;

                case TokenizerState.AfterAttributeValueQuoted:
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f')
                    {
                        _pos++;
                        _state = TokenizerState.BeforeAttributeName;
                    }
                    else if (c == '/')
                    {
                        _pos++;
                        _state = TokenizerState.SelfClosingStartTag;
                    }
                    else if (c == '>')
                    {
                        _pos++;
                        _state = TokenizerState.Data;
                        return _currentToken!;
                    }
                    else
                    {
                        _state = TokenizerState.BeforeAttributeName;
                    }
                    break;

                case TokenizerState.SelfClosingStartTag:
                    if (c == '>')
                    {
                        _currentToken!.SelfClosing = true;
                        _pos++;
                        _state = TokenizerState.Data;
                        return _currentToken;
                    }
                    else
                    {
                        _state = TokenizerState.BeforeAttributeName;
                    }
                    break;

                case TokenizerState.MarkupDeclarationOpen:
                    if (_pos + 1 < _input.Length && _input[_pos] == '-' && _input[_pos + 1] == '-')
                    {
                        _pos += 2;
                        _buffer.Clear();
                        _state = TokenizerState.Comment;
                    }
                    else if (_pos + 6 < _input.Length &&
                             _input.Substring(_pos, 7).Equals("DOCTYPE", StringComparison.OrdinalIgnoreCase))
                    {
                        _pos += 7;
                        _state = TokenizerState.Doctype;
                    }
                    else
                    {
                        // Bogus comment
                        _buffer.Clear();
                        _state = TokenizerState.BogusComment;
                    }
                    break;

                case TokenizerState.Comment:
                    if (c == '-' && _pos + 1 < _input.Length && _input[_pos + 1] == '-')
                    {
                        _pos += 2;
                        if (_pos < _input.Length && _input[_pos] == '>')
                        {
                            _pos++;
                            _state = TokenizerState.Data;
                            return new HtmlToken { Type = HtmlTokenType.Comment, Data = _buffer.ToString() };
                        }
                        _buffer.Append("--");
                    }
                    else
                    {
                        _buffer.Append(c);
                        _pos++;
                    }
                    break;

                case TokenizerState.BogusComment:
                    if (c == '>')
                    {
                        _pos++;
                        _state = TokenizerState.Data;
                        return new HtmlToken { Type = HtmlTokenType.Comment, Data = _buffer.ToString() };
                    }
                    _buffer.Append(c);
                    _pos++;
                    break;

                case TokenizerState.Doctype:
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f')
                    {
                        _pos++;
                        _buffer.Clear();
                        _state = TokenizerState.DoctypeName;
                    }
                    else if (c == '>')
                    {
                        _pos++;
                        _state = TokenizerState.Data;
                        return new HtmlToken { Type = HtmlTokenType.Doctype, DoctypeName = "" };
                    }
                    else
                    {
                        _buffer.Clear();
                        _state = TokenizerState.DoctypeName;
                    }
                    break;

                case TokenizerState.DoctypeName:
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '>')
                    {
                        var name = _buffer.ToString().ToLowerInvariant();
                        _buffer.Clear();
                        if (c == '>')
                        {
                            _pos++;
                            _state = TokenizerState.Data;
                        }
                        else
                        {
                            _pos++;
                            // Skip rest until >
                            while (_pos < _input.Length && _input[_pos] != '>') _pos++;
                            if (_pos < _input.Length) _pos++; // skip >
                            _state = TokenizerState.Data;
                        }
                        return new HtmlToken { Type = HtmlTokenType.Doctype, DoctypeName = name };
                    }
                    else
                    {
                        _buffer.Append(c);
                        _pos++;
                    }
                    break;

                default:
                    _pos++;
                    break;
            }
        }

        return HtmlToken.Eof();
    }

    /// <summary>
    /// Consume a character reference (entity) starting after '&amp;'.
    /// Returns the decoded character(s) as a string.
    /// </summary>
    private string ConsumeCharacterReference()
    {
        _pos++; // skip '&'

        if (_pos >= _input.Length)
            return "&";

        if (_input[_pos] == '#')
        {
            _pos++;
            return ConsumeNumericReference();
        }

        // Named reference - try common ones
        return ConsumeNamedReference();
    }

    private string ConsumeNumericReference()
    {
        bool hex = false;
        if (_pos < _input.Length && (_input[_pos] == 'x' || _input[_pos] == 'X'))
        {
            hex = true;
            _pos++;
        }

        var numBuf = new StringBuilder();
        while (_pos < _input.Length)
        {
            char c = _input[_pos];
            if (hex ? IsHexDigit(c) : char.IsDigit(c))
            {
                numBuf.Append(c);
                _pos++;
            }
            else
                break;
        }

        if (_pos < _input.Length && _input[_pos] == ';')
            _pos++;

        if (numBuf.Length == 0)
            return hex ? "&#x" : "&#";

        int codepoint = hex
            ? Convert.ToInt32(numBuf.ToString(), 16)
            : int.Parse(numBuf.ToString());

        if (codepoint == 0 || codepoint > 0x10FFFF)
            return "\uFFFD";

        return char.ConvertFromUtf32(codepoint);
    }

    private string ConsumeNamedReference()
    {
        // Scan the maximal alphanumeric run after '&' (entity names are short)
        int nameEnd = _pos;
        int maxEnd = Math.Min(_input.Length, _pos + 32);
        while (nameEnd < maxEnd && IsAsciiAlphanumeric(_input[nameEnd]))
            nameEnd++;

        if (nameEnd > _pos && nameEnd < _input.Length && _input[nameEnd] == ';')
        {
            var name = _input.Substring(_pos, nameEnd - _pos);
            if (NamedEntities.TryGetValue(name, out var value))
            {
                _pos = nameEnd + 1; // consume name and ';'
                return value;
            }
        }

        // Legacy semicolon-less forms (only the historically supported set)
        for (int i = 0; i < LegacyEntities.Length; i++)
        {
            var name = LegacyEntities[i];
            if (_pos + name.Length <= _input.Length &&
                string.Compare(_input, _pos, name, 0, name.Length, StringComparison.Ordinal) == 0)
            {
                _pos += name.Length;
                if (_pos < _input.Length && _input[_pos] == ';')
                    _pos++;
                return LegacyValues[i];
            }
        }

        // Unknown entity: emit '&' and continue
        return "&";
    }

    private static bool IsAsciiAlphanumeric(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');

    // Entities that historically worked without a trailing semicolon
    private static readonly string[] LegacyEntities = { "amp", "lt", "gt", "quot", "apos", "nbsp" };
    private static readonly string[] LegacyValues = { "&", "<", ">", "\"", "'", "\u00A0" };

    /// <summary>Common HTML named character references (WHATWG subset, case-sensitive).</summary>
    private static readonly Dictionary<string, string> NamedEntities = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Core
        ["amp"] = "&", ["lt"] = "<", ["gt"] = ">", ["quot"] = "\"", ["apos"] = "'",
        // Spaces and dashes
        ["nbsp"] = "\u00A0", ["ensp"] = "\u2002", ["emsp"] = "\u2003", ["thinsp"] = "\u2009",
        ["shy"] = "\u00AD", ["ndash"] = "\u2013", ["mdash"] = "\u2014", ["horbar"] = "\u2015",
        // Quotes
        ["lsquo"] = "\u2018", ["rsquo"] = "\u2019", ["sbquo"] = "\u201A",
        ["ldquo"] = "\u201C", ["rdquo"] = "\u201D", ["bdquo"] = "\u201E",
        ["lsaquo"] = "\u2039", ["rsaquo"] = "\u203A", ["laquo"] = "\u00AB", ["raquo"] = "\u00BB",
        ["prime"] = "\u2032", ["Prime"] = "\u2033",
        // Punctuation and symbols
        ["hellip"] = "\u2026", ["middot"] = "\u00B7", ["bull"] = "\u2022",
        ["dagger"] = "\u2020", ["Dagger"] = "\u2021", ["permil"] = "\u2030",
        ["sect"] = "\u00A7", ["para"] = "\u00B6", ["iexcl"] = "\u00A1", ["iquest"] = "\u00BF",
        ["brvbar"] = "\u00A6", ["uml"] = "\u00A8", ["macr"] = "\u00AF", ["acute"] = "\u00B4",
        ["cedil"] = "\u00B8", ["ordf"] = "\u00AA", ["ordm"] = "\u00BA",
        ["copy"] = "\u00A9", ["reg"] = "\u00AE", ["trade"] = "\u2122",
        ["deg"] = "\u00B0", ["plusmn"] = "\u00B1", ["micro"] = "\u00B5",
        ["sup1"] = "\u00B9", ["sup2"] = "\u00B2", ["sup3"] = "\u00B3",
        ["frac14"] = "\u00BC", ["frac12"] = "\u00BD", ["frac34"] = "\u00BE",
        ["times"] = "\u00D7", ["divide"] = "\u00F7", ["minus"] = "\u2212",
        ["lowast"] = "\u2217", ["frasl"] = "\u2044",
        // Currency
        ["euro"] = "\u20AC", ["pound"] = "\u00A3", ["yen"] = "\u00A5",
        ["cent"] = "\u00A2", ["curren"] = "\u00A4",
        // Latin-1 letters (uppercase)
        ["Agrave"] = "\u00C0", ["Aacute"] = "\u00C1", ["Acirc"] = "\u00C2", ["Atilde"] = "\u00C3",
        ["Auml"] = "\u00C4", ["Aring"] = "\u00C5", ["AElig"] = "\u00C6", ["Ccedil"] = "\u00C7",
        ["Egrave"] = "\u00C8", ["Eacute"] = "\u00C9", ["Ecirc"] = "\u00CA", ["Euml"] = "\u00CB",
        ["Igrave"] = "\u00CC", ["Iacute"] = "\u00CD", ["Icirc"] = "\u00CE", ["Iuml"] = "\u00CF",
        ["ETH"] = "\u00D0", ["Ntilde"] = "\u00D1",
        ["Ograve"] = "\u00D2", ["Oacute"] = "\u00D3", ["Ocirc"] = "\u00D4", ["Otilde"] = "\u00D5",
        ["Ouml"] = "\u00D6", ["Oslash"] = "\u00D8",
        ["Ugrave"] = "\u00D9", ["Uacute"] = "\u00DA", ["Ucirc"] = "\u00DB", ["Uuml"] = "\u00DC",
        ["Yacute"] = "\u00DD", ["THORN"] = "\u00DE",
        // Latin-1 letters (lowercase)
        ["szlig"] = "\u00DF",
        ["agrave"] = "\u00E0", ["aacute"] = "\u00E1", ["acirc"] = "\u00E2", ["atilde"] = "\u00E3",
        ["auml"] = "\u00E4", ["aring"] = "\u00E5", ["aelig"] = "\u00E6", ["ccedil"] = "\u00E7",
        ["egrave"] = "\u00E8", ["eacute"] = "\u00E9", ["ecirc"] = "\u00EA", ["euml"] = "\u00EB",
        ["igrave"] = "\u00EC", ["iacute"] = "\u00ED", ["icirc"] = "\u00EE", ["iuml"] = "\u00EF",
        ["eth"] = "\u00F0", ["ntilde"] = "\u00F1",
        ["ograve"] = "\u00F2", ["oacute"] = "\u00F3", ["ocirc"] = "\u00F4", ["otilde"] = "\u00F5",
        ["ouml"] = "\u00F6", ["oslash"] = "\u00F8",
        ["ugrave"] = "\u00F9", ["uacute"] = "\u00FA", ["ucirc"] = "\u00FB", ["uuml"] = "\u00FC",
        ["yacute"] = "\u00FD", ["thorn"] = "\u00FE", ["yuml"] = "\u00FF",
        // Ligatures and accents
        ["OElig"] = "\u0152", ["oelig"] = "\u0153", ["Scaron"] = "\u0160", ["scaron"] = "\u0161",
        ["Yuml"] = "\u0178", ["fnof"] = "\u0192", ["circ"] = "\u02C6", ["tilde"] = "\u02DC",
        // Arrows
        ["larr"] = "\u2190", ["uarr"] = "\u2191", ["rarr"] = "\u2192", ["darr"] = "\u2193",
        ["harr"] = "\u2194", ["crarr"] = "\u21B5",
        ["lArr"] = "\u21D0", ["uArr"] = "\u21D1", ["rArr"] = "\u21D2", ["dArr"] = "\u21D3", ["hArr"] = "\u21D4",
        // Math
        ["infin"] = "\u221E", ["ne"] = "\u2260", ["equiv"] = "\u2261", ["le"] = "\u2264", ["ge"] = "\u2265",
        ["sum"] = "\u2211", ["prod"] = "\u220F", ["int"] = "\u222B", ["radic"] = "\u221A",
        ["asymp"] = "\u2248", ["prop"] = "\u221D", ["part"] = "\u2202", ["nabla"] = "\u2207",
        ["forall"] = "\u2200", ["exist"] = "\u2203", ["empty"] = "\u2205",
        ["isin"] = "\u2208", ["notin"] = "\u2209", ["ni"] = "\u220B",
        ["cap"] = "\u2229", ["cup"] = "\u222A", ["sub"] = "\u2282", ["sup"] = "\u2283",
        ["sube"] = "\u2286", ["supe"] = "\u2287", ["oplus"] = "\u2295", ["otimes"] = "\u2297",
        ["perp"] = "\u22A5", ["sdot"] = "\u22C5", ["not"] = "\u00AC", ["and"] = "\u2227", ["or"] = "\u2228",
        ["ang"] = "\u2220", ["there4"] = "\u2234", ["sim"] = "\u223C", ["cong"] = "\u2245",
        // Greek (uppercase)
        ["Alpha"] = "\u0391", ["Beta"] = "\u0392", ["Gamma"] = "\u0393", ["Delta"] = "\u0394",
        ["Epsilon"] = "\u0395", ["Zeta"] = "\u0396", ["Eta"] = "\u0397", ["Theta"] = "\u0398",
        ["Iota"] = "\u0399", ["Kappa"] = "\u039A", ["Lambda"] = "\u039B", ["Mu"] = "\u039C",
        ["Nu"] = "\u039D", ["Xi"] = "\u039E", ["Omicron"] = "\u039F", ["Pi"] = "\u03A0",
        ["Rho"] = "\u03A1", ["Sigma"] = "\u03A3", ["Tau"] = "\u03A4", ["Upsilon"] = "\u03A5",
        ["Phi"] = "\u03A6", ["Chi"] = "\u03A7", ["Psi"] = "\u03A8", ["Omega"] = "\u03A9",
        // Greek (lowercase)
        ["alpha"] = "\u03B1", ["beta"] = "\u03B2", ["gamma"] = "\u03B3", ["delta"] = "\u03B4",
        ["epsilon"] = "\u03B5", ["zeta"] = "\u03B6", ["eta"] = "\u03B7", ["theta"] = "\u03B8",
        ["iota"] = "\u03B9", ["kappa"] = "\u03BA", ["lambda"] = "\u03BB", ["mu"] = "\u03BC",
        ["nu"] = "\u03BD", ["xi"] = "\u03BE", ["omicron"] = "\u03BF", ["pi"] = "\u03C0",
        ["rho"] = "\u03C1", ["sigmaf"] = "\u03C2", ["sigma"] = "\u03C3", ["tau"] = "\u03C4",
        ["upsilon"] = "\u03C5", ["phi"] = "\u03C6", ["chi"] = "\u03C7", ["psi"] = "\u03C8",
        ["omega"] = "\u03C9",
        // Misc symbols
        ["spades"] = "\u2660", ["clubs"] = "\u2663", ["hearts"] = "\u2665", ["diams"] = "\u2666",
        ["loz"] = "\u25CA", ["oline"] = "\u203E", ["alefsym"] = "\u2135",
        ["weierp"] = "\u2118", ["image"] = "\u2111", ["real"] = "\u211C",
        ["zwnj"] = "\u200C", ["zwj"] = "\u200D", ["lrm"] = "\u200E", ["rlm"] = "\u200F",
        ["ceil"] = "\u2308", ["rceil"] = "\u2309", ["lceil"] = "\u2308",
        ["lfloor"] = "\u230A", ["rfloor"] = "\u230B", ["lang"] = "\u2329", ["rang"] = "\u232A",
    };

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private enum TokenizerState
    {
        Data,
        TagOpen,
        EndTagOpen,
        TagName,
        BeforeAttributeName,
        AttributeName,
        AfterAttributeName,
        BeforeAttributeValue,
        AttributeValueDoubleQuoted,
        AttributeValueSingleQuoted,
        AttributeValueUnquoted,
        AfterAttributeValueQuoted,
        SelfClosingStartTag,
        MarkupDeclarationOpen,
        Comment,
        BogusComment,
        Doctype,
        DoctypeName
    }
}
