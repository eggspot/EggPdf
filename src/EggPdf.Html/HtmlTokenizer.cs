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
        // Try common named entities (performance: check most common first)
        string?[] entities = { "amp", "lt", "gt", "quot", "apos", "nbsp" };
        string?[] values = { "&", "<", ">", "\"", "'", "\u00A0" };

        for (int i = 0; i < entities.Length; i++)
        {
            var name = entities[i]!;
            if (_pos + name.Length <= _input.Length &&
                _input.Substring(_pos, name.Length).Equals(name, StringComparison.Ordinal))
            {
                _pos += name.Length;
                if (_pos < _input.Length && _input[_pos] == ';')
                    _pos++;
                return values[i]!;
            }
        }

        // Unknown entity: emit '&' and continue
        return "&";
    }

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
