using System;
using System.Collections.Generic;
using System.Text;

namespace EggPdf.Pdf;

internal sealed class PdfSyntaxParser
{
    private const int MaxNestingDepth = 256;

    private readonly byte[] _data;
    private readonly int _end;
    private readonly Func<PdfReference, PdfObject?>? _resolveLength;
    private readonly Func<int, int>? _findEndstream;
    private readonly PdfParseBudget? _budget;

    public PdfSyntaxParser(byte[] data, int start, int end, Func<PdfReference, PdfObject?>? resolveLength = null,
        Func<int, int>? findEndstream = null, PdfParseBudget? budget = null)
    {
        _data = data;
        Position = start;
        _end = end;
        _resolveLength = resolveLength;
        _findEndstream = findEndstream;
        _budget = budget;
    }

    public int Position { get; set; }

    public bool AtEnd
    {
        get
        {
            SkipWhitespaceAndComments();
            return Position >= _end;
        }
    }

    public static bool IsWhitespace(byte b) => b == 0x20 || b == 0x0A || b == 0x0D || b == 0x09 || b == 0x0C || b == 0x00;

    public static bool IsDelimiter(byte b) =>
        b == '(' || b == ')' || b == '<' || b == '>' || b == '[' || b == ']' || b == '{' || b == '}' || b == '/' || b == '%';

    public void SkipWhitespaceAndComments()
    {
        while (Position < _end)
        {
            byte b = _data[Position];
            if (IsWhitespace(b))
            {
                Position++;
            }
            else if (b == '%')
            {
                while (Position < _end && _data[Position] != '\n' && _data[Position] != '\r') Position++;
            }
            else
            {
                return;
            }
        }
    }

    public bool TryReadKeyword(string keyword)
    {
        SkipWhitespaceAndComments();
        if (Position + keyword.Length > _end) return false;
        for (int i = 0; i < keyword.Length; i++)
            if (_data[Position + i] != keyword[i]) return false;
        int after = Position + keyword.Length;
        if (after < _end && !IsWhitespace(_data[after]) && !IsDelimiter(_data[after])) return false;
        Position = after;
        return true;
    }

    public long ReadInteger()
    {
        SkipWhitespaceAndComments();
        if (!TryReadInteger(out long value)) throw Error("Expected an integer");
        return value;
    }

    public bool TryReadIndirectHeader(out int objectNumber, out int generation)
    {
        objectNumber = 0;
        generation = 0;
        int start = Position;
        SkipWhitespaceAndComments();
        if (TryReadInteger(out long number) && SkipWhitespaceThenInteger(out long gen) && TryReadKeyword("obj") &&
            number > 0 && number <= int.MaxValue && gen >= 0 && gen <= ushort.MaxValue)
        {
            objectNumber = (int)number;
            generation = (int)gen;
            return true;
        }
        Position = start;
        return false;
    }

    public PdfObject ReadIndirectObjectBody()
    {
        var value = ReadObject();
        if (value is PdfDictionary dictionary && TryReadKeyword("stream"))
            return ReadStreamData(dictionary);
        return value;
    }

    public PdfObject ReadObject()
    {
        int start = Position;
        try
        {
            return ReadObject(0);
        }
        finally
        {
            _budget?.Scanned(Position - start);
        }
    }

    private PdfObject ReadObject(int depth)
    {
        var value = ReadObjectWithoutAccounting(depth);
        _budget?.Materialized(value);
        return value;
    }

    private PdfObject ReadObjectWithoutAccounting(int depth)
    {
        if (depth > MaxNestingDepth) throw Error("Arrays or dictionaries are nested too deeply");
        SkipWhitespaceAndComments();
        if (Position >= _end) throw Error("Unexpected end of data");

        byte b = _data[Position];
        switch (b)
        {
            case (byte)'/':
                return ReadName();
            case (byte)'(':
                return ReadLiteralString();
            case (byte)'[':
                return ReadArray(depth);
            case (byte)'<':
                if (Position + 1 < _end && _data[Position + 1] == '<') return ReadDictionary(depth);
                return ReadHexString();
        }

        if (b == '+' || b == '-' || b == '.' || (b >= '0' && b <= '9'))
            return ReadNumberOrReference();

        if (TryReadKeyword("true")) return new PdfBoolean(true);
        if (TryReadKeyword("false")) return new PdfBoolean(false);
        if (TryReadKeyword("null")) return PdfNull.Instance;
        throw Error("Unexpected token");
    }

    private PdfObject ReadNumberOrReference()
    {
        int start = Position;
        if (TryReadInteger(out long first))
        {
            int afterFirst = Position;
            if (first >= 0 && first <= int.MaxValue && SkipWhitespaceThenInteger(out long generation) &&
                generation >= 0 && generation <= ushort.MaxValue && TryReadKeyword("R"))
                return new PdfReference((int)first, (int)generation);
            Position = afterFirst;
            if (Position >= _end || _data[Position] != '.')
                return new PdfNumber(first);
        }
        Position = start;
        return new PdfNumber(ReadReal());
    }

    private bool SkipWhitespaceThenInteger(out long value)
    {
        value = 0;
        int start = Position;
        SkipWhitespaceAndComments();
        if (Position == start || !TryReadInteger(out value))
        {
            Position = start;
            return false;
        }
        return true;
    }

    private bool TryReadInteger(out long value)
    {
        value = 0;
        int p = Position;
        bool negative = false;
        if (p < _end && (_data[p] == '+' || _data[p] == '-'))
        {
            negative = _data[p] == '-';
            p++;
        }
        int digitsStart = p;
        while (p < _end && _data[p] >= '0' && _data[p] <= '9')
        {
            if (value > (long.MaxValue - 9) / 10) return false;
            value = value * 10 + (_data[p] - '0');
            p++;
        }
        if (p == digitsStart) return false;
        if (p < _end && !IsWhitespace(_data[p]) && !IsDelimiter(_data[p]) && _data[p] != '.') return false;
        if (p < _end && _data[p] == '.') return false;
        if (negative) value = -value;
        Position = p;
        return true;
    }

    private double ReadReal()
    {
        int p = Position;
        bool negative = false;
        if (p < _end && (_data[p] == '+' || _data[p] == '-'))
        {
            negative = _data[p] == '-';
            p++;
        }
        double value = 0;
        bool any = false;
        while (p < _end && _data[p] >= '0' && _data[p] <= '9')
        {
            value = value * 10 + (_data[p] - '0');
            p++;
            any = true;
        }
        if (p < _end && _data[p] == '.')
        {
            p++;
            double scale = 0.1;
            while (p < _end && _data[p] >= '0' && _data[p] <= '9')
            {
                value += (_data[p] - '0') * scale;
                scale /= 10;
                p++;
                any = true;
            }
        }
        if (!any) throw Error("Malformed number");
        Position = p;
        return negative ? -value : value;
    }

    private PdfName ReadName()
    {
        Position++;
        var name = new StringBuilder();
        while (Position < _end && !IsWhitespace(_data[Position]) && !IsDelimiter(_data[Position]))
        {
            byte b = _data[Position++];
            if (b == '#' && Position + 1 < _end && TryHex(_data[Position], out int hi) && TryHex(_data[Position + 1], out int lo))
            {
                name.Append((char)(hi * 16 + lo));
                Position += 2;
            }
            else
            {
                name.Append((char)b);
            }
        }
        return new PdfName(name.ToString());
    }

    private PdfString ReadLiteralString()
    {
        Position++;
        var bytes = new List<byte>();
        int nesting = 1;
        while (Position < _end)
        {
            byte b = _data[Position++];
            if (b == '\\')
            {
                if (Position >= _end) break;
                byte e = _data[Position++];
                switch (e)
                {
                    case (byte)'n': bytes.Add((byte)'\n'); break;
                    case (byte)'r': bytes.Add((byte)'\r'); break;
                    case (byte)'t': bytes.Add((byte)'\t'); break;
                    case (byte)'b': bytes.Add(8); break;
                    case (byte)'f': bytes.Add(12); break;
                    case (byte)'\r':
                        if (Position < _end && _data[Position] == '\n') Position++;
                        break;
                    case (byte)'\n':
                        break;
                    default:
                        if (e >= '0' && e <= '7')
                        {
                            int octal = e - '0';
                            for (int i = 0; i < 2 && Position < _end && _data[Position] >= '0' && _data[Position] <= '7'; i++)
                                octal = octal * 8 + (_data[Position++] - '0');
                            bytes.Add((byte)octal);
                        }
                        else
                        {
                            bytes.Add(e);
                        }
                        break;
                }
            }
            else if (b == '(')
            {
                nesting++;
                bytes.Add(b);
            }
            else if (b == ')')
            {
                if (--nesting == 0) return new PdfString(bytes.ToArray());
                bytes.Add(b);
            }
            else
            {
                bytes.Add(b);
            }
        }
        throw Error("Unterminated string");
    }

    private PdfString ReadHexString()
    {
        Position++;
        var bytes = new List<byte>();
        int pending = -1;
        while (Position < _end)
        {
            byte b = _data[Position++];
            if (b == '>')
            {
                if (pending >= 0) bytes.Add((byte)(pending * 16));
                return new PdfString(bytes.ToArray());
            }
            if (IsWhitespace(b)) continue;
            if (!TryHex(b, out int digit)) throw Error("Invalid hex string");
            if (pending < 0)
            {
                pending = digit;
            }
            else
            {
                bytes.Add((byte)(pending * 16 + digit));
                pending = -1;
            }
        }
        throw Error("Unterminated hex string");
    }

    private PdfArray ReadArray(int depth)
    {
        Position++;
        var array = new PdfArray();
        while (true)
        {
            SkipWhitespaceAndComments();
            if (Position >= _end) throw Error("Unterminated array");
            if (_data[Position] == ']')
            {
                Position++;
                return array;
            }
            array.Items.Add(ReadObject(depth + 1));
        }
    }

    private PdfDictionary ReadDictionary(int depth)
    {
        Position += 2;
        var dictionary = new PdfDictionary();
        while (true)
        {
            SkipWhitespaceAndComments();
            if (Position >= _end) throw Error("Unterminated dictionary");
            if (_data[Position] == '>' && Position + 1 < _end && _data[Position + 1] == '>')
            {
                Position += 2;
                return dictionary;
            }
            if (_data[Position] != '/') throw Error("Expected a name as dictionary key");
            var key = ReadName().Value;
            dictionary.Set(key, ReadObject(depth + 1));
        }
    }

    private PdfStream ReadStreamData(PdfDictionary dictionary)
    {
        if (Position < _end && _data[Position] == '\r') Position++;
        if (Position < _end && _data[Position] == '\n') Position++;
        int dataStart = Position;

        long declared = -1;
        var lengthObject = dictionary.Get("Length");
        if (lengthObject is PdfReference reference && _resolveLength != null) lengthObject = _resolveLength(reference);
        if (lengthObject is PdfNumber number && number.IsInteger) declared = (long)number.Value;

        if (declared >= 0 && dataStart + declared <= _end && EndstreamFollows((int)(dataStart + declared)))
        {
            Position = (int)(dataStart + declared);
            TryReadKeyword("endstream");
            return new PdfStream(dictionary, _data, dataStart, (int)declared);
        }

        int endstream = _findEndstream?.Invoke(dataStart) ?? IndexOf(_data, "endstream", dataStart, _end);
        if (endstream < 0 || endstream + "endstream".Length > _end) throw Error("Stream has no endstream");
        int dataEnd = endstream;
        if (dataEnd > dataStart && _data[dataEnd - 1] == '\n') dataEnd--;
        if (dataEnd > dataStart && _data[dataEnd - 1] == '\r') dataEnd--;
        Position = endstream + "endstream".Length;
        return new PdfStream(dictionary, _data, dataStart, dataEnd - dataStart);
    }

    private bool EndstreamFollows(int at)
    {
        int saved = Position;
        Position = at;
        bool found = TryReadKeyword("endstream");
        Position = saved;
        return found;
    }

    public static int IndexOf(byte[] data, string token, int start, int end)
    {
        for (int i = Math.Max(start, 0); i <= end - token.Length; i++)
        {
            int j = 0;
            while (j < token.Length && data[i + j] == token[j]) j++;
            if (j == token.Length) return i;
        }
        return -1;
    }

    public static int LastIndexOf(byte[] data, string token, int end)
    {
        for (int i = Math.Min(end, data.Length) - token.Length; i >= 0; i--)
        {
            int j = 0;
            while (j < token.Length && data[i + j] == token[j]) j++;
            if (j == token.Length) return i;
        }
        return -1;
    }

    private static bool TryHex(byte b, out int value)
    {
        if (b >= '0' && b <= '9') value = b - '0';
        else if (b >= 'a' && b <= 'f') value = b - 'a' + 10;
        else if (b >= 'A' && b <= 'F') value = b - 'A' + 10;
        else
        {
            value = 0;
            return false;
        }
        return true;
    }

    private PdfFormatException Error(string message) =>
        new(message + " at byte " + Position.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
}
