using System;

namespace EggPdf.Pdf;

/// <summary>
/// Pure C# GIF decoder. Supports GIF87a and GIF89a.
/// Decodes first frame only (animated GIFs return first frame).
/// Handles transparency extension (transparent color index).
/// Infallible: returns null on any error, never throws.
/// </summary>
internal static class GifDecoder
{
    private const byte TrailerIntroducer = 0x3B;
    private const byte ExtensionIntroducer = 0x21;
    private const byte ImageDescriptorIntroducer = 0x2C;
    private const byte GraphicControlExtensionLabel = 0xF9;
    private const int MaxCodeSize = 12;

    /// <summary>Check if data starts with GIF signature ("GIF87a" or "GIF89a").</summary>
    internal static bool IsGif(byte[] data)
    {
        if (data == null || data.Length < 6)
            return false;
        return data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'8'
            && (data[4] == (byte)'7' || data[4] == (byte)'9')
            && data[5] == (byte)'a';
    }

    /// <summary>
    /// Decode a GIF image into raw pixel data.
    /// Returns null if the GIF is invalid or uses unsupported features.
    /// Only decodes the first frame of animated GIFs.
    /// </summary>
    internal static GifDecodeResult? Decode(byte[] gifData)
    {
        try
        {
            return DecodeInternal(gifData);
        }
        catch
        {
            return null;
        }
    }

    private static GifDecodeResult? DecodeInternal(byte[] data)
    {
        if (!IsGif(data))
            return null;

        int pos = 6;

        if (pos + 7 > data.Length)
            return null;

        int screenWidth = data[pos] | (data[pos + 1] << 8);
        int screenHeight = data[pos + 2] | (data[pos + 3] << 8);
        byte packed = data[pos + 4];
        pos += 7;

        bool hasGlobalColorTable = (packed & 0x80) != 0;
        int globalColorTableSize = 1 << ((packed & 0x07) + 1);

        byte[]? globalColorTable = null;
        if (hasGlobalColorTable)
        {
            int tableBytes = globalColorTableSize * 3;
            if (pos + tableBytes > data.Length)
                return null;
            globalColorTable = new byte[tableBytes];
            Buffer.BlockCopy(data, pos, globalColorTable, 0, tableBytes);
            pos += tableBytes;
        }

        int transparentColorIndex = -1;
        bool hasTransparency = false;

        while (pos < data.Length)
        {
            byte introducer = data[pos++];

            if (introducer == TrailerIntroducer)
                break;

            if (introducer == ExtensionIntroducer)
            {
                if (pos >= data.Length)
                    return null;

                byte label = data[pos++];

                if (label == GraphicControlExtensionLabel)
                {
                    if (pos >= data.Length)
                        return null;
                    byte blockSize = data[pos++];
                    if (blockSize < 4 || pos + blockSize > data.Length)
                        return null;

                    byte gcPacked = data[pos];
                    hasTransparency = (gcPacked & 0x01) != 0;
                    transparentColorIndex = data[pos + 3];
                    pos += blockSize;

                    if (pos < data.Length && data[pos] == 0x00)
                        pos++;
                }
                else
                {
                    if (!SkipSubBlocks(data, ref pos))
                        return null;
                }
                continue;
            }

            if (introducer == ImageDescriptorIntroducer)
            {
                if (pos + 9 > data.Length)
                    return null;

                int imgLeft = data[pos] | (data[pos + 1] << 8);
                int imgTop = data[pos + 2] | (data[pos + 3] << 8);
                int imgWidth = data[pos + 4] | (data[pos + 5] << 8);
                int imgHeight = data[pos + 6] | (data[pos + 7] << 8);
                byte imgPacked = data[pos + 8];
                pos += 9;

                if (imgWidth <= 0 || imgHeight <= 0)
                    return null;

                bool hasLocalColorTable = (imgPacked & 0x80) != 0;
                bool isInterlaced = (imgPacked & 0x40) != 0;
                int localColorTableSize = 1 << ((imgPacked & 0x07) + 1);

                byte[]? colorTable = globalColorTable;
                if (hasLocalColorTable)
                {
                    int tableBytes = localColorTableSize * 3;
                    if (pos + tableBytes > data.Length)
                        return null;
                    colorTable = new byte[tableBytes];
                    Buffer.BlockCopy(data, pos, colorTable, 0, tableBytes);
                    pos += tableBytes;
                }

                if (colorTable == null)
                    return null;

                if (pos >= data.Length)
                    return null;
                int lzwMinCodeSize = data[pos++];
                if (lzwMinCodeSize < 2 || lzwMinCodeSize > MaxCodeSize)
                    return null;

                byte[]? compressedData = ReadSubBlockData(data, ref pos);
                if (compressedData == null || compressedData.Length == 0)
                    return null;

                var frame = new FrameRect(imgLeft, imgTop, imgWidth, imgHeight);
                int canvasWidth = Math.Max(screenWidth, imgLeft + imgWidth);
                int canvasHeight = Math.Max(screenHeight, imgTop + imgHeight);
                if ((long)canvasWidth * canvasHeight * 4 > int.MaxValue)
                    return null;

                byte[]? indices = LzwDecompress(compressedData, lzwMinCodeSize, imgWidth * imgHeight);
                if (indices == null)
                    return null;

                if (isInterlaced)
                    indices = Deinterlace(indices, imgWidth, imgHeight);

                int transparentIndex = hasTransparency ? transparentColorIndex : -1;
                return ComposeOnCanvas(indices, frame, canvasWidth, canvasHeight, colorTable, transparentIndex);
            }

            break;
        }

        return null;
    }

    private readonly struct FrameRect
    {
        public readonly int Left, Top, Width, Height;

        public FrameRect(int left, int top, int width, int height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// Places the first frame at its offset on the logical screen. Browsers leave the rest of the screen transparent
    /// (the background colour index is ignored), and grow the screen when the first frame does not fit in it.
    /// </summary>
    private static GifDecodeResult ComposeOnCanvas(byte[] indices, FrameRect frame, int canvasWidth, int canvasHeight,
        byte[] colorTable, int transparentIndex)
    {
        bool coversCanvas = frame.Left == 0 && frame.Top == 0 && frame.Width == canvasWidth && frame.Height == canvasHeight;
        bool hasAlpha = transparentIndex >= 0 || !coversCanvas;
        int channels = hasAlpha ? 4 : 3;
        int colorEntries = colorTable.Length / 3;
        var pixels = new byte[canvasWidth * canvasHeight * channels];

        for (int y = 0; y < frame.Height; y++)
        {
            int row = (frame.Top + y) * canvasWidth + frame.Left;
            for (int x = 0; x < frame.Width; x++)
            {
                int idx = indices[y * frame.Width + x];
                if (idx == transparentIndex)
                    continue;
                if (idx >= colorEntries)
                    idx = 0;

                int dst = (row + x) * channels;
                pixels[dst] = colorTable[idx * 3];
                pixels[dst + 1] = colorTable[idx * 3 + 1];
                pixels[dst + 2] = colorTable[idx * 3 + 2];
                if (hasAlpha)
                    pixels[dst + 3] = 255;
            }
        }

        return new GifDecodeResult(canvasWidth, canvasHeight, pixels, hasAlpha);
    }

    private static byte[]? LzwDecompress(byte[] compressed, int minCodeSize, int expectedPixels)
    {
        int clearCode = 1 << minCodeSize;
        int eoiCode = clearCode + 1;
        int nextCode = eoiCode + 1;
        int codeSize = minCodeSize + 1;

        const int MaxTableSize = 4096;
        int[] prefixes = new int[MaxTableSize];
        byte[] suffixes = new byte[MaxTableSize];
        int[] lengths = new int[MaxTableSize];

        for (int i = 0; i < clearCode; i++)
        {
            prefixes[i] = -1;
            suffixes[i] = (byte)i;
            lengths[i] = 1;
        }

        var output = new byte[expectedPixels];
        int outputPos = 0;

        int bitPos = 0;
        int totalBits = compressed.Length * 8;

        int prevCode = -1;

        while (bitPos + codeSize <= totalBits && outputPos < expectedPixels)
        {
            int code = ReadBits(compressed, bitPos, codeSize);
            bitPos += codeSize;

            if (code == eoiCode)
                break;

            if (code == clearCode)
            {
                nextCode = eoiCode + 1;
                codeSize = minCodeSize + 1;
                prevCode = -1;
                continue;
            }

            if (prevCode == -1)
            {
                if (code >= clearCode)
                    return null;
                if (outputPos < expectedPixels)
                    output[outputPos++] = (byte)code;
                prevCode = code;
                continue;
            }

            byte firstChar;
            if (code < nextCode)
            {
                int len = (code < clearCode) ? 1 : lengths[code];
                if (outputPos + len > expectedPixels)
                    len = expectedPixels - outputPos;

                OutputString(output, outputPos, code, len, prefixes, suffixes, clearCode);
                outputPos += len;
                firstChar = GetFirstChar(code, prefixes, suffixes, clearCode);
            }
            else if (code == nextCode)
            {
                firstChar = GetFirstChar(prevCode, prefixes, suffixes, clearCode);
                int prevLen = (prevCode < clearCode) ? 1 : lengths[prevCode];
                int len = prevLen + 1;
                if (outputPos + len > expectedPixels)
                    len = expectedPixels - outputPos;

                if (len > 1)
                    OutputString(output, outputPos, prevCode, len - 1, prefixes, suffixes, clearCode);
                if (len > 0)
                    output[outputPos + len - 1] = firstChar;
                outputPos += len;
            }
            else
            {
                return null;
            }

            if (nextCode < MaxTableSize)
            {
                prefixes[nextCode] = prevCode;
                suffixes[nextCode] = firstChar;
                lengths[nextCode] = ((prevCode < clearCode) ? 1 : lengths[prevCode]) + 1;
                nextCode++;

                if (nextCode >= (1 << codeSize) && codeSize < MaxCodeSize)
                    codeSize++;
            }

            prevCode = code;
        }

        if (outputPos < expectedPixels)
        {
            for (int i = outputPos; i < expectedPixels; i++)
                output[i] = 0;
        }

        return output;
    }

    /// <summary>Read variable-width bits from compressed data (LSB first, as per GIF spec).</summary>
    private static int ReadBits(byte[] data, int bitPos, int count)
    {
        int result = 0;
        for (int i = 0; i < count; i++)
        {
            int byteIdx = (bitPos + i) / 8;
            int bitIdx = (bitPos + i) % 8;
            if (byteIdx < data.Length && (data[byteIdx] & (1 << bitIdx)) != 0)
                result |= 1 << i;
        }
        return result;
    }

    private static byte GetFirstChar(int code, int[] prefixes, byte[] suffixes, int clearCode)
    {
        int c = code;
        int safety = 4096;
        while (c >= clearCode + 2 && safety-- > 0)
            c = prefixes[c];
        return (c < 0) ? (byte)0 : (byte)c;
    }

    private static void OutputString(byte[] output, int outputPos, int code, int length,
        int[] prefixes, byte[] suffixes, int clearCode)
    {
        int writePos = outputPos + length - 1;
        int c = code;
        int safety = 4096;
        while (writePos >= outputPos && safety-- > 0)
        {
            if (c < clearCode + 2)
            {
                if (c < 0) c = 0;
                output[writePos] = (byte)c;
                break;
            }
            output[writePos--] = suffixes[c];
            c = prefixes[c];
        }
    }

    /// <summary>Deinterlace GIF image data (4-pass interlacing: every 8th row from 0, every 8th from 4, every 4th from 2, every 2nd from 1).</summary>
    private static byte[] Deinterlace(byte[] indices, int width, int height)
    {
        var result = new byte[width * height];
        int srcRow = 0;

        for (int y = 0; y < height; y += 8)
        {
            Buffer.BlockCopy(indices, srcRow * width, result, y * width, width);
            srcRow++;
        }
        for (int y = 4; y < height; y += 8)
        {
            Buffer.BlockCopy(indices, srcRow * width, result, y * width, width);
            srcRow++;
        }
        for (int y = 2; y < height; y += 4)
        {
            Buffer.BlockCopy(indices, srcRow * width, result, y * width, width);
            srcRow++;
        }
        for (int y = 1; y < height; y += 2)
        {
            Buffer.BlockCopy(indices, srcRow * width, result, y * width, width);
            srcRow++;
        }

        return result;
    }

    private static byte[]? ReadSubBlockData(byte[] data, ref int pos)
    {
        using var ms = new System.IO.MemoryStream();

        while (pos < data.Length)
        {
            byte blockSize = data[pos++];
            if (blockSize == 0)
                break;

            if (pos + blockSize > data.Length)
                return null;

            ms.Write(data, pos, blockSize);
            pos += blockSize;
        }

        return ms.ToArray();
    }

    private static bool SkipSubBlocks(byte[] data, ref int pos)
    {
        while (pos < data.Length)
        {
            byte blockSize = data[pos++];
            if (blockSize == 0)
                return true;

            pos += blockSize;
            if (pos > data.Length)
                return false;
        }
        return false;
    }
}

/// <summary>Result of GIF decoding.</summary>
internal sealed class GifDecodeResult
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>Pixel data: RGB (3 bytes/pixel) if HasAlpha is false, RGBA (4 bytes/pixel) if true.</summary>
    public byte[] PixelData { get; }
    /// <summary>True if PixelData contains RGBA data (transparency present), false if RGB.</summary>
    public bool HasAlpha { get; }

    public GifDecodeResult(int width, int height, byte[] pixelData, bool hasAlpha)
    {
        Width = width;
        Height = height;
        PixelData = pixelData;
        HasAlpha = hasAlpha;
    }
}
