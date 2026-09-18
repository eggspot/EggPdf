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
        WriteBits(1, 1); // is_simple
        WriteBits(0, 1); // num_symbols - 1 = 0 (i.e. 1 symbol)
        if (symbol <= 1)
        {
            WriteBits(0, 1); // 1-bit form
            WriteBits((uint)symbol, 1);
        }
        else
        {
            WriteBits(1, 1); // 8-bit form
            WriteBits((uint)symbol, 8);
        }
    }

    /// <summary>
    /// Write a Huffman "simple code, 2 symbols" group: both symbols get a
    /// 1-bit canonical code, assigned "0"/"1" in increasing symbol-VALUE
    /// order (matching Vp8LDecoder.HuffmanTree.Build's canonical assignment)
    /// regardless of the order given here. Returns which bit selects which
    /// requested symbol, so callers can write the right pixel-stream bits.
    /// </summary>
    public (int bitForA, int bitForB) WriteSimpleTwoSymbol(int symbolA, int symbolB)
    {
        WriteBits(1, 1); // is_simple
        WriteBits(1, 1); // num_symbols - 1 = 1 (2 symbols)

        int smaller = symbolA < symbolB ? symbolA : symbolB;
        int larger = symbolA < symbolB ? symbolB : symbolA;

        // First symbol (encoded here) must be the smaller one for canonical
        // code "0" -- Build() assigns codes to symbols in increasing value
        // order, so the smaller symbol always gets "0" regardless of which
        // one is written first in the bitstream; we just also write it first
        // here to keep the two bookkeeping systems visibly in sync.
        WriteBits(smaller <= 1 ? 0u : 1u, 1); // first symbol length-bits flag
        WriteBits((uint)smaller, smaller <= 1 ? 1 : 8);
        WriteBits((uint)larger, 8); // second symbol: always 8-bit form

        int bitForSmaller = 0, bitForLarger = 1;
        return symbolA < symbolB ? (bitForSmaller, bitForLarger) : (bitForLarger, bitForSmaller);
    }

    /// <summary>Prepend the 1-byte VP8L signature and return the finished byte array (flushing any partial byte).</summary>
    public byte[] ToVp8LPayload()
    {
        if (_bitPos > 0) { _bytes.Add((byte)_curByte); _curByte = 0; _bitPos = 0; }
        var result = new byte[_bytes.Count + 1];
        result[0] = 0x2F;
        _bytes.CopyTo(result, 1);
        return result;
    }
}
