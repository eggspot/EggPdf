using System;
using System.Collections.Generic;

namespace EggPdf.Pdf;

/// <summary>
/// Decodes WebP Lossless (VP8L) image data to raw ARGB pixels, per the WebP
/// Lossless Bitstream Specification: header, up to 4 transforms (predictor,
/// color, subtract-green, color-indexing), prefix (Huffman) code groups
/// (optionally per-block via a meta-Huffman image), an optional color cache,
/// and the main pixel stream of literals and LZ77-style backward references.
/// </summary>
public static class Vp8LDecoder
{
    /// <summary>Decode a VP8L chunk payload (the bytes after the "VP8L" FourCC and chunk size) to raw ARGB8888 pixels, row-major, top-to-bottom.</summary>
    public static (int width, int height, byte[] argb)? Decode(byte[] data)
    {
        if (data == null || data.Length < 5 || data[0] != 0x2F)
            return null;

        try
        {
            var br = new BitReader(data, 1);
            int width = (int)br.ReadBits(14) + 1;
            int height = (int)br.ReadBits(14) + 1;
            br.ReadBits(1); // alpha_is_used -- informational only, decoding path is unaffected
            int version = (int)br.ReadBits(3);
            if (version != 0) return null;

            var transforms = new List<Transform>();
            while (br.ReadBits(1) != 0)
            {
                int type = (int)br.ReadBits(2);
                transforms.Add(ReadTransform(br, type, width, height));
            }

            // A color-indexing transform with a small (<=16 color) palette packs
            // several pixels' indices into one bundled pixel's green channel --
            // the main pixel stream is then encoded at that narrower bundled
            // width, not the real image width.
            int decodeWidth = width;
            foreach (var t in transforms)
            {
                if (t.Type == TransformType.ColorIndexing && t.PaletteSize <= 16)
                {
                    int bitsPerIndex = t.PaletteSize <= 2 ? 1 : t.PaletteSize <= 4 ? 2 : 4;
                    int indicesPerByte = 8 / bitsPerIndex;
                    decodeWidth = (width + indicesPerByte - 1) / indicesPerByte;
                }
            }

            var argb = DecodeImageStream(br, decodeWidth, height, allowRecursion: true);

            // Undo transforms in reverse of the order they were read (they were
            // applied in that order during encoding, most-recent-first to undo).
            // Color-indexing unbundling can grow the array from decodeWidth to
            // the real width, so each step's result feeds the next.
            for (int i = transforms.Count - 1; i >= 0; i--)
                argb = InverseTransform(transforms[i], argb, width, height);

            return (width, height, argb);
        }
        catch
        {
            // Infallible per project convention: a malformed/unsupported stream
            // yields "couldn't decode" rather than throwing.
            return null;
        }
    }

    // ===== Transforms =====

    private enum TransformType { Predictor = 0, Color = 1, SubtractGreen = 2, ColorIndexing = 3 }

    private struct Transform
    {
        public TransformType Type;
        public int Bits;          // block size_bits (predictor/color transforms)
        public byte[]? Image;     // ARGB sub-image carrying per-block transform params (predictor/color)
        public int ImageWidth;    // width of that sub-image, in blocks
        public byte[]? Palette;   // ARGB color table (color-indexing transform)
        public int PaletteSize;
    }

    private static Transform ReadTransform(BitReader br, int type, int width, int height)
    {
        var t = new Transform { Type = (TransformType)type };
        switch (t.Type)
        {
            case TransformType.Predictor:
            case TransformType.Color:
            {
                t.Bits = (int)br.ReadBits(3) + 2;
                int blockW = (width + (1 << t.Bits) - 1) >> t.Bits;
                int blockH = (height + (1 << t.Bits) - 1) >> t.Bits;
                t.ImageWidth = blockW;
                t.Image = DecodeImageStream(br, blockW, blockH, allowRecursion: false);
                break;
            }
            case TransformType.SubtractGreen:
                break;
            case TransformType.ColorIndexing:
            {
                int size = (int)br.ReadBits(8) + 1;
                t.PaletteSize = size;
                var img = DecodeImageStream(br, size, 1, allowRecursion: false);
                // Palette entries are delta-coded (each after the first is a sum,
                // mod 256 per channel, of itself and all preceding entries).
                var palette = new byte[size * 4];
                Array.Copy(img, 0, palette, 0, 4);
                for (int i = 1; i < size; i++)
                {
                    for (int c = 0; c < 4; c++)
                        palette[i * 4 + c] = (byte)(img[i * 4 + c] + palette[(i - 1) * 4 + c]);
                }
                t.Palette = palette;
                break;
            }
        }
        return t;
    }

    /// <summary>Applies one inverse transform, returning the (possibly resized -- color-indexing unbundling grows the array) result.</summary>
    private static byte[] InverseTransform(Transform t, byte[] argb, int width, int height)
    {
        switch (t.Type)
        {
            case TransformType.SubtractGreen:
                for (int i = 0; i < width * height; i++)
                {
                    byte g = argb[i * 4 + 1];
                    argb[i * 4 + 0] = (byte)(argb[i * 4 + 0] + g); // red
                    argb[i * 4 + 2] = (byte)(argb[i * 4 + 2] + g); // blue
                }
                return argb;

            case TransformType.ColorIndexing:
                return InverseColorIndexing(t, argb, width, height);

            case TransformType.Color:
                InverseColorTransform(t, argb, width, height);
                return argb;

            case TransformType.Predictor:
                InversePredictorTransform(t, argb, width, height);
                return argb;

            default:
                return argb;
        }
    }

    private static byte[] InverseColorIndexing(Transform t, byte[] argb, int width, int height)
    {
        int paletteSize = t.PaletteSize;
        var palette = t.Palette!;

        if (paletteSize <= 16)
        {
            // Indices were bundled: several pixels' index values packed into one
            // byte's green channel (8/bits-per-index indices per source pixel),
            // and the *bundled* image is narrower than the real image.
            int bitsPerIndex = paletteSize <= 2 ? 1 : paletteSize <= 4 ? 2 : 4;
            int indicesPerByte = 8 / bitsPerIndex;
            int bundledWidth = (width + indicesPerByte - 1) / indicesPerByte;

            var expanded = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcX = x / indicesPerByte;
                    int shift = (x % indicesPerByte) * bitsPerIndex;
                    byte packed = argb[(y * bundledWidth + srcX) * 4 + 1];
                    int index = (packed >> shift) & ((1 << bitsPerIndex) - 1);
                    if (index >= paletteSize) index = 0;
                    Array.Copy(palette, index * 4, expanded, (y * width + x) * 4, 4);
                }
            }
            return expanded;
        }
        else
        {
            for (int i = 0; i < width * height; i++)
            {
                int index = argb[i * 4 + 1];
                if (index >= paletteSize) index = 0;
                Array.Copy(palette, index * 4, argb, i * 4, 4);
            }
            return argb;
        }
    }

    private static sbyte ColorTransformDelta(sbyte t, sbyte c)
    {
        return (sbyte)(((int)t * (int)c) >> 5);
    }

    private static void InverseColorTransform(Transform t, byte[] argb, int width, int height)
    {
        int bits = t.Bits;
        var blockImg = t.Image!;
        int blockImgW = t.ImageWidth;

        for (int y = 0; y < height; y++)
        {
            int blockY = y >> bits;
            for (int x = 0; x < width; x++)
            {
                int blockX = x >> bits;
                int bi = (blockY * blockImgW + blockX) * 4;
                sbyte greenToRed = (sbyte)blockImg[bi + 1];
                sbyte greenToBlue = (sbyte)blockImg[bi + 2];
                sbyte redToBlue = (sbyte)blockImg[bi + 0];

                int pi = (y * width + x) * 4;
                byte red = argb[pi + 0];
                byte green = argb[pi + 1];
                byte blue = argb[pi + 2];

                red = (byte)(red + ColorTransformDelta(greenToRed, (sbyte)green));
                blue = (byte)(blue + ColorTransformDelta(greenToBlue, (sbyte)green));
                blue = (byte)(blue + ColorTransformDelta(redToBlue, (sbyte)red));

                argb[pi + 0] = red;
                argb[pi + 2] = blue;
            }
        }
    }

    private static void InversePredictorTransform(Transform t, byte[] argb, int width, int height)
    {
        int bits = t.Bits;
        var blockImg = t.Image!;
        int blockImgW = t.ImageWidth;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pi = (y * width + x) * 4;
                if (x == 0 && y == 0)
                {
                    // First pixel predicts as opaque black (R=G=B=0, A=0xFF).
                    argb[pi + 3] = (byte)(argb[pi + 3] + 0xFF);
                    continue;
                }

                int mode = x == 0
                    ? 2  // left edge: predict from pixel above (mode "T")
                    : y == 0
                        ? 1 // top edge: predict from pixel to the left (mode "L")
                        : blockImg[((y >> bits) * blockImgW + (x >> bits)) * 4 + 1];

                // On the top row (y=0, x>0) there is no row above at all --
                // mode is hardcoded to 1 ("L") there, which never reads T/TL/TR,
                // but Predict still builds all four neighbor pixels unconditionally
                // before dispatching on mode, so up/upLeft/upRight must still be
                // valid (in-bounds) indices even though their values are unused;
                // falling back to "left" keeps them safely in range.
                int left = x > 0 ? pi - 4 : pi - width * 4;
                int up = y > 0 ? pi - width * 4 : left;
                int upLeft = y > 0 ? (x > 0 ? up - 4 : up) : left;
                int upRight = y > 0 ? (x < width - 1 ? up + 4 : up) : left;

                var pred = Predict(mode, argb, left, up, upLeft, upRight);
                for (int c = 0; c < 4; c++)
                    argb[pi + c] = (byte)(argb[pi + c] + pred[c]);
            }
        }
    }

    /// <summary>The 14 WebP lossless predictor modes; each returns a 4-channel [R,G,B,A] prediction.</summary>
    private static int[] Predict(int mode, byte[] a, int left, int up, int upLeft, int upRight)
    {
        int[] L = { a[left], a[left + 1], a[left + 2], a[left + 3] };
        int[] T = { a[up], a[up + 1], a[up + 2], a[up + 3] };
        int[] TL = { a[upLeft], a[upLeft + 1], a[upLeft + 2], a[upLeft + 3] };
        int[] TR = { a[upRight], a[upRight + 1], a[upRight + 2], a[upRight + 3] };

        switch (mode)
        {
            case 0: return new[] { 0, 0, 0, 0xFF };
            case 1: return L;
            case 2: return T;
            case 3: return TR;
            case 4: return TL;
            case 5: return Avg(Avg(L, TR), T);
            case 6: return Avg(L, TL);
            case 7: return Avg(L, T);
            case 8: return Avg(TL, T);
            case 9: return Avg(T, TR);
            case 10: return Avg(Avg(L, TL), Avg(T, TR));
            case 11: return Select(T, L, TL);
            case 12: return ClampAddSubtractFull(L, T, TL);
            default: return ClampAddSubtractHalf(Avg(L, T), TL);
        }
    }

    private static int[] Avg(int[] a, int[] b)
    {
        var r = new int[4];
        for (int i = 0; i < 4; i++) r[i] = (a[i] + b[i]) >> 1;
        return r;
    }

    private static int Clamp255(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

    private static int[] ClampAddSubtractFull(int[] a, int[] b, int[] c)
    {
        var r = new int[4];
        for (int i = 0; i < 4; i++) r[i] = Clamp255(a[i] + b[i] - c[i]);
        return r;
    }

    private static int[] ClampAddSubtractHalf(int[] avgAB, int[] c)
    {
        var r = new int[4];
        for (int i = 0; i < 4; i++)
        {
            int a = avgAB[i];
            int diff = a - c[i];
            r[i] = Clamp255(a + diff / 2);
        }
        return r;
    }

    /// <summary>
    /// "Select" predictor (mode 11). Matches libwebp's Select(a=T, b=L, c=TL):
    /// per channel, Sub3 = |b-c| - |c-a|; summed across channels, choose a (T)
    /// if the sum is &lt;= 0, else b (L). Not simple "closer to TL wins" --
    /// implemented to match the spec exactly rather than by intuition.
    /// </summary>
    private static int[] Select(int[] a, int[] b, int[] c)
    {
        int sum = 0;
        for (int i = 0; i < 4; i++)
            sum += Math.Abs(b[i] - c[i]) - Math.Abs(c[i] - a[i]);
        return sum <= 0 ? a : b;
    }

    // ===== Image stream decoding (Huffman groups + pixel loop) =====

    private static byte[] DecodeImageStream(BitReader br, int width, int height, bool allowRecursion)
    {
        int colorCacheBits = 0;
        if (br.ReadBits(1) != 0)
            colorCacheBits = (int)br.ReadBits(4);

        // Optional meta-Huffman image: assigns a Huffman-group index per block.
        byte[]? huffmanImage = null;
        int huffmanBits = 0;
        int huffmanImageW = 0;
        int numGroups = 1;
        if (allowRecursion && br.ReadBits(1) != 0)
        {
            huffmanBits = (int)br.ReadBits(3) + 2;
            huffmanImageW = (width + (1 << huffmanBits) - 1) >> huffmanBits;
            int huffmanImageH = (height + (1 << huffmanBits) - 1) >> huffmanBits;
            huffmanImage = DecodeImageStream(br, huffmanImageW, huffmanImageH, allowRecursion: false);

            int maxIndex = 0;
            for (int i = 0; i < huffmanImageW * huffmanImageH; i++)
            {
                int idx = (huffmanImage[i * 4 + 1] << 8) | huffmanImage[i * 4 + 2];
                if (idx > maxIndex) maxIndex = idx;
            }
            numGroups = maxIndex + 1;
        }

        var groups = new HuffmanGroup[numGroups];
        for (int i = 0; i < numGroups; i++)
            groups[i] = ReadHuffmanGroup(br, colorCacheBits);

        int cacheSize = colorCacheBits > 0 ? 1 << colorCacheBits : 0;
        var cache = cacheSize > 0 ? new uint[cacheSize] : null;

        var argb = new byte[width * height * 4];
        int pos = 0;
        int total = width * height;

        while (pos < total)
        {
            HuffmanGroup group;
            if (huffmanImage != null)
            {
                int px = pos % width, py = pos / width;
                int bi = ((py >> huffmanBits) * huffmanImageW + (px >> huffmanBits)) * 4;
                int idx = (huffmanImage[bi + 1] << 8) | huffmanImage[bi + 2];
                group = groups[idx];
            }
            else group = groups[0];

            int green = ReadSymbol(br, group.Green);
            if (green < 256)
            {
                byte a = (byte)ReadSymbol(br, group.Alpha);
                byte r = (byte)ReadSymbol(br, group.Red);
                byte b = (byte)ReadSymbol(br, group.Blue);
                WritePixel(argb, pos, a, r, (byte)green, b);
                if (cache != null) cache[HashPixel(argb, pos, cacheBits: colorCacheBits)] = ReadPixel(argb, pos);
                pos++;
            }
            else if (green < 256 + 24)
            {
                int lengthCode = green - 256;
                int length = ReadLengthOrDistance(br, lengthCode);
                int distCode = ReadSymbol(br, group.Distance);
                int distance = DecodeDistance(br, distCode, width);

                int srcPos = pos - distance;
                if (srcPos < 0) return argb; // malformed: bail out with what we have
                for (int k = 0; k < length && pos < total; k++, pos++, srcPos++)
                {
                    Array.Copy(argb, srcPos * 4, argb, pos * 4, 4);
                    if (cache != null) cache[HashPixel(argb, pos, cacheBits: colorCacheBits)] = ReadPixel(argb, pos);
                }
            }
            else
            {
                int cacheIndex = green - 256 - 24;
                if (cache == null || cacheIndex >= cache.Length) return argb;
                WritePixelRaw(argb, pos, cache[cacheIndex]);
                pos++;
            }
        }

        return argb;
    }

    private static void WritePixel(byte[] argb, int pos, byte a, byte r, byte g, byte b)
    {
        argb[pos * 4 + 0] = r;
        argb[pos * 4 + 1] = g;
        argb[pos * 4 + 2] = b;
        argb[pos * 4 + 3] = a;
    }

    private static void WritePixelRaw(byte[] argb, int pos, uint val)
    {
        argb[pos * 4 + 0] = (byte)(val >> 16);
        argb[pos * 4 + 1] = (byte)(val >> 8);
        argb[pos * 4 + 2] = (byte)val;
        argb[pos * 4 + 3] = (byte)(val >> 24);
    }

    private static uint ReadPixel(byte[] argb, int pos)
        => ((uint)argb[pos * 4 + 3] << 24) | ((uint)argb[pos * 4 + 0] << 16) | ((uint)argb[pos * 4 + 1] << 8) | argb[pos * 4 + 2];

    private static int HashPixel(byte[] argb, int pos, int cacheBits)
    {
        uint p = ReadPixel(argb, pos);
        return (int)((p * 0x1e35a7bdu) >> (32 - cacheBits));
    }

    // Length/distance prefix codes 0-23 share the same extra-bits table.
    private static readonly int[] ExtraBits =
    {
        0,0,0,0,1,1,2,2,3,3,4,4,5,5,6,6,7,7,8,8,9,9,10,10
    };
    private static readonly int[] BaseValue =
    {
        1,2,3,4,5,7,9,13,17,25,33,49,65,97,129,193,257,385,513,769,1025,1537,2049,3073
    };

    private static int ReadLengthOrDistance(BitReader br, int code)
    {
        if (code >= ExtraBits.Length) return 1;
        int extra = ExtraBits[code];
        int val = BaseValue[code];
        if (extra > 0) val += (int)br.ReadBits(extra);
        return val;
    }

    private static int DecodeDistance(BitReader br, int code, int width)
    {
        int distCodeValue = ReadLengthOrDistance(br, code);
        if (distCodeValue <= 120)
            return MapDistance(distCodeValue, width);
        return distCodeValue - 120;
    }

    // Special-case mapping for small distances (nearby pixels), per spec Table.
    private static readonly (int x, int y)[] DistanceMap =
    {
        (0,1),(1,0),(1,1),(-1,1),(0,2),(2,0),(1,2),(-1,2),(2,1),(-2,1),(2,2),(-2,2),
        (0,3),(3,0),(1,3),(-1,3),(3,1),(-3,1),(2,3),(-2,3),(3,2),(-3,2),(0,4),(4,0),
        (1,4),(-1,4),(4,1),(-4,1),(3,3),(-3,3),(2,4),(-2,4),(4,2),(-4,2),(0,5),(3,4),
        (-3,4),(4,3),(-4,3),(5,0),(1,5),(-1,5),(5,1),(-5,1),(2,5),(-2,5),(5,2),(-5,2),
        (4,4),(-4,4),(3,5),(-3,5),(5,3),(-5,3),(0,6),(6,0),(1,6),(-1,6),(6,1),(-6,1),
        (2,6),(-2,6),(6,2),(-6,2),(5,4),(-5,4),(4,5),(-4,5),(0,7),(7,0),(3,6),(-3,6),
        (6,3),(-6,3),(1,7),(-1,7),(7,1),(-7,1),(5,5),(-5,5),(2,7),(-2,7),(7,2),(-7,2),
        (4,6),(-4,6),(6,4),(-6,4),(3,7),(-3,7),(7,3),(-7,3),(5,6),(-5,6),(6,5),(-6,5),
        (0,8),(8,0),(4,7),(-4,7),(7,4),(-7,4),(1,8),(-1,8),(8,1),(-8,1),(5,7),(-5,7),
        (7,5),(-7,5),(2,8),(-2,8),(8,2),(-8,2),(3,8),(-3,8),(8,3),(-8,3),(6,6),(-6,6),
        (6,7),(-6,7),(7,6),(-7,6),(4,8),(-4,8),(8,4),(-8,4),
    };

    private static int MapDistance(int code, int width)
    {
        var (dx, dy) = DistanceMap[code - 1];
        int dist = dy * width + dx;
        return dist < 1 ? 1 : dist;
    }

    // ===== Huffman (prefix) code decoding =====

    private struct HuffmanGroup
    {
        public HuffmanTree Green; // alphabet: 256 literal + 24 length + color-cache indices
        public HuffmanTree Red;
        public HuffmanTree Blue;
        public HuffmanTree Alpha;
        public HuffmanTree Distance;
    }

    private static HuffmanGroup ReadHuffmanGroup(BitReader br, int colorCacheBits)
    {
        int greenSize = 256 + 24 + (colorCacheBits > 0 ? 1 << colorCacheBits : 0);
        return new HuffmanGroup
        {
            Green = ReadHuffmanCode(br, greenSize),
            Red = ReadHuffmanCode(br, 256),
            Blue = ReadHuffmanCode(br, 256),
            Alpha = ReadHuffmanCode(br, 256),
            Distance = ReadHuffmanCode(br, 40),
        };
    }

    private static readonly int[] CodeLengthCodeOrder = { 17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

    private static HuffmanTree ReadHuffmanCode(BitReader br, int alphabetSize)
    {
        var lengths = new int[alphabetSize];

        if (br.ReadBits(1) != 0)
        {
            // Simple code length code: 1 or 2 symbols, trivial tree.
            int numSymbols = (int)br.ReadBits(1) + 1;
            int firstSymbolLenBits = (int)br.ReadBits(1) == 0 ? 1 : 8;
            int s0 = (int)br.ReadBits(firstSymbolLenBits);
            lengths[s0] = 1;
            if (numSymbols == 2)
            {
                int s1 = (int)br.ReadBits(8);
                lengths[s1] = 1;
            }
            else
            {
                lengths[s0] = 0; // single symbol: zero-bit code (always this value)
                return HuffmanTree.BuildSingleSymbol(s0);
            }
            return HuffmanTree.Build(lengths);
        }

        // Normal code length code: a 19-symbol meta-alphabet describes the
        // actual code lengths, itself Huffman-coded.
        int numCodeLengths = (int)br.ReadBits(4) + 4;
        var clLengths = new int[19];
        for (int i = 0; i < numCodeLengths; i++)
            clLengths[CodeLengthCodeOrder[i]] = (int)br.ReadBits(3);
        var clTree = HuffmanTree.Build(clLengths);

        int maxSymbol = alphabetSize;
        if (br.ReadBits(1) != 0)
        {
            int lengthNBits = 2 + 2 * (int)br.ReadBits(3);
            maxSymbol = (int)br.ReadBits(lengthNBits) + 2;
        }

        int symbol = 0;
        int prevLen = 8;
        while (symbol < alphabetSize)
        {
            if (maxSymbol-- == 0) break;
            int code = ReadSymbol(br, clTree);
            if (code < 16)
            {
                lengths[symbol++] = code;
                if (code != 0) prevLen = code;
            }
            else if (code == 16)
            {
                int repeat = (int)br.ReadBits(2) + 3;
                for (int i = 0; i < repeat && symbol < alphabetSize; i++) lengths[symbol++] = prevLen;
            }
            else if (code == 17)
            {
                int repeat = (int)br.ReadBits(3) + 3;
                symbol += repeat;
            }
            else // 18
            {
                int repeat = (int)br.ReadBits(7) + 11;
                symbol += repeat;
            }
        }

        return HuffmanTree.Build(lengths);
    }

    private static int ReadSymbol(BitReader br, HuffmanTree tree)
    {
        if (tree.SingleSymbol.HasValue) return tree.SingleSymbol.Value;
        int node = 0; // index into tree.Nodes; root is 0
        while (true)
        {
            int bit = (int)br.ReadBits(1);
            node = tree.Nodes[node * 2 + bit];
            if (node < 0) return -node - 1; // leaf encodes symbol as -(symbol+1)
        }
    }

    /// <summary>Canonical Huffman tree, stored as a flat binary-tree node array (2 children per node); negative entries are leaves.</summary>
    private struct HuffmanTree
    {
        public int[] Nodes;
        public int? SingleSymbol;

        public static HuffmanTree BuildSingleSymbol(int symbol) => new HuffmanTree { SingleSymbol = symbol, Nodes = Array.Empty<int>() };

        public static HuffmanTree Build(int[] codeLengths)
        {
            int maxLen = 0;
            for (int i = 0; i < codeLengths.Length; i++) if (codeLengths[i] > maxLen) maxLen = codeLengths[i];
            if (maxLen == 0) return BuildSingleSymbol(0);

            // Canonical Huffman: count codes per length, assign first code per length, then walk symbols in order.
            var blCount = new int[maxLen + 1];
            for (int i = 0; i < codeLengths.Length; i++) if (codeLengths[i] > 0) blCount[codeLengths[i]]++;

            var nextCode = new int[maxLen + 1];
            int codeAcc = 0;
            for (int bits = 1; bits <= maxLen; bits++)
            {
                codeAcc = (codeAcc + blCount[bits - 1]) << 1;
                nextCode[bits] = codeAcc;
            }

            // Node array grown dynamically; root at index 0.
            var nodes = new List<int> { -1, -1 };
            for (int symbol = 0; symbol < codeLengths.Length; symbol++)
            {
                int len = codeLengths[symbol];
                if (len == 0) continue;
                int code = nextCode[len]++;
                int node = 0;
                for (int b = len - 1; b >= 0; b--)
                {
                    int bit = (code >> b) & 1;
                    if (nodes[node * 2 + bit] == -1 && b > 0)
                    {
                        nodes.Add(-1); nodes.Add(-1);
                        nodes[node * 2 + bit] = nodes.Count / 2 - 1;
                    }
                    else if (b == 0)
                    {
                        nodes[node * 2 + bit] = -symbol - 1;
                        break;
                    }
                    node = nodes[node * 2 + bit];
                }
            }

            return new HuffmanTree { Nodes = nodes.ToArray(), SingleSymbol = null };
        }
    }

    /// <summary>LSB-first bit reader over a byte array (VP8L's bit order).</summary>
    private sealed class BitReader
    {
        private readonly byte[] _data;
        private int _bytePos;
        private int _bitPos; // 0-7 within current byte

        public BitReader(byte[] data, int startByte)
        {
            _data = data;
            _bytePos = startByte;
        }

        public uint ReadBits(int count)
        {
            uint result = 0;
            for (int i = 0; i < count; i++)
            {
                int bit = 0;
                if (_bytePos < _data.Length)
                    bit = (_data[_bytePos] >> _bitPos) & 1;
                result |= (uint)(bit << i);
                _bitPos++;
                if (_bitPos == 8) { _bitPos = 0; _bytePos++; }
            }
            return result;
        }
    }
}
