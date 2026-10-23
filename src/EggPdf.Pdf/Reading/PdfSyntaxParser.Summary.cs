namespace EggPdf.Pdf;

internal sealed partial class PdfSyntaxParser
{
    private enum PageTreeKey { Other, Type, Count, Kids }

    public PdfSyntaxParser MoveTo(int start, int end, PdfParseBudget? budget)
    {
        Position = start;
        _end = end;
        _logicalEnd = -1;
        _budget = budget;
        return this;
    }

    public PdfPageTreeSummary? ReadPageTreeSummary()
    {
        int start = Position;
        try
        {
            SkipWhitespaceAndComments();
            if (Position + 1 >= _end || _data[Position] != '<' || _data[Position + 1] != '<') return null;
            Position += 2;
            var type = PdfPageTreeNodeType.Unspecified;
            PdfObject? kids = null;
            PdfObject? count = null;
            while (true)
            {
                SkipWhitespaceAndComments();
                if (Position >= _end) throw Error("Unterminated dictionary");
                if (_data[Position] == '>' && Position + 1 < _end && _data[Position + 1] == '>')
                {
                    Position += 2;
                    SkipWhitespaceAndComments();
                    if (EndsBeforeObject && _end - Position < StreamKeywordWithLineBreak) throw new PdfWindowTooSmallException();
                    return TryReadKeyword("stream") ? null : new PdfPageTreeSummary(type, kids, count);
                }
                if (_data[Position] != '/') throw Error("Expected a name as dictionary key");
                var key = ReadPageTreeKey();
                SkipWhitespaceAndComments();
                if (Position >= _end) throw Error("Unexpected end of data");
                switch (key)
                {
                    case PageTreeKey.Type when _data[Position] == '/':
                        type = ReadNodeType();
                        break;
                    case PageTreeKey.Kids when _data[Position] == '[':
                        SkipObject(1);
                        kids = PdfPageTreeSummary.DirectKidsArray;
                        break;
                    case PageTreeKey.Kids:
                        kids = ReadObject(1);
                        break;
                    case PageTreeKey.Count:
                        count = ReadObject(1);
                        break;
                    default:
                        if (key == PageTreeKey.Type) type = PdfPageTreeNodeType.Other;
                        SkipValue();
                        break;
                }
            }
        }
        finally
        {
            _budget?.Scanned(Position - start);
        }
    }

    private PageTreeKey ReadPageTreeKey()
    {
        int start = ++Position;
        SkipNameCharacters();
        if (NameEquals(start, "Type")) return PageTreeKey.Type;
        if (NameEquals(start, "Count")) return PageTreeKey.Count;
        if (NameEquals(start, "Kids")) return PageTreeKey.Kids;
        return PageTreeKey.Other;
    }

    private PdfPageTreeNodeType ReadNodeType()
    {
        int start = ++Position;
        SkipNameCharacters();
        if (NameEquals(start, "Page")) return PdfPageTreeNodeType.Page;
        if (NameEquals(start, "Pages")) return PdfPageTreeNodeType.Pages;
        return PdfPageTreeNodeType.Other;
    }

    private void SkipNameCharacters()
    {
        while (Position < _end && !IsWhitespace(_data[Position]) && !IsDelimiter(_data[Position])) Position++;
    }

    private bool NameEquals(int start, string name)
    {
        int matched = 0;
        for (int i = start; i < Position; matched++)
        {
            int b = _data[i];
            if (b == '#' && i + 2 < Position && TryHex(_data[i + 1], out int high) && TryHex(_data[i + 2], out int low))
            {
                b = high * 16 + low;
                i += 3;
            }
            else
            {
                i++;
            }
            if (matched >= name.Length || name[matched] != b) return false;
        }
        return matched == name.Length;
    }

    private void SkipValue()
    {
        if (TryReadInteger(out _))
        {
            int afterNumber = Position;
            if (!SkipWhitespaceThenInteger(out _) || !TryReadKeyword("R")) Position = afterNumber;
            return;
        }
        SkipObject(1);
    }

    private void SkipObject(int depth)
    {
        if (depth > MaxNestingDepth) throw Error("Arrays or dictionaries are nested too deeply");
        SkipWhitespaceAndComments();
        if (Position >= _end) throw Error("Unexpected end of data");
        byte b = _data[Position];
        switch (b)
        {
            case (byte)'/':
                Position++;
                SkipNameCharacters();
                return;
            case (byte)'(':
                SkipLiteralString();
                return;
            case (byte)'[':
                Position++;
                SkipUntil((byte)']', depth, "Unterminated array");
                return;
            case (byte)'<':
                if (Position + 1 < _end && _data[Position + 1] == '<')
                {
                    Position += 2;
                    SkipUntil((byte)'>', depth, "Unterminated dictionary");
                    return;
                }
                while (Position < _end && _data[Position] != '>') Position++;
                if (Position >= _end) throw Error("Unterminated hex string");
                Position++;
                return;
        }
        if (IsDelimiter(b)) throw Error("Unexpected token");
        SkipNameCharacters();
    }

    private void SkipUntil(byte close, int depth, string unterminated)
    {
        while (true)
        {
            SkipWhitespaceAndComments();
            if (Position >= _end) throw Error(unterminated);
            if (_data[Position] == close && (close != '>' || (Position + 1 < _end && _data[Position + 1] == '>')))
            {
                Position += close == '>' ? 2 : 1;
                return;
            }
            SkipObject(depth + 1);
        }
    }

    private void SkipLiteralString()
    {
        Position++;
        int nesting = 1;
        while (Position < _end)
        {
            byte b = _data[Position++];
            if (b == '\\')
            {
                if (Position < _end) Position++;
            }
            else if (b == '(')
            {
                nesting++;
            }
            else if (b == ')' && --nesting == 0)
            {
                return;
            }
        }
        throw Error("Unterminated string");
    }
}
