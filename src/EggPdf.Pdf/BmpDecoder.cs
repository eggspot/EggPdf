using System;

namespace EggPdf.Pdf;

/// <summary>
/// Pure C# BMP decoder. Supports BITMAPINFOHEADER (40-byte) through BITMAPV5HEADER.
/// Bit depths: 1, 4, 8 (indexed with color table), 16 and 32 (channel masks), 24 (BGR).
/// Compression: BI_RGB (uncompressed), BI_BITFIELDS and BI_ALPHABITFIELDS.
/// Handles bottom-up row order (standard BMP is stored upside down).
/// Infallible: returns null on any error, never throws.
/// </summary>
internal static partial class BmpDecoder
{
    private const int BI_RGB = 0;
    private const int BI_BITFIELDS = 3;
    private const int BI_ALPHABITFIELDS = 6;
    private const long MaxTruncationFactor = 2;
    private const int FileHeaderSize = 14;
    private const int InfoHeaderSize = 40;
    private const int DataOffsetField = 10;
    private const int WidthField = 18;
    private const int HeightField = 22;
    private const int BitDepthField = 28;
    private const int CompressionField = 30;
    private const int ColorsUsedField = 46;
    private const int RgbQuadSize = 4;

    /// <summary>Check if data starts with BMP signature ("BM").</summary>
    internal static bool IsBmp(byte[] data)
    {
        if (data == null || data.Length < 2)
            return false;
        return data[0] == (byte)'B' && data[1] == (byte)'M';
    }

    /// <summary>
    /// Decode a BMP image into raw pixel data.
    /// Returns null if the BMP is invalid or uses unsupported features.
    /// </summary>
    internal static BmpDecodeResult? Decode(byte[] bmpData)
    {
        try
        {
            return DecodeInternal(bmpData);
        }
        catch
        {
            return null;
        }
    }

    private static BmpDecodeResult? DecodeInternal(byte[] data)
    {
        if (!IsBmp(data))
            return null;

        if (data.Length < FileHeaderSize)
            return null;

        int dataOffset = ReadInt32LE(data, DataOffsetField);

        if (data.Length < FileHeaderSize + 4)
            return null;

        int headerSize = ReadInt32LE(data, FileHeaderSize);
        if (headerSize < InfoHeaderSize || data.Length < FileHeaderSize + headerSize)
            return null;

        int width = ReadInt32LE(data, WidthField);
        int height = ReadInt32LE(data, HeightField);
        int bitDepth = ReadInt16LE(data, BitDepthField);
        int compression = ReadInt32LE(data, CompressionField);

        if (width <= 0)
            return null;

        bool topDown = height < 0;
        int absHeight = Math.Abs(height);

        if (absHeight <= 0)
            return null;

        bool usesMasks = bitDepth == 16 || bitDepth == 32;
        bool hasMaskFields = compression == BI_BITFIELDS || compression == BI_ALPHABITFIELDS;
        if (compression != BI_RGB && !(hasMaskFields && usesMasks))
            return null;

        if (bitDepth != 1 && bitDepth != 4 && bitDepth != 8 && bitDepth != 16 && bitDepth != 24 && bitDepth != 32)
            return null;

        if (dataOffset < 0 || dataOffset >= data.Length)
            return null;
        long claimedPixelBytes = ((long)width * bitDepth + 31) / 32 * 4 * absHeight;
        if ((long)width * absHeight * 4 > int.MaxValue || claimedPixelBytes > int.MaxValue ||
            claimedPixelBytes > MaxTruncationFactor * (data.Length - dataOffset))
            return null;

        byte[]? colorTable = null;
        if (bitDepth <= 8)
        {
            int colorTableOffset = FileHeaderSize + headerSize;
            int maxColors = 1 << bitDepth;
            int colorsUsed = ReadInt32LE(data, ColorsUsedField);
            int colorCount = (colorsUsed > 0 && colorsUsed <= maxColors) ? colorsUsed : maxColors;
            int colorTableSize = colorCount * RgbQuadSize;

            if (colorTableOffset + colorTableSize > data.Length)
                return null;

            colorTable = new byte[colorTableSize];
            Buffer.BlockCopy(data, colorTableOffset, colorTable, 0, colorTableSize);
        }

        int rowStride = (int)(claimedPixelBytes / absHeight);

        switch (bitDepth)
        {
            case 1:
                return DecodeIndexed1(data, dataOffset, width, absHeight, rowStride, topDown, colorTable!);
            case 4:
                return DecodeIndexed4(data, dataOffset, width, absHeight, rowStride, topDown, colorTable!);
            case 8:
                return DecodeIndexed8(data, dataOffset, width, absHeight, rowStride, topDown, colorTable!);
            case 24:
                return Decode24Bit(data, dataOffset, width, absHeight, rowStride, topDown);
            default:
                var masks = ReadChannelMasks(data, headerSize, compression, bitDepth);
                if (masks == null)
                    return null;
                return DecodeMasked(data, dataOffset, width, absHeight, rowStride, topDown, bitDepth / 8, masks.Value);
        }
    }

    private static BmpDecodeResult DecodeIndexed1(byte[] data, int dataOffset, int width, int height,
        int rowStride, bool topDown, byte[] colorTable)
    {
        var rgb = new byte[width * height * 3];
        int colorEntries = colorTable.Length / 4;

        for (int y = 0; y < height; y++)
        {
            int srcRow = topDown ? y : (height - 1 - y);
            int srcOffset = dataOffset + srcRow * rowStride;
            int dstRow = y * width;

            for (int x = 0; x < width; x++)
            {
                int byteIdx = srcOffset + x / 8;
                if (byteIdx >= data.Length) break;
                int bitIdx = 7 - (x % 8);
                int index = (data[byteIdx] >> bitIdx) & 0x01;
                if (index >= colorEntries) index = 0;

                int dst = (dstRow + x) * 3;
                rgb[dst] = colorTable[index * 4 + 2];
                rgb[dst + 1] = colorTable[index * 4 + 1];
                rgb[dst + 2] = colorTable[index * 4];
            }
        }

        return new BmpDecodeResult(width, height, rgb, false);
    }

    private static BmpDecodeResult DecodeIndexed4(byte[] data, int dataOffset, int width, int height,
        int rowStride, bool topDown, byte[] colorTable)
    {
        var rgb = new byte[width * height * 3];
        int colorEntries = colorTable.Length / 4;

        for (int y = 0; y < height; y++)
        {
            int srcRow = topDown ? y : (height - 1 - y);
            int srcOffset = dataOffset + srcRow * rowStride;
            int dstRow = y * width;

            for (int x = 0; x < width; x++)
            {
                int byteIdx = srcOffset + x / 2;
                if (byteIdx >= data.Length) break;
                int index;
                if (x % 2 == 0)
                    index = (data[byteIdx] >> 4) & 0x0F;
                else
                    index = data[byteIdx] & 0x0F;

                if (index >= colorEntries) index = 0;

                int dst = (dstRow + x) * 3;
                rgb[dst] = colorTable[index * 4 + 2];
                rgb[dst + 1] = colorTable[index * 4 + 1];
                rgb[dst + 2] = colorTable[index * 4];
            }
        }

        return new BmpDecodeResult(width, height, rgb, false);
    }

    private static BmpDecodeResult DecodeIndexed8(byte[] data, int dataOffset, int width, int height,
        int rowStride, bool topDown, byte[] colorTable)
    {
        var rgb = new byte[width * height * 3];
        int colorEntries = colorTable.Length / 4;

        for (int y = 0; y < height; y++)
        {
            int srcRow = topDown ? y : (height - 1 - y);
            int srcOffset = dataOffset + srcRow * rowStride;
            int dstRow = y * width;

            for (int x = 0; x < width; x++)
            {
                int byteIdx = srcOffset + x;
                if (byteIdx >= data.Length) break;
                int index = data[byteIdx];
                if (index >= colorEntries) index = 0;

                int dst = (dstRow + x) * 3;
                rgb[dst] = colorTable[index * 4 + 2];
                rgb[dst + 1] = colorTable[index * 4 + 1];
                rgb[dst + 2] = colorTable[index * 4];
            }
        }

        return new BmpDecodeResult(width, height, rgb, false);
    }

    private static BmpDecodeResult Decode24Bit(byte[] data, int dataOffset, int width, int height,
        int rowStride, bool topDown)
    {
        var rgb = new byte[width * height * 3];

        for (int y = 0; y < height; y++)
        {
            int srcRow = topDown ? y : (height - 1 - y);
            int srcOffset = dataOffset + srcRow * rowStride;
            int dstRow = y * width;

            for (int x = 0; x < width; x++)
            {
                int src = srcOffset + x * 3;
                if (src + 2 >= data.Length) break;

                int dst = (dstRow + x) * 3;
                rgb[dst] = data[src + 2];
                rgb[dst + 1] = data[src + 1];
                rgb[dst + 2] = data[src];
            }
        }

        return new BmpDecodeResult(width, height, rgb, false);
    }

    private static int ReadInt32LE(byte[] data, int offset)
    {
        return data[offset] | (data[offset + 1] << 8) |
               (data[offset + 2] << 16) | (data[offset + 3] << 24);
    }

    private static int ReadInt16LE(byte[] data, int offset)
    {
        return data[offset] | (data[offset + 1] << 8);
    }
}

/// <summary>Result of BMP decoding.</summary>
internal sealed class BmpDecodeResult
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>Pixel data: RGB (3 bytes/pixel) if HasAlpha is false, RGBA (4 bytes/pixel) if true; BMP's BGR byte order is already swapped.</summary>
    public byte[] PixelData { get; }
    /// <summary>True if PixelData contains RGBA data (32-bit BMP), false if RGB.</summary>
    public bool HasAlpha { get; }

    public BmpDecodeResult(int width, int height, byte[] pixelData, bool hasAlpha)
    {
        Width = width;
        Height = height;
        PixelData = pixelData;
        HasAlpha = hasAlpha;
    }
}
