using System;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class Vp8LDecoderTests
{
    private const int GreenAlphabetWithoutCache = 256 + 24;
    private const int CodeLengthRepeatZerosMin = 11;
    private const int CodeLengthRepeatZerosMax = 138;

    private static void WriteHeader(Vp8LTestBitWriter w, int width, int height, bool hasAlpha = false)
    {
        w.WriteBits((uint)(width - 1), 14);
        w.WriteBits((uint)(height - 1), 14);
        w.WriteBits(hasAlpha ? 1u : 0u, 1);
        w.WriteBits(0, 3);
    }

    private static void WriteLiteral(Vp8LTestBitWriter w, int greenBit, int redBit, int blueBit, int alphaBit)
    {
        w.WriteBits((uint)greenBit, 1);
        w.WriteBits((uint)redBit, 1);
        w.WriteBits((uint)blueBit, 1);
        w.WriteBits((uint)alphaBit, 1);
    }

    private static void WriteNoTransformNoCacheNoMetaCodes(Vp8LTestBitWriter w)
    {
        w.WriteBits(0, 1);
        w.WriteBits(0, 1);
        w.WriteBits(0, 1);
    }

    [Fact]
    public void Decode_SinglePixel_NoTransformsNoCache_ReturnsExactColor()
    {
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 1, 1);
        WriteNoTransformNoCacheNoMetaCodes(w);
        w.WriteSimpleSingleSymbol(20);
        w.WriteSimpleSingleSymbol(10);
        w.WriteSimpleSingleSymbol(30);
        w.WriteSimpleSingleSymbol(255);
        w.WriteSimpleSingleSymbol(0);

        var result = Vp8LDecoder.Decode(w.ToVp8LPayload());

        result.Should().NotBeNull();
        var (width, height, argb) = result!.Value;
        width.Should().Be(1);
        height.Should().Be(1);
        argb.Should().Equal(new byte[] { 10, 20, 30, 255 });
    }

    [Fact]
    public void Decode_TwoByOneImage_TwoDistinctLiteralPixels()
    {
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 2, 1);
        WriteNoTransformNoCacheNoMetaCodes(w);

        var green = w.WriteSimpleTwoSymbol(0, 1);
        w.WriteSimpleSingleSymbol(0);
        w.WriteSimpleSingleSymbol(0);
        w.WriteSimpleSingleSymbol(255);
        w.WriteSimpleSingleSymbol(0);

        w.WriteBits((uint)green.bitForA, 1);
        w.WriteBits((uint)green.bitForB, 1);

        var result = Vp8LDecoder.Decode(w.ToVp8LPayload());

        result.Should().NotBeNull();
        var (width, height, argb) = result!.Value;
        width.Should().Be(2);
        height.Should().Be(1);
        argb.Should().Equal(new byte[] { 0, 0, 0, 255, 0, 1, 0, 255 });
    }

    private static void WriteGreenTreeWithLiteralAndLengthCode(Vp8LTestBitWriter w, int literalSymbol, int lengthCode)
    {
        const uint CodeForLengthOne = 0;
        int lengthSymbol = 256 + lengthCode;

        w.WriteBits(0, 1);
        w.WriteBits(0, 4);
        w.WriteBits(0, 3);
        w.WriteBits(1, 3);
        w.WriteBits(0, 3);
        w.WriteBits(1, 3);
        w.WriteBits(0, 1);

        WriteRepeatZero(w, literalSymbol);
        w.WriteBits(CodeForLengthOne, 1);
        WriteRepeatZero(w, lengthSymbol - literalSymbol - 1);
        w.WriteBits(CodeForLengthOne, 1);
        WriteRepeatZero(w, GreenAlphabetWithoutCache - lengthSymbol - 1);
    }

    private static void WriteRepeatZero(Vp8LTestBitWriter w, int count)
    {
        const uint CodeForRepeatZeros = 1;
        while (count > 0)
        {
            int chunk = Math.Max(CodeLengthRepeatZerosMin, Math.Min(count, CodeLengthRepeatZerosMax));
            w.WriteBits(CodeForRepeatZeros, 1);
            w.WriteBits((uint)(chunk - CodeLengthRepeatZerosMin), 7);
            count -= chunk;
        }
    }

    [Fact]
    public void Decode_BackwardReference_CopiesPriorPixel()
    {
        const int LengthCodeForTwo = 1;
        const int DistanceCodeForPreviousPixel = 1;
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 3, 1);
        WriteNoTransformNoCacheNoMetaCodes(w);

        WriteGreenTreeWithLiteralAndLengthCode(w, literalSymbol: 42, lengthCode: LengthCodeForTwo);
        w.WriteSimpleSingleSymbol(10);
        w.WriteSimpleSingleSymbol(20);
        w.WriteSimpleSingleSymbol(255);
        w.WriteSimpleSingleSymbol(DistanceCodeForPreviousPixel);

        w.WriteBits(0, 1);
        w.WriteBits(1, 1);

        var result = Vp8LDecoder.Decode(w.ToVp8LPayload());

        result.Should().NotBeNull();
        var (width, height, argb) = result!.Value;
        width.Should().Be(3);
        height.Should().Be(1);
        argb.Should().Equal(new byte[] { 10, 42, 20, 255, 10, 42, 20, 255, 10, 42, 20, 255 });
    }

    [Fact]
    public void Decode_SubtractGreenTransform_AddsGreenBackToRedAndBlue()
    {
        const int SubtractGreen = 2;
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 1, 1);

        w.WriteBits(1, 1);
        w.WriteBits(SubtractGreen, 2);
        w.WriteBits(0, 1);

        w.WriteBits(0, 1);
        w.WriteBits(0, 1);

        w.WriteSimpleSingleSymbol(50);
        w.WriteSimpleSingleSymbol(100 - 50);
        w.WriteSimpleSingleSymbol(200 - 50);
        w.WriteSimpleSingleSymbol(255);
        w.WriteSimpleSingleSymbol(0);

        var result = Vp8LDecoder.Decode(w.ToVp8LPayload());

        result.Should().NotBeNull();
        result!.Value.argb.Should().Equal(new byte[] { 100, 50, 200, 255 });
    }

    [Fact]
    public void Decode_ColorIndexingTransform_ExpandsPalette()
    {
        const int ColorIndexing = 3;
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 4, 1);

        w.WriteBits(1, 1);
        w.WriteBits(ColorIndexing, 2);
        w.WriteBits(3 - 1, 8);

        w.WriteBits(0, 1);
        var green = w.WriteSimpleTwoSymbol(0, 100);
        var red = w.WriteSimpleTwoSymbol(10, 30);
        var blue = w.WriteSimpleTwoSymbol(0, 50);
        var alpha = w.WriteSimpleTwoSymbol(0, 255);
        w.WriteSimpleSingleSymbol(0);

        WriteLiteral(w, green.bitForB, red.bitForA, blue.bitForB, alpha.bitForB);
        WriteLiteral(w, green.bitForA, red.bitForB, blue.bitForA, alpha.bitForA);
        WriteLiteral(w, green.bitForA, red.bitForB, blue.bitForA, alpha.bitForA);

        w.WriteBits(0, 1);

        const int PackedIndices0120 = 0 | (1 << 2) | (2 << 4) | (0 << 6);
        w.WriteBits(0, 1);
        w.WriteBits(0, 1);
        w.WriteSimpleSingleSymbol(PackedIndices0120);
        w.WriteSimpleSingleSymbol(0);
        w.WriteSimpleSingleSymbol(0);
        w.WriteSimpleSingleSymbol(0);
        w.WriteSimpleSingleSymbol(0);

        var result = Vp8LDecoder.Decode(w.ToVp8LPayload());

        result.Should().NotBeNull();
        var (width, height, argb) = result!.Value;
        width.Should().Be(4);
        height.Should().Be(1);
        argb.Should().Equal(new byte[]
        {
            10, 100, 50, 255,
            40, 100, 50, 255,
            70, 100, 50, 255,
            10, 100, 50, 255,
        });
    }

    [Fact]
    public void Decode_PredictorTransform_LeftMode_AddsPrecedingPixel()
    {
        const int Predictor = 0;
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 2, 1);

        w.WriteBits(1, 1);
        w.WriteBits(Predictor, 2);
        w.WriteBits(0, 3);

        w.WriteBits(0, 1);
        w.WriteSimpleSingleSymbol(0);
        w.WriteSimpleSingleSymbol(0);
        w.WriteSimpleSingleSymbol(0);
        w.WriteSimpleSingleSymbol(0);
        w.WriteSimpleSingleSymbol(0);

        w.WriteBits(0, 1);

        w.WriteBits(0, 1);
        w.WriteBits(0, 1);

        var green = w.WriteSimpleTwoSymbol(10, 3);
        var red = w.WriteSimpleTwoSymbol(5, 2);
        var blue = w.WriteSimpleTwoSymbol(15, 4);
        var alpha = w.WriteSimpleTwoSymbol(0, 1);
        w.WriteSimpleSingleSymbol(0);

        WriteLiteral(w, green.bitForA, red.bitForA, blue.bitForA, alpha.bitForA);
        WriteLiteral(w, green.bitForB, red.bitForB, blue.bitForB, alpha.bitForB);

        var result = Vp8LDecoder.Decode(w.ToVp8LPayload());

        result.Should().NotBeNull();
        var (width, height, argb) = result!.Value;
        width.Should().Be(2);
        height.Should().Be(1);
        argb.Should().Equal(new byte[] { 5, 10, 15, 255, 7, 13, 19, 0 });
    }

    [Fact]
    public void Decode_WrongSignatureByte_ReturnsNull()
    {
        var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 };
        Vp8LDecoder.Decode(data).Should().BeNull("a VP8L stream must start with the 0x2F signature byte");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Decode_TruncatedAtVariousLengths_ReturnsNullWithoutThrowing(int length)
    {
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 4, 4);
        WriteNoTransformNoCacheNoMetaCodes(w);
        w.WriteSimpleSingleSymbol(1);
        var full = w.ToVp8LPayload();
        var truncated = new byte[length];
        Array.Copy(full, truncated, length);

        var act = () => Vp8LDecoder.Decode(truncated);
        act.Should().NotThrow("a truncated/malformed stream must degrade gracefully, per the project's infallible-parser convention");
        act().Should().BeNull("fewer than the five header bytes can never be an image");
    }
}
