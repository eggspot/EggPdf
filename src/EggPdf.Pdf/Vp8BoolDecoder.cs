namespace EggPdf.Pdf;

/// <summary>
/// The VP8 boolean entropy decoder (RFC 6386 section 7): an arithmetic decoder yielding one
/// binary decision per call, each with an 8-bit probability that the bit is 0. Reads past the end
/// of the partition supply zeros, so truncated data decodes to a (wrong but bounded) picture
/// instead of throwing.
/// </summary>
internal sealed class Vp8BoolDecoder
{
    private readonly byte[] _data;
    private int _pos;
    private readonly int _end;
    private int _value;
    private int _range = 255;
    private int _bitCount;

    public Vp8BoolDecoder(byte[] data, int start, int length)
    {
        _data = data;
        _pos = start;
        _end = start + length;
        _value = (NextByte() << 8) | NextByte();
    }

    /// <summary>Bytes requested beyond the end of the partition; a large value means the data was cut short.</summary>
    public int Overrun { get; private set; }

    private int NextByte()
    {
        if (_pos < _end) return _data[_pos++];
        Overrun++;
        return 0;
    }

    /// <summary>Decode one bool whose probability of being 0 is <paramref name="prob"/>/256.</summary>
    public int ReadBool(int prob)
    {
        int split = 1 + (((_range - 1) * prob) >> 8);
        int bigSplit = split << 8;
        int bit;
        if (_value >= bigSplit)
        {
            bit = 1;
            _range -= split;
            _value -= bigSplit;
        }
        else
        {
            bit = 0;
            _range = split;
        }

        while (_range < 128)
        {
            _value <<= 1;
            _range <<= 1;
            if (++_bitCount == 8)
            {
                _bitCount = 0;
                _value |= NextByte();
            }
        }
        return bit;
    }

    /// <summary>A flag bit (probability one half).</summary>
    public int ReadFlag() => ReadBool(128);

    /// <summary>An unsigned <paramref name="bits"/>-bit literal, most significant bit first.</summary>
    public int ReadLiteral(int bits)
    {
        int v = 0;
        while (bits-- > 0) v = (v << 1) | ReadBool(128);
        return v;
    }

    /// <summary>A magnitude of <paramref name="bits"/> bits followed by a sign bit.</summary>
    public int ReadSigned(int bits)
    {
        int v = ReadLiteral(bits);
        return ReadBool(128) != 0 ? -v : v;
    }

    /// <summary>A flag, then (when set) a signed value; 0 when the flag is clear.</summary>
    public int ReadOptionalSigned(int bits) => ReadFlag() != 0 ? ReadSigned(bits) : 0;
}
