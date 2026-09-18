using System;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// Vp8LDecoder tests build minimal valid VP8L bitstreams by hand via
/// Vp8LTestBitWriter (an independent implementation of the same documented
/// bit order), rather than depending on any real-world .webp file or an
/// external encoder -- there is no such fixture available in this repo, and
/// this makes each test a precise, traceable statement of exactly which
/// spec feature it exercises.
/// </summary>
public class Vp8LDecoderTests
{
    private static void WriteHeader(Vp8LTestBitWriter w, int width, int height, bool hasAlpha = false)
    {
        w.WriteBits((uint)(width - 1), 14);
        w.WriteBits((uint)(height - 1), 14);
        w.WriteBits(hasAlpha ? 1u : 0u, 1);
        w.WriteBits(0, 3); // version
    }

    [Fact]
    public void Decode_SinglePixel_NoTransformsNoCache_ReturnsExactColor()
    {
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 1, 1);
        w.WriteBits(0, 1); // no transforms
        w.WriteBits(0, 1); // no color cache
        w.WriteBits(0, 1); // no meta-huffman

        // One Huffman group: green(literal)=20, red=10, blue=30, alpha=255, distance unused.
        w.WriteSimpleSingleSymbol(20); // green
        w.WriteSimpleSingleSymbol(10); // red
        w.WriteSimpleSingleSymbol(30); // blue
        w.WriteSimpleSingleSymbol(255); // alpha
        w.WriteSimpleSingleSymbol(0); // distance (never read for a single literal)

        var result = Vp8LDecoder.Decode(w.ToVp8LPayload());

        result.Should().NotBeNull();
        var (width, height, argb) = result!.Value;
        width.Should().Be(1);
        height.Should().Be(1);
        argb.Should().Equal(new byte[] { 10, 20, 30, 255 }); // R,G,B,A
    }

    [Fact]
    public void Decode_TwoByOneImage_TwoDistinctLiteralPixels()
    {
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 2, 1);
        w.WriteBits(0, 1); w.WriteBits(0, 1); w.WriteBits(0, 1);

        // Two possible green values (0 and 1, fits the 1-bit simple form) so each
        // pixel can pick a different one via a real (non-degenerate) Huffman code.
        w.WriteBits(1, 1); // is_simple
        w.WriteBits(1, 1); // num_symbols - 1 = 1 (2 symbols)
        w.WriteBits(0, 1); // first symbol: 1-bit form
        w.WriteBits(0, 1); // first symbol value = 0
        w.WriteBits(1, 8); // second symbol value = 1 (always 8-bit form)

        w.WriteSimpleSingleSymbol(0);   // red: always 0
        w.WriteSimpleSingleSymbol(0);   // blue: always 0
        w.WriteSimpleSingleSymbol(255); // alpha: always opaque
        w.WriteSimpleSingleSymbol(0);   // distance unused

        // Pixel 0: green symbol 0 -> 1 bit (code "0"). Pixel 1: green symbol 1 -> 1 bit (code "1").
        w.WriteBits(0, 1);
        w.WriteBits(1, 1);

        var result = Vp8LDecoder.Decode(w.ToVp8LPayload());

        result.Should().NotBeNull();
        var (width, height, argb) = result!.Value;
        width.Should().Be(2);
        height.Should().Be(1);
        argb.Should().Equal(new byte[] { 0, 0, 0, 255, 0, 1, 0, 255 });
    }

    /// <summary>
    /// Writes a Green Huffman code (alphabet size 280 = 256 literal + 24
    /// length, no color cache) via the "normal" code-length-code path with
    /// exactly two non-zero-length symbols: 42 (a literal) and 256+lengthCode
    /// (a length code), both code length 1. Length codes are >= 256, which
    /// can't be expressed via the "simple" 8-bit shortcut (max value 255) --
    /// real encoders always need this path for anything with a backward
    /// reference, so it's worth constructing by hand rather than skipping.
    /// </summary>
    private static void WriteGreenTreeWithLiteralAndLengthCode(Vp8LTestBitWriter w, int literalSymbol, int lengthCode)
    {
        int lengthSymbol = 256 + lengthCode;

        w.WriteBits(0, 1); // is_simple = false (normal path)
        w.WriteBits(0, 4); // num_code_lengths - 4 = 0 -> num_code_lengths = 4

        // kCodeLengthCodeOrder = [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]
        // Only two cl-alphabet symbols (16 and 17, chosen as the two RLE-repeat
        // codes we need) get a real code length; everything else is 0/unused.
        // We use repeat code 18 (zero-run 11-138) to skip untouched symbols,
        // so the cl-tree only needs entries for "18" (order index 1) and a
        // literal code-length value: we use value 1 (order index 3) to mean
        // "this symbol has code length 1".
        w.WriteBits(0, 3); // order[0] = 17 -> length 0
        w.WriteBits(1, 3); // order[1] = 18 -> length 1
        w.WriteBits(0, 3); // order[2] = 0  -> length 0
        w.WriteBits(1, 3); // order[3] = 1  -> length 1

        w.WriteBits(0, 1); // no max_symbol truncation

        // Decode sequence (alphabet size 280), symbol pointer starts at 0:
        //  code 18 (bit "1"), repeat = literalSymbol zeros -> pointer reaches literalSymbol
        //  code  1 (bit "0"), literal length 1 -> codeLengths[literalSymbol] = 1, pointer++
        //  code 18 repeat(s) to skip up to lengthSymbol
        //  code  1, literal length 1 -> codeLengths[lengthSymbol] = 1, pointer++
        //  code 18 repeat(s) to cover the remaining tail up to 280
        WriteRepeatZero(w, literalSymbol);
        w.WriteBits(0, 1); // code "0" -> cl-symbol 1 (literal length 1)

        WriteRepeatZero(w, lengthSymbol - literalSymbol - 1);
        w.WriteBits(0, 1); // code "0" -> cl-symbol 1 (literal length 1)

        WriteRepeatZero(w, 280 - lengthSymbol - 1);
    }

    /// <summary>Emit enough repeat-zero (code 18, 11-138 per use) codes to skip exactly <paramref name="count"/> alphabet positions.</summary>
    private static void WriteRepeatZero(Vp8LTestBitWriter w, int count)
    {
        while (count > 0)
        {
            int chunk = count > 138 ? 138 : (count < 11 ? 11 : count);
            // If fewer than 11 remain, it's only reachable because our test
            // alphabet sizes were chosen so this never happens -- guard anyway.
            if (count < 11) chunk = 11;
            w.WriteBits(1, 1); // code "1" -> cl-symbol 18
            w.WriteBits((uint)(chunk - 11), 7);
            count -= chunk;
        }
    }

    [Fact]
    public void Decode_BackwardReference_CopiesPriorPixel()
    {
        // 3x1 image: pixel 0 is a literal green=42, pixels 1-2 are a length-2
        // backward reference at distance 1 (repeat the immediately preceding pixel).
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 3, 1);
        w.WriteBits(0, 1); // no transforms
        w.WriteBits(0, 1); // no color cache
        w.WriteBits(0, 1); // no meta-huffman

        // Length code 1 -> BaseValue[1] = 2, ExtraBits[1] = 0 -> length exactly 2.
        WriteGreenTreeWithLiteralAndLengthCode(w, literalSymbol: 42, lengthCode: 1);

        w.WriteSimpleSingleSymbol(10);  // red literal value (pixel 0 only; copies carry it forward)
        w.WriteSimpleSingleSymbol(20);  // blue literal value
        w.WriteSimpleSingleSymbol(255); // alpha
        // Distance code 1 -> ReadLengthOrDistance gives distCodeValue=2 (BaseValue[1]=2,
        // 0 extra bits) -> DistanceMap[2-1] = DistanceMap[1] = (dx=1, dy=0) -> a 1D
        // distance of dy*width+dx = 1: the immediately preceding pixel.
        w.WriteSimpleSingleSymbol(1);

        // Pixel stream: literal (green tree emits 42, then red/blue/alpha trees
        // each emit their single fixed symbol), then one length/distance pair.
        w.WriteBits(0, 1); // green tree has 2 symbols (42 then lengthSymbol), both 1-bit codes;
                            // canonical order assigns the SMALLER symbol value "0" -> 42 is symbol 42, lengthSymbol is 256+1=257, so 42 < 257 -> code "0" = 42.
        w.WriteBits(1, 1); // second green symbol: code "1" = lengthSymbol (the length-2 backward reference)

        var result = Vp8LDecoder.Decode(w.ToVp8LPayload());

        result.Should().NotBeNull();
        var (width, height, argb) = result!.Value;
        width.Should().Be(3);
        height.Should().Be(1);
        // All 3 pixels must be identical: pixel 0 is the literal, pixels 1-2 are copies of it.
        argb.Should().Equal(new byte[] { 10, 42, 20, 255, 10, 42, 20, 255, 10, 42, 20, 255 });
    }

    [Fact]
    public void Decode_SubtractGreenTransform_AddsGreenBackToRedAndBlue()
    {
        // Target final pixel: R=100, G=50, B=200, A=255. The bitstream carries
        // the *transformed* values (red-green, blue-green mod 256): 50, 150.
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 1, 1);

        w.WriteBits(1, 1); // transform present
        w.WriteBits(2, 2); // type 2 = SUBTRACT_GREEN
        w.WriteBits(0, 1); // no more transforms

        w.WriteBits(0, 1); w.WriteBits(0, 1); // no color cache, no meta-huffman

        w.WriteSimpleSingleSymbol(50);  // green (unaffected by this transform)
        w.WriteSimpleSingleSymbol(50);  // red' = 100 - 50
        w.WriteSimpleSingleSymbol(150); // blue' = 200 - 50
        w.WriteSimpleSingleSymbol(255); // alpha
        w.WriteSimpleSingleSymbol(0);   // distance unused

        var result = Vp8LDecoder.Decode(w.ToVp8LPayload());

        result.Should().NotBeNull();
        result!.Value.argb.Should().Equal(new byte[] { 100, 50, 200, 255 });
    }

    [Fact]
    public void Decode_ColorIndexingTransform_ExpandsPalette()
    {
        // 4x1 image, 3-color palette (<=4 -> 2 bits/index, 4 indices per byte,
        // so all 4 real pixels' indices pack into a single bundled green byte;
        // the main image is therefore encoded at bundled width 1, not 4).
        // Target final palette: entry0=(10,100,50,255), entry1=(40,100,50,255),
        // entry2=(70,100,50,255) -- entries after the first are DELTA-coded
        // (mod 256, per channel) against the previous entry.
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 4, 1);

        w.WriteBits(1, 1); // transform present
        w.WriteBits(3, 2); // type 3 = COLOR_INDEXING
        w.WriteBits(2, 8); // color_table_size - 1 = 2 -> 3 colors

        // Palette sub-image: width=3, height=1, decoded via the same
        // literal-only mechanism as any entropy-coded image (no transforms
        // of its own). Raw (pre-delta) per-channel values needed: green
        // [100,0,0], blue [50,0,0], alpha [255,0,0], red [10,30,30].
        // Sub-images decode with allowRecursion:false, which short-circuits
        // (never reads) the meta-huffman-image bit -- only a color-cache bit
        // is read here, unlike the top-level main image.
        w.WriteBits(0, 1); // no color cache

        var green = w.WriteSimpleTwoSymbol(0, 100);
        var red = w.WriteSimpleTwoSymbol(10, 30);
        var blue = w.WriteSimpleTwoSymbol(0, 50);
        var alpha = w.WriteSimpleTwoSymbol(0, 255);
        w.WriteSimpleSingleSymbol(0); // distance unused

        // Pixel order per pixel: green, alpha, red, blue.
        w.WriteBits((uint)green.bitForB, 1); w.WriteBits((uint)alpha.bitForB, 1); // entry0: green=100, alpha=255
        w.WriteBits((uint)red.bitForA, 1);   w.WriteBits((uint)blue.bitForB, 1);  // entry0: red=10, blue=50 (absolute)
        w.WriteBits((uint)green.bitForA, 1); w.WriteBits((uint)alpha.bitForA, 1); // entry1: green=0, alpha=0
        w.WriteBits((uint)red.bitForB, 1);   w.WriteBits((uint)blue.bitForA, 1);  // entry1: red=30, blue=0 (delta)
        w.WriteBits((uint)green.bitForA, 1); w.WriteBits((uint)alpha.bitForA, 1); // entry2: green=0, alpha=0
        w.WriteBits((uint)red.bitForB, 1);   w.WriteBits((uint)blue.bitForA, 1);  // entry2: red=30, blue=0 (delta)

        w.WriteBits(0, 1); // no more transforms (read only after the whole palette sub-image above)

        // Main image: 1 bundled pixel packing 4 real pixels' indices [0,1,2,0]
        // (2 bits each): 0 | (1<<2) | (2<<4) | (0<<6) = 36.
        w.WriteBits(0, 1); w.WriteBits(0, 1); // no color cache, no meta-huffman
        w.WriteSimpleSingleSymbol(36);  // green = packed indices
        w.WriteSimpleSingleSymbol(0);   // red (discarded after unbundling)
        w.WriteSimpleSingleSymbol(0);   // blue (discarded)
        w.WriteSimpleSingleSymbol(0);   // alpha (discarded)
        w.WriteSimpleSingleSymbol(0);   // distance unused

        var result = Vp8LDecoder.Decode(w.ToVp8LPayload());

        result.Should().NotBeNull();
        var (width, height, argb) = result!.Value;
        width.Should().Be(4);
        height.Should().Be(1);
        argb.Should().Equal(new byte[]
        {
            10, 100, 50, 255,  // index 0
            40, 100, 50, 255,  // index 1
            70, 100, 50, 255,  // index 2
            10, 100, 50, 255,  // index 0
        });
    }

    [Fact]
    public void Decode_PredictorTransform_LeftMode_AddsPrecedingPixel()
    {
        // 2x1 image. Pixel (0,0) is always the special-cased "opaque black"
        // predictor regardless of the block's assigned mode (only alpha +=
        // 0xFF). Pixel (1,0) sits on the top row (y=0, x!=0), which the
        // decoder hardcodes to mode 1 ("L", predict from the immediately
        // preceding pixel) without even consulting the per-block mode image --
        // so this test doesn't need to encode a meaningful block mode value.
        var w = new Vp8LTestBitWriter();
        WriteHeader(w, 2, 1);

        w.WriteBits(1, 1); // transform present
        w.WriteBits(0, 2); // type 0 = PREDICTOR
        w.WriteBits(0, 3); // size_bits - 2 = 0 -> block size 4 (one block covers this 2x1 image)

        // Block-mode sub-image: 1x1, allowRecursion:false. Its value is
        // irrelevant here (mode is hardcoded to 1 for the y=0 edge), but it
        // still has to be a valid, fully-consumed sub-stream.
        w.WriteBits(0, 1); // no color cache
        w.WriteSimpleSingleSymbol(0); // green (predictor mode, unused by this test)
        w.WriteSimpleSingleSymbol(0); w.WriteSimpleSingleSymbol(0); w.WriteSimpleSingleSymbol(0); w.WriteSimpleSingleSymbol(0);

        w.WriteBits(0, 1); // no more transforms

        w.WriteBits(0, 1); w.WriteBits(0, 1); // main image: no color cache, no meta-huffman

        // Raw (pre-transform) literal values: pixel0=(R5,G10,B15,A0), pixel1=(R2,G3,B4,A1).
        var green = w.WriteSimpleTwoSymbol(10, 3);
        var red = w.WriteSimpleTwoSymbol(5, 2);
        var blue = w.WriteSimpleTwoSymbol(15, 4);
        var alpha = w.WriteSimpleTwoSymbol(0, 1);
        w.WriteSimpleSingleSymbol(0); // distance unused

        w.WriteBits((uint)green.bitForA, 1); w.WriteBits((uint)alpha.bitForA, 1);
        w.WriteBits((uint)red.bitForA, 1);   w.WriteBits((uint)blue.bitForA, 1); // pixel0: G10,A0,R5,B15
        w.WriteBits((uint)green.bitForB, 1); w.WriteBits((uint)alpha.bitForB, 1);
        w.WriteBits((uint)red.bitForB, 1);   w.WriteBits((uint)blue.bitForB, 1); // pixel1: G3,A1,R2,B4

        var result = Vp8LDecoder.Decode(w.ToVp8LPayload());

        result.Should().NotBeNull();
        var (width, height, argb) = result!.Value;
        width.Should().Be(2);
        height.Should().Be(1);
        // pixel0 = raw + [0,0,0,0xFF] = (5,10,15,255).
        // pixel1 = raw + pixel0 (mode "L") = (2+5,3+10,4+15,(1+255)%256) = (7,13,19,0).
        argb.Should().Equal(new byte[] { 5, 10, 15, 255, 7, 13, 19, 0 });
    }

    [Fact]
    public void Decode_WrongSignatureByte_ReturnsNullWithoutThrowing()
    {
        var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 };
        var act = () => Vp8LDecoder.Decode(data);
        act.Should().NotThrow();
        act().Should().BeNull();
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
        w.WriteBits(0, 1); w.WriteBits(0, 1); w.WriteBits(0, 1);
        w.WriteSimpleSingleSymbol(1);
        var full = w.ToVp8LPayload();
        var truncated = new byte[length];
        Array.Copy(full, truncated, length);

        var act = () => Vp8LDecoder.Decode(truncated);
        act.Should().NotThrow("a truncated/malformed stream must degrade gracefully, per the project's infallible-parser convention");
    }
}
