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
    /// <summary>Check if data starts with GIF signature ("GIF87a" or "GIF89a").</summary>
    internal static bool IsGif(byte[] data)
    {
        if (data == null || data.Length < 6)
            return false;
        // "GIF8" prefix
        return data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38
            && (data[4] == 0x37 || data[4] == 0x39) // '7' or '9'
            && data[5] == 0x61; // 'a'
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

        int pos = 6; // skip signature

        // Logical Screen Descriptor (7 bytes)
        if (pos + 7 > data.Length)
            return null;

        int screenWidth = data[pos] | (data[pos + 1] << 8);
        int screenHeight = data[pos + 2] | (data[pos + 3] << 8);
        byte packed = data[pos + 4];
        // byte bgColorIndex = data[pos + 5]; // background color index
        // byte pixelAspect = data[pos + 6];
        pos += 7;

        if (screenWidth <= 0 || screenHeight <= 0)
            return null;

        bool hasGlobalColorTable = (packed & 0x80) != 0;
        int globalColorTableSize = 1 << ((packed & 0x07) + 1);

        // Read Global Color Table
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

        // Parse blocks until we find an image descriptor
        int transparentColorIndex = -1;
        bool hasTransparency = false;

        while (pos < data.Length)
        {
            byte introducer = data[pos++];

            if (introducer == 0x3B) // Trailer
                break;

            if (introducer == 0x21) // Extension
            {
                if (pos >= data.Length)
                    return null;

                byte label = data[pos++];

                if (label == 0xF9) // Graphic Control Extension
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

                    // Skip block terminator
                    if (pos < data.Length && data[pos] == 0x00)
                        pos++;
                }
                else
                {
                    // Skip other extensions (sub-blocks)
                    if (!SkipSubBlocks(data, ref pos))
                        return null;
                }
                continue;
            }

            if (introducer == 0x2C) // Image Descriptor
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

                // Read Local Color Table (overrides global)
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

                // LZW Minimum Code Size
                if (pos >= data.Length)
                    return null;
                int lzwMinCodeSize = data[pos++];
                if (lzwMinCodeSize < 2 || lzwMinCodeSize > 12)
                    return null;

                // Read LZW compressed data from sub-blocks
                byte[]? compressedData = ReadSubBlockData(data, ref pos);
                if (compressedData == null || compressedData.Length == 0)
                    return null;

                // LZW decompress
                byte[]? indices = LzwDecompress(compressedData, lzwMinCodeSize, imgWidth * imgHeight);
                if (indices == null)
                    return null;

                // Handle interlacing
                if (isInterlaced)
                    indices = Deinterlace(indices, imgWidth, imgHeight);

                // Convert palette indices to pixels
                int colorEntries = colorTable.Length / 3;

                // Determine output dimensions: use screen size for placement
                int outWidth = screenWidth;
                int outHeight = screenHeight;

                // If image descriptor covers entire screen, use image directly
                if (imgLeft == 0 && imgTop == 0 && imgWidth == screenWidth && imgHeight == screenHeight)
                {
                    outWidth = imgWidth;
                    outHeight = imgHeight;
                }
                else
                {
                    // Use image dimensions if they're the main content
                    outWidth = imgWidth;
                    outHeight = imgHeight;
                }

                int pixelCount = outWidth * outHeight;

                if (hasTransparency && transparentColorIndex >= 0)
                {
                    var rgba = new byte[pixelCount * 4];
                    for (int i = 0; i < pixelCount && i < indices.Length; i++)
                    {
                        int idx = indices[i];
                        if (idx >= colorEntries)
                            idx = 0;

                        int dst = i * 4;
                        if (idx == transparentColorIndex)
                        {
                            rgba[dst] = 0;
                            rgba[dst + 1] = 0;
                            rgba[dst + 2] = 0;
                            rgba[dst + 3] = 0;
                        }
                        else
                        {
                            rgba[dst] = colorTable[idx * 3];
                            rgba[dst + 1] = colorTable[idx * 3 + 1];
                            rgba[dst + 2] = colorTable[idx * 3 + 2];
                            rgba[dst + 3] = 255;
                        }
                    }
                    return new GifDecodeResult(outWidth, outHeight, rgba, true);
                }
                else
                {
                    var rgb = new byte[pixelCount * 3];
                    for (int i = 0; i < pixelCount && i < indices.Length; i++)
                    {
                        int idx = indices[i];
                        if (idx >= colorEntries)
                            idx = 0;

                        int dst = i * 3;
                        rgb[dst] = colorTable[idx * 3];
                        rgb[dst + 1] = colorTable[idx * 3 + 1];
                        rgb[dst + 2] = colorTable[idx * 3 + 2];
                    }
                    return new GifDecodeResult(outWidth, outHeight, rgb, false);
                }
            }

            // Unknown block type -- skip
            break;
        }

        return null; // no image found
    }

    /// <summary>LZW decompression for GIF image data.</summary>
    private static byte[]? LzwDecompress(byte[] compressed, int minCodeSize, int expectedPixels)
    {
        int clearCode = 1 << minCodeSize;
        int eoiCode = clearCode + 1;
        int nextCode = eoiCode + 1;
        int codeSize = minCodeSize + 1;

        // Code table: each entry is a (prefix, suffix) pair
        // For codes < clearCode, the entry is just the single byte value
        // We use parallel arrays for performance
        const int MaxTableSize = 4096; // 12-bit max
        int[] prefixes = new int[MaxTableSize];
        byte[] suffixes = new byte[MaxTableSize];
        int[] lengths = new int[MaxTableSize];

        // Initialize table with single-character entries
        for (int i = 0; i < clearCode; i++)
        {
            prefixes[i] = -1;
            suffixes[i] = (byte)i;
            lengths[i] = 1;
        }

        var output = new byte[expectedPixels];
        int outputPos = 0;

        // Bit reader state
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
                // Reset table
                nextCode = eoiCode + 1;
                codeSize = minCodeSize + 1;
                prevCode = -1;
                continue;
            }

            if (prevCode == -1)
            {
                // First code after clear
                if (code >= clearCode)
                    return null; // invalid
                if (outputPos < expectedPixels)
                    output[outputPos++] = (byte)code;
                prevCode = code;
                continue;
            }

            byte firstChar;
            if (code < nextCode)
            {
                // Code is in table - output its string
                int len = (code < clearCode) ? 1 : lengths[code];
                if (outputPos + len > expectedPixels)
                    len = expectedPixels - outputPos;

                OutputString(output, outputPos, code, len, prefixes, suffixes, clearCode);
                outputPos += len;
                firstChar = GetFirstChar(code, prefixes, suffixes, clearCode);
            }
            else if (code == nextCode)
            {
                // Special case: code not yet in table
                firstChar = GetFirstChar(prevCode, prefixes, suffixes, clearCode);
                int prevLen = (prevCode < clearCode) ? 1 : lengths[prevCode];
                int len = prevLen + 1;
                if (outputPos + len > expectedPixels)
                    len = expectedPixels - outputPos;

                // Output: previous string + first char of previous string
                if (len > 1)
                    OutputString(output, outputPos, prevCode, len - 1, prefixes, suffixes, clearCode);
                if (len > 0)
                    output[outputPos + len - 1] = firstChar;
                outputPos += len;
            }
            else
            {
                return null; // invalid code
            }

            // Add new entry to table
            if (nextCode < MaxTableSize)
            {
                prefixes[nextCode] = prevCode;
                suffixes[nextCode] = firstChar;
                lengths[nextCode] = ((prevCode < clearCode) ? 1 : lengths[prevCode]) + 1;
                nextCode++;

                // Increase code size if needed
                if (nextCode > (1 << codeSize) && codeSize < 12)
                    codeSize++;
            }

            prevCode = code;
        }

        // If we got fewer pixels than expected, that's OK for partial images
        if (outputPos < expectedPixels)
        {
            // Fill remaining with 0
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

    /// <summary>Get the first character of a code's string.</summary>
    private static byte GetFirstChar(int code, int[] prefixes, byte[] suffixes, int clearCode)
    {
        // Walk the chain to find the first character
        int c = code;
        int safety = 4096;
        while (c >= clearCode + 2 && safety-- > 0)
            c = prefixes[c];
        return (c < 0) ? (byte)0 : (byte)c;
    }

    /// <summary>Output a code's string to the output buffer.</summary>
    private static void OutputString(byte[] output, int outputPos, int code, int length,
        int[] prefixes, byte[] suffixes, int clearCode)
    {
        // Write backwards from the end
        int writePos = outputPos + length - 1;
        int c = code;
        int safety = 4096;
        while (writePos >= outputPos && safety-- > 0)
        {
            if (c < clearCode + 2)
            {
                // Single character code
                if (c < 0) c = 0;
                output[writePos] = (byte)c;
                break;
            }
            output[writePos--] = suffixes[c];
            c = prefixes[c];
        }
    }

    /// <summary>Deinterlace GIF image data (4-pass interlacing).</summary>
    private static byte[] Deinterlace(byte[] indices, int width, int height)
    {
        var result = new byte[width * height];
        int srcRow = 0;

        // Pass 1: rows 0, 8, 16, ...
        for (int y = 0; y < height; y += 8)
        {
            Buffer.BlockCopy(indices, srcRow * width, result, y * width, width);
            srcRow++;
        }
        // Pass 2: rows 4, 12, 20, ...
        for (int y = 4; y < height; y += 8)
        {
            Buffer.BlockCopy(indices, srcRow * width, result, y * width, width);
            srcRow++;
        }
        // Pass 3: rows 2, 6, 10, ...
        for (int y = 2; y < height; y += 4)
        {
            Buffer.BlockCopy(indices, srcRow * width, result, y * width, width);
            srcRow++;
        }
        // Pass 4: rows 1, 3, 5, ...
        for (int y = 1; y < height; y += 2)
        {
            Buffer.BlockCopy(indices, srcRow * width, result, y * width, width);
            srcRow++;
        }

        return result;
    }

    /// <summary>Read sub-block data into a single byte array.</summary>
    private static byte[]? ReadSubBlockData(byte[] data, ref int pos)
    {
        using var ms = new System.IO.MemoryStream();

        while (pos < data.Length)
        {
            byte blockSize = data[pos++];
            if (blockSize == 0)
                break; // block terminator

            if (pos + blockSize > data.Length)
                return null;

            ms.Write(data, pos, blockSize);
            pos += blockSize;
        }

        return ms.ToArray();
    }

    /// <summary>Skip sub-blocks (used for extensions we don't care about).</summary>
    private static bool SkipSubBlocks(byte[] data, ref int pos)
    {
        while (pos < data.Length)
        {
            byte blockSize = data[pos++];
            if (blockSize == 0)
                return true; // block terminator

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
