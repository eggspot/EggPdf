using System.Collections.Generic;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// LSB-first bit writer mirroring Vp8LDecoder's bit order, used only to
/// hand-construct minimal valid VP8L bitstreams for testing -- an
/// independent implementation of the same documented bit-packing rule
/// (not reusing any decoder internals), so a passing test is evidence the
/// decoder actually implements the spec, not just its own inverse.
/// </summary>
internal sealed class Vp8LTestBitWriter
{
    private const byte Vp8LSignature = 0x2F;

    private readonly List<byte> _bytes = new();
    private int _curByte;
    private int _bitPos;

    public void WriteBits(uint value, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int bit = (int)((value >> i) & 1);
            _curByte |= bit << _bitPos;
            _bitPos++;
            if (_bitPos == 8)
            {
                _bytes.Add((byte)_curByte);
                _curByte = 0;
                _bitPos = 0;
            }
        }
    }

    /// <summary>Write a Huffman "simple code, 1 symbol" group: decodes to exactly this symbol, consuming zero bits at read time.</summary>
    public void WriteSimpleSingleSymbol(int symbol)
    {
        WriteSimpleCodeHeader(symbolCount: 1);
        WriteFirstSymbol(symbol);
    }

    /// <summary>
    /// Write a Huffman "simple code, 2 symbols" group: both symbols get a
    /// 1-bit canonical code, assigned "0"/"1" in increasing symbol-VALUE
    /// order (matching Vp8LDecoder.HuffmanTree.Build's canonical assignment)
    /// regardless of the order given here. The smaller symbol is also written
    /// first, so the bitstream order and the canonical codes visibly agree.
    /// Returns which bit selects which requested symbol, so callers can write
    /// the right pixel-stream bits.
    /// </summary>
    public (int bitForA, int bitForB) WriteSimpleTwoSymbol(int symbolA, int symbolB)
    {
        WriteSimpleCodeHeader(symbolCount: 2);

        int smaller = symbolA < symbolB ? symbolA : symbolB;
        int larger = symbolA < symbolB ? symbolB : symbolA;

        WriteFirstSymbol(smaller);
        WriteBits((uint)larger, 8);

        int bitForSmaller = 0, bitForLarger = 1;
        return symbolA < symbolB ? (bitForSmaller, bitForLarger) : (bitForLarger, bitForSmaller);
    }

    /// <summary>Prepend the 1-byte VP8L signature and return the finished byte array (flushing any partial byte).</summary>
    public byte[] ToVp8LPayload()
    {
        if (_bitPos > 0) { _bytes.Add((byte)_curByte); _curByte = 0; _bitPos = 0; }
        var result = new byte[_bytes.Count + 1];
        result[0] = Vp8LSignature;
        _bytes.CopyTo(result, 1);
        return result;
    }

    private void WriteSimpleCodeHeader(int symbolCount)
    {
        const uint isSimpleCode = 1;
        WriteBits(isSimpleCode, 1);
        WriteBits((uint)(symbolCount - 1), 1);
    }

    /// <summary>The first symbol of a simple code takes a 1-bit form when it is 0 or 1, else an 8-bit form; the second is always 8-bit.</summary>
    private void WriteFirstSymbol(int symbol)
    {
        bool fitsOneBit = symbol <= 1;
        WriteBits(fitsOneBit ? 0u : 1u, 1);
        WriteBits((uint)symbol, fitsOneBit ? 1 : 8);
    }
}
