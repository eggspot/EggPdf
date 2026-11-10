using System;
using System.Collections.Generic;
using System.IO;

namespace EggPdf.Text.TrueType;

public static partial class Woff2Decoder
{
    private const int GlyfTransformHeaderSize = 36;
    private const int GlyfStreamCount = 7;
    private const int OverlapSimpleBitmapOption = 0x0001;
    private const int OnCurvePoint = 0x01;
    private const int OverlapSimpleFlag = 0x40;
    private const int CompositeContourCount = -1;
    private const int ArgsAreWords = 0x0001, WeHaveAScale = 0x0008, MoreComponents = 0x0020,
        WeHaveAnXAndYScale = 0x0040, WeHaveATwoByTwo = 0x0080, WeHaveInstructions = 0x0100;
    private const int ProportionalLsbOmitted = 0x01, MonospacedLsbOmitted = 0x02;

    /// <summary>
    /// Reverses the glyf transform (WOFF 2.0 section 5.1): seven streams (contour counts, point counts, point flags,
    /// glyph data with triplet-encoded coordinates, composite records, bounding boxes, instructions) and an optional
    /// overlap bitmap become standard glyf and loca tables. A simple glyph's bounding box is computed from its points
    /// unless the bbox bitmap says it is stored; a composite's is always stored.
    /// </summary>
    private static bool ReconstructGlyf(byte[] stream, int offset, int length, out byte[] glyf, out byte[] loca)
    {
        var header = new Woff2Reader(stream, offset, length);
        header.Skip(2);
        int optionFlags = header.U16();
        int numGlyphs = header.U16();
        int indexFormat = header.U16();
        var streamSizes = new uint[GlyfStreamCount];
        for (int i = 0; i < GlyfStreamCount; i++) streamSizes[i] = header.U32();

        long next = offset + GlyfTransformHeaderSize;
        long end = (long)offset + length;
        Woff2Reader NextStream(long size)
        {
            if (next + size > end) throw new FormatException("A glyf stream runs past the transformed table.");
            var reader = new Woff2Reader(stream, (int)next, (int)size);
            next += size;
            return reader;
        }

        var contourCounts = NextStream(streamSizes[0]);
        var pointCounts = NextStream(streamSizes[1]);
        var pointFlags = NextStream(streamSizes[2]);
        var glyphData = NextStream(streamSizes[3]);
        var composites = NextStream(streamSizes[4]);
        var boxes = NextStream(streamSizes[5]);
        var instructions = NextStream(streamSizes[6]);

        int bitmapLength = 4 * ((numGlyphs + 31) / 32);
        var boxBitmap = boxes.Bytes(bitmapLength);
        byte[]? overlapBitmap = (optionFlags & OverlapSimpleBitmapOption) != 0 ? NextStream(bitmapLength).Bytes(bitmapLength) : null;

        var output = new GlyfWriter();
        var offsets = new long[numGlyphs + 1];
        for (int g = 0; g < numGlyphs; g++)
        {
            offsets[g] = output.Length;
            int contours = contourCounts.I16();
            bool storedBox = IsBitSet(boxBitmap, g);
            if (contours == 0)
            {
                if (storedBox) throw new FormatException("An empty glyph has a bounding box.");
                continue;
            }

            if (contours == CompositeContourCount)
            {
                if (!storedBox) throw new FormatException("A composite glyph has no bounding box.");
                WriteCompositeGlyph(output, composites, boxes, glyphData, instructions);
            }
            else if (contours > 0)
            {
                bool overlap = overlapBitmap != null && IsBitSet(overlapBitmap, g);
                WriteSimpleGlyph(output, contours, storedBox, overlap, pointCounts, pointFlags, glyphData, boxes, instructions);
            }
            else
            {
                throw new FormatException("A glyph has a negative contour count other than -1.");
            }
            output.Pad4();
        }
        offsets[numGlyphs] = output.Length;

        glyf = output.ToArray();
        loca = WriteLoca(offsets, indexFormat);
        return loca.Length > 0 || numGlyphs == 0;
    }

    private static void WriteCompositeGlyph(GlyfWriter output, Woff2Reader composites, Woff2Reader boxes,
        Woff2Reader glyphData, Woff2Reader instructions)
    {
        output.I16(CompositeContourCount);
        for (int i = 0; i < 4; i++) output.I16(boxes.I16());

        int start = composites.Position;
        bool haveInstructions = false;
        int flags;
        do
        {
            flags = composites.U16();
            haveInstructions |= (flags & WeHaveInstructions) != 0;
            int argumentBytes = (flags & ArgsAreWords) != 0 ? 4 : 2;
            int transformBytes = (flags & WeHaveAScale) != 0 ? 2
                : (flags & WeHaveAnXAndYScale) != 0 ? 4
                : (flags & WeHaveATwoByTwo) != 0 ? 8 : 0;
            composites.Skip(2 + argumentBytes + transformBytes);
        } while ((flags & MoreComponents) != 0);
        output.Bytes(composites.Data, start, composites.Position - start);

        if (!haveInstructions) return;
        int instructionLength = glyphData.U255();
        output.U16(instructionLength);
        output.Bytes(instructions.Data, instructions.Position, instructionLength);
        instructions.Skip(instructionLength);
    }

    /// <summary>
    /// A simple glyph in plain glyf form: every point flag holds only the on-curve bit (and the overlap bit on the
    /// first point), and every coordinate is a 16-bit delta.
    /// </summary>
    private static void WriteSimpleGlyph(GlyfWriter output, int contours, bool storedBox, bool overlap,
        Woff2Reader pointCounts, Woff2Reader pointFlags, Woff2Reader glyphData, Woff2Reader boxes, Woff2Reader instructions)
    {
        var endPoints = new int[contours];
        int pointCount = 0;
        for (int c = 0; c < contours; c++)
        {
            pointCount += pointCounts.U255();
            if (pointCount > ushort.MaxValue + 1) throw new FormatException("A glyph has too many points.");
            endPoints[c] = pointCount - 1;
        }

        var flags = pointFlags.Bytes(pointCount);
        var xs = new int[pointCount];
        var ys = new int[pointCount];
        DecodeTriplets(flags, glyphData, xs, ys);
        int instructionLength = glyphData.U255();

        int xMin = 0, yMin = 0, xMax = 0, yMax = 0;
        if (storedBox)
        {
            xMin = boxes.I16(); yMin = boxes.I16(); xMax = boxes.I16(); yMax = boxes.I16();
        }
        else if (pointCount > 0)
        {
            xMin = xMax = xs[0];
            yMin = yMax = ys[0];
            for (int i = 1; i < pointCount; i++)
            {
                xMin = Math.Min(xMin, xs[i]); xMax = Math.Max(xMax, xs[i]);
                yMin = Math.Min(yMin, ys[i]); yMax = Math.Max(yMax, ys[i]);
            }
        }

        output.I16(contours);
        output.I16(xMin); output.I16(yMin); output.I16(xMax); output.I16(yMax);
        foreach (int endPoint in endPoints) output.U16(endPoint);
        output.U16(instructionLength);
        output.Bytes(instructions.Data, instructions.Position, instructionLength);
        instructions.Skip(instructionLength);

        for (int i = 0; i < pointCount; i++)
        {
            int flag = (flags[i] & 0x80) == 0 ? OnCurvePoint : 0;
            if (i == 0 && overlap) flag |= OverlapSimpleFlag;
            output.U8(flag);
        }
        int previous = 0;
        for (int i = 0; i < pointCount; i++) { output.I16(xs[i] - previous); previous = xs[i]; }
        previous = 0;
        for (int i = 0; i < pointCount; i++) { output.I16(ys[i] - previous); previous = ys[i]; }
    }

    /// <summary>
    /// The triplet encoding of WOFF 2.0 section 5.2: each point's flag byte (bit 7 set = off-curve) picks how many
    /// data bytes follow and how they split into an x and a y delta; the low bits pick each delta's sign.
    /// </summary>
    private static void DecodeTriplets(byte[] flags, Woff2Reader data, int[] xs, int[] ys)
    {
        int x = 0, y = 0;
        for (int i = 0; i < flags.Length; i++)
        {
            int flag = flags[i] & 0x7F;
            int dx, dy;
            if (flag < 10)
            {
                dx = 0;
                dy = WithSign(flag, ((flag & 14) << 7) + data.U8());
            }
            else if (flag < 20)
            {
                dx = WithSign(flag, (((flag - 10) & 14) << 7) + data.U8());
                dy = 0;
            }
            else if (flag < 84)
            {
                int b0 = flag - 20, b1 = data.U8();
                dx = WithSign(flag, 1 + (b0 & 0x30) + (b1 >> 4));
                dy = WithSign(flag >> 1, 1 + ((b0 & 0x0C) << 2) + (b1 & 0x0F));
            }
            else if (flag < 120)
            {
                int b0 = flag - 84;
                dx = WithSign(flag, 1 + ((b0 / 12) << 8) + data.U8());
                dy = WithSign(flag >> 1, 1 + (((b0 % 12) >> 2) << 8) + data.U8());
            }
            else if (flag < 124)
            {
                int b0 = data.U8(), b1 = data.U8(), b2 = data.U8();
                dx = WithSign(flag, (b0 << 4) + (b1 >> 4));
                dy = WithSign(flag >> 1, ((b1 & 0x0F) << 8) + b2);
            }
            else
            {
                int b0 = data.U8(), b1 = data.U8(), b2 = data.U8(), b3 = data.U8();
                dx = WithSign(flag, (b0 << 8) + b1);
                dy = WithSign(flag >> 1, (b2 << 8) + b3);
            }
            x += dx;
            y += dy;
            xs[i] = x;
            ys[i] = y;
        }
    }

    private static int WithSign(int flag, int value) => (flag & 1) != 0 ? value : -value;

    private static bool IsBitSet(byte[] bitmap, int index) => (bitmap[index >> 3] & (0x80 >> (index & 7))) != 0;

    private static byte[] WriteLoca(long[] offsets, int indexFormat)
    {
        bool shortFormat = indexFormat == 0;
        var loca = new byte[offsets.Length * (shortFormat ? 2 : 4)];
        for (int i = 0; i < offsets.Length; i++)
        {
            if (shortFormat)
            {
                if (offsets[i] > 0x1FFFE) return Array.Empty<byte>();
                WriteU16(loca, i * 2, (int)(offsets[i] / 2));
            }
            else
            {
                if (offsets[i] > uint.MaxValue) return Array.Empty<byte>();
                WriteU32(loca, i * 4, (uint)offsets[i]);
            }
        }
        return loca;
    }

    /// <summary>
    /// Reverses the hmtx transform (WOFF 2.0 section 5.4): the advances are stored, and left side bearings the flags
    /// say were omitted equal each glyph's xMin from the reconstructed glyf table (0 for an empty glyph).
    /// </summary>
    private static byte[]? ReconstructHmtx(byte[] stream, int offset, int length, SortedDictionary<uint, byte[]> tables)
    {
        if (!tables.TryGetValue(MaxpTag, out var maxp) || maxp.Length < MaxpNumGlyphsOffset + 2) return null;
        if (!tables.TryGetValue(HheaTag, out var hhea) || hhea.Length < HheaNumberOfHMetricsOffset + 2) return null;
        if (!tables.TryGetValue(GlyfTag, out var glyf) || !tables.TryGetValue(LocaTag, out var loca)) return null;

        int numGlyphs = (maxp[MaxpNumGlyphsOffset] << 8) | maxp[MaxpNumGlyphsOffset + 1];
        int numHMetrics = (hhea[HheaNumberOfHMetricsOffset] << 8) | hhea[HheaNumberOfHMetricsOffset + 1];
        if (numHMetrics < 1 || numHMetrics > numGlyphs) return null;

        var reader = new Woff2Reader(stream, offset, length);
        int flags = reader.U8();
        bool proportionalOmitted = (flags & ProportionalLsbOmitted) != 0;
        bool monospacedOmitted = (flags & MonospacedLsbOmitted) != 0;

        var hmtx = new byte[numHMetrics * 4 + (numGlyphs - numHMetrics) * 2];
        for (int i = 0; i < numHMetrics; i++) WriteU16(hmtx, i * 4, reader.U16());
        for (int i = 0; i < numHMetrics; i++)
            WriteU16(hmtx, i * 4 + 2, proportionalOmitted ? GlyphXMin(glyf, loca, numGlyphs, i) : reader.U16());
        for (int i = numHMetrics; i < numGlyphs; i++)
            WriteU16(hmtx, numHMetrics * 4 + (i - numHMetrics) * 2, monospacedOmitted ? GlyphXMin(glyf, loca, numGlyphs, i) : reader.U16());
        return hmtx;
    }

    private static int GlyphXMin(byte[] glyf, byte[] loca, int numGlyphs, int glyph)
    {
        bool shortFormat = loca.Length == (numGlyphs + 1) * 2;
        long start = shortFormat ? ((loca[glyph * 2] << 8) | loca[glyph * 2 + 1]) * 2L : ReadU32(loca, glyph * 4);
        long next = shortFormat ? ((loca[glyph * 2 + 2] << 8) | loca[glyph * 2 + 3]) * 2L : ReadU32(loca, glyph * 4 + 4);
        if (next <= start || start + 4 > glyf.Length) return 0;
        return (short)((glyf[start + 2] << 8) | glyf[start + 3]);
    }

    /// <summary>A bounds-checked big-endian reader over part of a byte array; reading past the part throws.</summary>
    private sealed class Woff2Reader
    {
        private readonly int _end;

        public Woff2Reader(byte[] data, int offset, int length)
        {
            if (offset < 0 || length < 0 || (long)offset + length > data.Length)
                throw new FormatException("A WOFF2 structure runs past its data.");
            Data = data;
            Position = offset;
            _end = offset + length;
        }

        public byte[] Data { get; }

        public int Position { get; private set; }

        public void Seek(int position)
        {
            if (position < 0 || position > _end) throw new FormatException("A WOFF2 offset is out of range.");
            Position = position;
        }

        public void Skip(int count)
        {
            Require(count);
            Position += count;
        }

        public int U8()
        {
            Require(1);
            return Data[Position++];
        }

        public int U16()
        {
            Require(2);
            int value = (Data[Position] << 8) | Data[Position + 1];
            Position += 2;
            return value;
        }

        public int I16() => (short)U16();

        public uint U32()
        {
            Require(4);
            uint value = ReadU32(Data, Position);
            Position += 4;
            return value;
        }

        public byte[] Bytes(int count)
        {
            Require(count);
            var bytes = new byte[count];
            Buffer.BlockCopy(Data, Position, bytes, 0, count);
            Position += count;
            return bytes;
        }

        /// <summary>UIntBase128: up to five bytes of 7 bits each, high bit set on all but the last, no leading zero byte.</summary>
        public uint UIntBase128()
        {
            uint value = 0;
            for (int i = 0; i < 5; i++)
            {
                int b = U8();
                if (i == 0 && b == 0x80) throw new FormatException("A UIntBase128 has a leading zero byte.");
                if ((value & 0xFE000000) != 0) throw new FormatException("A UIntBase128 overflows 32 bits.");
                value = (value << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0) return value;
            }
            throw new FormatException("A UIntBase128 is longer than five bytes.");
        }

        /// <summary>255UInt16: one byte below 253, or 253 then a 16-bit value, 254 then a byte plus 506, 255 then a byte plus 253.</summary>
        public int U255()
        {
            int code = U8();
            switch (code)
            {
                case 253: return U16();
                case 254: return U8() + 506;
                case 255: return U8() + 253;
                default: return code;
            }
        }

        private void Require(int count)
        {
            if (count < 0 || Position + (long)count > _end) throw new FormatException("WOFF2 data is truncated.");
        }
    }

    /// <summary>A growable big-endian byte buffer for the reconstructed glyf table.</summary>
    private sealed class GlyfWriter
    {
        private readonly MemoryStream _buffer = new();

        public long Length => _buffer.Length;

        public void U8(int value) => _buffer.WriteByte((byte)value);

        public void U16(int value)
        {
            _buffer.WriteByte((byte)(value >> 8));
            _buffer.WriteByte((byte)value);
        }

        public void I16(int value)
        {
            if (value < short.MinValue || value > short.MaxValue) throw new FormatException("A glyph value overflows 16 bits.");
            U16(value);
        }

        public void Bytes(byte[] source, int offset, int count)
        {
            if (count < 0 || (long)offset + count > source.Length) throw new FormatException("WOFF2 data is truncated.");
            _buffer.Write(source, offset, count);
        }

        public void Pad4()
        {
            while ((_buffer.Length & 3) != 0) _buffer.WriteByte(0);
        }

        public byte[] ToArray() => _buffer.ToArray();
    }
}
