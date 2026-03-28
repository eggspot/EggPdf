using System;

namespace EggPdf.Pdf;

/// <summary>
/// Pure C# BMP decoder. Supports BITMAPINFOHEADER (40-byte) and BITMAPV5HEADER.
/// Bit depths: 1, 4, 8 (indexed with color table), 24 (BGR), 32 (BGRA).
/// Compression: BI_RGB (uncompressed) only.
/// Handles bottom-up row order (standard BMP is stored upside down).
/// Infallible: returns null on any error, never throws.
/// </summary>
internal static class BmpDecoder
{
    private const int BI_RGB = 0;
    private const int BI_BITFIELDS = 3;

    /// <summary>Check if data starts with BMP signature ("BM").</summary>
    internal static bool IsBmp(byte[] data)
    {
        if (data == null || data.Length < 2)
            return false;
        return data[0] == 0x42 && data[1] == 0x4D; // "BM"
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

        // BITMAPFILEHEADER (14 bytes)
        if (data.Length < 14)
            return null;

        // int fileSize = ReadInt32LE(data, 2); // not used for validation
        int dataOffset = ReadInt32LE(data, 10);

        // DIB Header (at least BITMAPINFOHEADER = 40 bytes)
        if (data.Length < 14 + 4)
            return null;

        int headerSize = ReadInt32LE(data, 14);
        if (headerSize < 40 || data.Length < 14 + headerSize)
            return null;

        int width = ReadInt32LE(data, 18);
        int height = ReadInt32LE(data, 22);
        // int planes = ReadInt16LE(data, 26); // always 1
        int bitDepth = ReadInt16LE(data, 28);
        int compression = ReadInt32LE(data, 30);
        // int imageSize = ReadInt32LE(data, 34); // can be 0 for BI_RGB
        // int colorsUsed = ReadInt32LE(data, 46); // 0 = default

        if (width <= 0)
            return null;

        // Height can be negative (top-down) or positive (bottom-up)
        bool topDown = height < 0;
        int absHeight = Math.Abs(height);

        if (absHeight <= 0)
            return null;

        // Only support BI_RGB (uncompressed) and BI_BITFIELDS for 32-bit
        if (compression != BI_RGB && !(compression == BI_BITFIELDS && bitDepth == 32))
            return null;

        // Validate bit depth
        if (bitDepth != 1 && bitDepth != 4 && bitDepth != 8 && bitDepth != 24 && bitDepth != 32)
            return null;

        // For indexed formats, read color table
        byte[]? colorTable = null;
        if (bitDepth <= 8)
        {
            int colorTableOffset = 14 + headerSize;
            int maxColors = 1 << bitDepth;
            int colorsUsed = ReadInt32LE(data, 46);
            int colorCount = (colorsUsed > 0 && colorsUsed <= maxColors) ? colorsUsed : maxColors;
            int colorTableSize = colorCount * 4; // RGBQUAD (4 bytes each: B, G, R, reserved)

            if (colorTableOffset + colorTableSize > data.Length)
                return null;

            colorTable = new byte[colorTableSize];
            Buffer.BlockCopy(data, colorTableOffset, colorTable, 0, colorTableSize);
        }

        // Calculate row stride (rows are padded to 4-byte boundaries)
        int rowStride = ((width * bitDepth + 31) / 32) * 4;

        if (dataOffset + rowStride * absHeight > data.Length)
        {
            // Tolerate slightly truncated files
            if (dataOffset >= data.Length)
                return null;
        }

        int pixelCount = width * absHeight;

        // Decode based on bit depth
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
            case 32:
                return Decode32Bit(data, dataOffset, width, absHeight, rowStride, topDown);
            default:
                return null;
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
                rgb[dst] = colorTable[index * 4 + 2];     // R (BMP stores BGR)
                rgb[dst + 1] = colorTable[index * 4 + 1]; // G
                rgb[dst + 2] = colorTable[index * 4];     // B
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
                rgb[dst] = colorTable[index * 4 + 2];     // R
                rgb[dst + 1] = colorTable[index * 4 + 1]; // G
                rgb[dst + 2] = colorTable[index * 4];     // B
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
                rgb[dst] = colorTable[index * 4 + 2];     // R
                rgb[dst + 1] = colorTable[index * 4 + 1]; // G
                rgb[dst + 2] = colorTable[index * 4];     // B
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
                rgb[dst] = data[src + 2];     // R (BMP stores BGR)
                rgb[dst + 1] = data[src + 1]; // G
                rgb[dst + 2] = data[src];     // B
            }
        }

        return new BmpDecodeResult(width, height, rgb, false);
    }

    private static BmpDecodeResult Decode32Bit(byte[] data, int dataOffset, int width, int height,
        int rowStride, bool topDown)
    {
        var rgba = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            int srcRow = topDown ? y : (height - 1 - y);
            int srcOffset = dataOffset + srcRow * rowStride;
            int dstRow = y * width;

            for (int x = 0; x < width; x++)
            {
                int src = srcOffset + x * 4;
                if (src + 3 >= data.Length) break;

                int dst = (dstRow + x) * 4;
                rgba[dst] = data[src + 2];     // R (BMP stores BGRA)
                rgba[dst + 1] = data[src + 1]; // G
                rgba[dst + 2] = data[src];     // B
                rgba[dst + 3] = data[src + 3]; // A
            }
        }

        return new BmpDecodeResult(width, height, rgba, true);
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
    /// <summary>Pixel data: RGB (3 bytes/pixel) if HasAlpha is false, RGBA (4 bytes/pixel) if true.</summary>
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
