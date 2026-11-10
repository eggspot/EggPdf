using System;
using System.Collections.Generic;
using System.Linq;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class Vp8LDecoderConformanceTests
{
    private const int GreenAlphabetWithoutCache = 256 + 24;
    private const int DistanceAlphabet = 40;
    private const int PredictorTransform = 0;
    private const int SubtractGreenTransform = 2;
    private const int ColorIndexingTransform = 3;
    private static readonly int[] CodeLengthCodeOrder = { 17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

    [Fact]
    public void Decode_LiteralPixel_ReadsRedThenBlueThenAlpha()
    {
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 2, 1);
        WriteNoTransformNoCacheNoMetaCodes(w);
        w.WriteSimpleSingleSymbol(5);
        var red = w.WriteSimpleTwoSymbol(10, 20);
        w.WriteSimpleSingleSymbol(30);
        var alpha = w.WriteSimpleTwoSymbol(200, 100);
        w.WriteSimpleSingleSymbol(0);

        w.WriteBits((uint)red.bitForA, 1);
        w.WriteBits((uint)alpha.bitForA, 1);
        w.WriteBits((uint)red.bitForB, 1);
        w.WriteBits((uint)alpha.bitForB, 1);

        var argb = DecodePixels(w);

        argb.Should().Equal(10, 5, 30, 200, 20, 5, 30, 100);
    }

    [Fact]
    public void Decode_MetaPrefixCodes_GroupIndexIsRedHighByteGreenLowByte()
    {
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 1, 1);
        w.WriteBits(0, 1);
        w.WriteBits(0, 1);
        w.WriteBits(1, 1);
        w.WriteBits(0, 3);

        w.WriteBits(0, 1);
        WriteSingleColorGroup(w, red: 0, green: 1, blue: 7, alpha: 0);

        WriteSingleColorGroup(w, red: 10, green: 10, blue: 10, alpha: 10);
        WriteSingleColorGroup(w, red: 100, green: 200, blue: 50, alpha: 255);

        var argb = DecodePixels(w);

        argb.Should().Equal(100, 200, 50, 255);
    }

    [Fact]
    public void Decode_PredictorTopRightOnRightmostColumn_UsesLeftmostPixelOfCurrentRow()
    {
        var argb = DecodeTwoByTwoPredictorImage(mode: 3);

        argb.Should().Equal(
            10, 20, 30, 255, 10, 20, 30, 255,
            20, 40, 60, 255, 20, 40, 60, 255);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(15)]
    public void Decode_PredictorModeAbove13_PredictsOpaqueBlack(int mode)
    {
        var argb = DecodeTwoByTwoPredictorImage(mode);

        argb.Skip(12).Should().Equal(0, 0, 0, 255);
    }

    [Fact]
    public void Decode_DistancePrefixCode24_ReadsItsExtraBits()
    {
        const int Width = 4100;
        const int DistanceCode = 24;
        const int DistanceExtraBitCount = 11;
        int distanceValue = Width - 1 + 120;
        int extraBits = distanceValue - 1 - (2 << DistanceExtraBitCount);

        var w = new Vp8LTestBitWriter();
        WriteHeader(w, Width, 1);
        WriteNoTransformNoCacheNoMetaCodes(w);
        var green = WriteNormalCode(w, GreenAlphabetWithoutCache, new Dictionary<int, int> { [0] = 1, [1] = 2, [256] = 2 });
        w.WriteSimpleSingleSymbol(0);
        w.WriteSimpleSingleSymbol(0);
        w.WriteSimpleSingleSymbol(255);
        w.WriteSimpleSingleSymbol(DistanceCode);

        WriteCode(w, green[1]);
        for (int i = 1; i < Width - 1; i++)
            WriteCode(w, green[0]);
        WriteCode(w, green[256]);
        w.WriteBits((uint)extraBits, DistanceExtraBitCount);

        var argb = DecodePixels(w);

        argb.Skip((Width - 1) * 4).Should().Equal(0, 1, 0, 255);
        argb.Skip(4).Take(4).Should().Equal(0, 0, 0, 255);
    }

    [Fact]
    public void Decode_ColorIndexOutsidePalette_IsTransparentBlack()
    {
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 1, 1);
        w.WriteBits(1, 1);
        w.WriteBits(ColorIndexingTransform, 2);
        w.WriteBits(3 - 1, 8);
        w.WriteBits(0, 1);
        WriteSingleColorGroup(w, red: 10, green: 20, blue: 30, alpha: 255);
        w.WriteBits(0, 1);

        w.WriteBits(0, 1);
        w.WriteBits(0, 1);
        WriteSingleColorGroup(w, red: 0, green: 3, blue: 0, alpha: 0);

        var argb = DecodePixels(w);

        argb.Should().Equal(0, 0, 0, 0);
    }

    [Fact]
    public void Decode_TransformAfterColorIndexing_WorksOnTheBundledWidth()
    {
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 4, 1);
        w.WriteBits(1, 1);
        w.WriteBits(ColorIndexingTransform, 2);
        w.WriteBits(2 - 1, 8);
        w.WriteBits(0, 1);
        WriteSingleColorGroup(w, red: 0, green: 100, blue: 0, alpha: 255);
        w.WriteBits(1, 1);
        w.WriteBits(SubtractGreenTransform, 2);
        w.WriteBits(0, 1);

        w.WriteBits(0, 1);
        w.WriteBits(0, 1);
        WriteSingleColorGroup(w, red: 0, green: 0b0101, blue: 0, alpha: 0);

        var argb = DecodePixels(w);

        argb.Should().Equal(
            0, 200, 0, 254,
            0, 100, 0, 255,
            0, 200, 0, 254,
            0, 100, 0, 255);
    }

    [Fact]
    public void Decode_NormalCodeWithOneUsedSymbol_ReadsNoBits()
    {
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 2, 1);
        WriteNoTransformNoCacheNoMetaCodes(w);
        WriteNormalCode(w, GreenAlphabetWithoutCache, new Dictionary<int, int> { [7] = 1 });
        var red = w.WriteSimpleTwoSymbol(40, 80);
        w.WriteSimpleSingleSymbol(0);
        w.WriteSimpleSingleSymbol(255);
        w.WriteSimpleSingleSymbol(0);

        w.WriteBits((uint)red.bitForB, 1);
        w.WriteBits((uint)red.bitForA, 1);

        var argb = DecodePixels(w);

        argb.Should().Equal(80, 7, 0, 255, 40, 7, 0, 255);
    }

    private static byte[] DecodeTwoByTwoPredictorImage(int mode)
    {
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 2, 2);
        w.WriteBits(1, 1);
        w.WriteBits(PredictorTransform, 2);
        w.WriteBits(0, 3);
        w.WriteBits(0, 1);
        WriteSingleColorGroup(w, red: 0, green: mode, blue: 0, alpha: 0);
        w.WriteBits(0, 1);

        w.WriteBits(0, 1);
        w.WriteBits(0, 1);
        var green = w.WriteSimpleTwoSymbol(20, 0);
        var red = w.WriteSimpleTwoSymbol(10, 0);
        var blue = w.WriteSimpleTwoSymbol(30, 0);
        w.WriteSimpleSingleSymbol(0);
        w.WriteSimpleSingleSymbol(0);

        foreach (bool isResidual in new[] { true, false, true, false })
        {
            w.WriteBits((uint)(isResidual ? green.bitForA : green.bitForB), 1);
            w.WriteBits((uint)(isResidual ? red.bitForA : red.bitForB), 1);
            w.WriteBits((uint)(isResidual ? blue.bitForA : blue.bitForB), 1);
        }

        return DecodePixels(w);
    }

    private static byte[] DecodePixels(Vp8LTestBitWriter w)
    {
        var result = Vp8LDecoder.Decode(w.ToVp8LPayload());
        result.Should().NotBeNull();
        return result!.Value.argb;
    }

    private static void WriteHeader(Vp8LTestBitWriter w, int width, int height)
    {
        w.WriteBits((uint)(width - 1), 14);
        w.WriteBits((uint)(height - 1), 14);
        w.WriteBits(1, 1);
        w.WriteBits(0, 3);
    }

    private static void WriteNoTransformNoCacheNoMetaCodes(Vp8LTestBitWriter w)
    {
        w.WriteBits(0, 1);
        w.WriteBits(0, 1);
        w.WriteBits(0, 1);
    }

    private static void WriteSingleColorGroup(Vp8LTestBitWriter w, int red, int green, int blue, int alpha)
    {
        w.WriteSimpleSingleSymbol(green);
        w.WriteSimpleSingleSymbol(red);
        w.WriteSimpleSingleSymbol(blue);
        w.WriteSimpleSingleSymbol(alpha);
        w.WriteSimpleSingleSymbol(0);
    }

    private static void WriteCode(Vp8LTestBitWriter w, (int code, int length) prefixCode)
    {
        for (int b = prefixCode.length - 1; b >= 0; b--)
            w.WriteBits((uint)((prefixCode.code >> b) & 1), 1);
    }

    private static Dictionary<int, (int code, int length)> CanonicalCodes(IReadOnlyDictionary<int, int> lengths)
    {
        int maxLength = lengths.Values.Max();
        var countPerLength = new int[maxLength + 1];
        foreach (var length in lengths.Values) countPerLength[length]++;
        var nextCode = new int[maxLength + 1];
        int code = 0;
        for (int bits = 1; bits <= maxLength; bits++)
        {
            code = (code + countPerLength[bits - 1]) << 1;
            nextCode[bits] = code;
        }
        var codes = new Dictionary<int, (int, int)>();
        foreach (var symbol in lengths.Keys.OrderBy(s => s))
            codes[symbol] = (nextCode[lengths[symbol]]++, lengths[symbol]);
        return codes;
    }

    private static Dictionary<int, (int code, int length)> WriteNormalCode(Vp8LTestBitWriter w, int alphabetSize, IReadOnlyDictionary<int, int> symbolLengths)
    {
        const int RepeatZerosLong = 18;
        var instructions = new List<(int clSymbol, int repeat)>();
        int position = 0;
        foreach (var symbol in symbolLengths.Keys.OrderBy(s => s).Append(alphabetSize))
        {
            int zeros = symbol - position;
            while (zeros >= 11)
            {
                int run = Math.Min(zeros, 138);
                instructions.Add((RepeatZerosLong, run));
                zeros -= run;
            }
            for (; zeros > 0; zeros--) instructions.Add((0, 0));
            if (symbol < alphabetSize) instructions.Add((symbolLengths[symbol], 0));
            position = symbol + 1;
        }

        var usedClSymbols = instructions.Select(i => i.clSymbol).Distinct().ToList();
        if (usedClSymbols.Count == 1) usedClSymbols.Add(usedClSymbols[0] == 0 ? 1 : 0);
        int clLength = 1;
        while ((1 << clLength) < usedClSymbols.Count) clLength++;
        var clLengths = usedClSymbols.ToDictionary(s => s, _ => clLength);
        var clCodes = CanonicalCodes(clLengths);

        int numCodeLengths = Math.Max(4, usedClSymbols.Max(s => Array.IndexOf(CodeLengthCodeOrder, s)) + 1);
        w.WriteBits(0, 1);
        w.WriteBits((uint)(numCodeLengths - 4), 4);
        for (int i = 0; i < numCodeLengths; i++)
            w.WriteBits((uint)(clLengths.TryGetValue(CodeLengthCodeOrder[i], out int length) ? length : 0), 3);
        w.WriteBits(0, 1);

        foreach (var (clSymbol, repeat) in instructions)
        {
            WriteCode(w, clCodes[clSymbol]);
            if (clSymbol == RepeatZerosLong) w.WriteBits((uint)(repeat - 11), 7);
        }

        return CanonicalCodes(symbolLengths);
    }
}
