using System;
using System.IO;
using System.IO.Compression;

namespace EggPdf.Pdf;

/// <summary>
/// Pure C# PNG decoder. Supports color types 0 (grayscale), 2 (RGB), 3 (indexed),
/// 4 (grayscale+alpha), 6 (RGBA) at 8-bit and 16-bit depth.
/// Infallible: returns null on any error, never throws.
/// </summary>
internal static class PngDecoder
{
    // PNG magic bytes: 137 80 78 71 13 10 26 10
    internal static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    /// <summary>Check if data starts with PNG signature.</summary>
    internal static bool IsPng(byte[] data)
    {
        if (data == null || data.Length < 8)
            return false;
        for (int i = 0; i < 8; i++)
        {
            if (data[i] != PngSignature[i])
                return false;
        }
        return true;
    }

    /// <summary>
    /// Decode a PNG image into raw pixel data.
    /// Returns null if the PNG is invalid or uses unsupported features (e.g. interlacing).
    /// </summary>
    internal static PngDecodeResult? Decode(byte[] pngData)
    {
        try
        {
            return DecodeInternal(pngData);
        }
        catch
        {
            return null;
        }
    }

    private static PngDecodeResult? DecodeInternal(byte[] data)
    {
        if (!IsPng(data))
            return null;

        int pos = 8; // skip signature

        // IHDR must be first chunk
        if (!TryReadChunk(data, ref pos, out var ihdrType, out var ihdrData))
            return null;
        if (ihdrType != "IHDR" || ihdrData.Length < 13)
            return null;

        int width = ReadInt32BE(ihdrData, 0);
        int height = ReadInt32BE(ihdrData, 4);
        int bitDepth = ihdrData[8];
        int colorType = ihdrData[9];
        int compressionMethod = ihdrData[10];
        int filterMethod = ihdrData[11];
        int interlaceMethod = ihdrData[12];

        if (width <= 0 || height <= 0)
            return null;
        if (compressionMethod != 0 || filterMethod != 0)
            return null;
        // Interlaced PNGs not supported
        if (interlaceMethod != 0)
            return null;
        // Only 8-bit and 16-bit depth
        if (bitDepth != 8 && bitDepth != 16)
        {
            // Indexed (color type 3) and grayscale (color type 0) also allow
            // 1, 2, 4 bit depth — 1-bit grayscale is the common QR code format.
            if ((colorType == 3 || colorType == 0) &&
                (bitDepth == 1 || bitDepth == 2 || bitDepth == 4))
            {
                // OK
            }
            else
            {
                return null;
            }
        }

        // Validate color type
        if (colorType != 0 && colorType != 2 && colorType != 3 && colorType != 4 && colorType != 6)
            return null;

        byte[]? palette = null;
        byte[]? trns = null;

        // Collect IDAT chunks and other relevant chunks
        using var idatStream = new MemoryStream();

        while (pos < data.Length)
        {
            if (!TryReadChunk(data, ref pos, out var chunkType, out var chunkData))
                break;

            if (chunkType == "IDAT")
            {
                idatStream.Write(chunkData, 0, chunkData.Length);
            }
            else if (chunkType == "PLTE")
            {
                palette = chunkData;
            }
            else if (chunkType == "tRNS")
            {
                trns = chunkData;
            }
            else if (chunkType == "IEND")
            {
                break;
            }
            // Skip all other chunks
        }

        if (idatStream.Length == 0)
            return null;

        // Indexed color type requires a palette
        if (colorType == 3 && palette == null)
            return null;

        // Decompress IDAT data (zlib: skip 2-byte header, then deflate)
        byte[] compressedData = idatStream.ToArray();
        byte[]? rawScanlines = DecompressZlib(compressedData);
        if (rawScanlines == null)
            return null;

        // Calculate bytes per pixel and stride
        int channels = GetChannelCount(colorType);
        int bitsPerPixel = channels * bitDepth;
        // For indexed with sub-byte depth, bitsPerPixel is just bitDepth
        if (colorType == 3)
            bitsPerPixel = bitDepth;

        int bytesPerPixel = Math.Max(1, bitsPerPixel / 8);
        int stride = (width * bitsPerPixel + 7) / 8; // bytes per scanline (without filter byte)
        int expectedLength = height * (stride + 1); // +1 for filter byte per row

        if (rawScanlines.Length < expectedLength)
            return null;

        // Unfilter scanlines
        byte[] unfiltered = new byte[height * stride];
        if (!UnfilterScanlines(rawScanlines, unfiltered, height, stride, bytesPerPixel))
            return null;

        // Convert to 8-bit RGB or RGBA
        return ConvertPixels(unfiltered, width, height, bitDepth, colorType, channels, stride, palette, trns);
    }

    /// <summary>Extract a grayscale sample for bit depths 1, 2, 4, or 8.</summary>
    private static int GetGraySample(byte[] data, int rowOffset, int x, int bitDepth)
    {
        switch (bitDepth)
        {
            case 1:
            {
                byte b = data[rowOffset + (x >> 3)];
                return (b >> (7 - (x & 7))) & 0x1;
            }
            case 2:
            {
                byte b = data[rowOffset + (x >> 2)];
                return (b >> (6 - ((x & 3) << 1))) & 0x3;
            }
            case 4:
            {
                byte b = data[rowOffset + (x >> 1)];
                return (x & 1) == 0 ? (b >> 4) & 0xF : b & 0xF;
            }
            default:
                return data[rowOffset + x];
        }
    }

    /// <summary>Scale a sub-byte grayscale sample to the full 0-255 range.</summary>
    private static byte ScaleGraySample(int sample, int bitDepth)
    {
        if (bitDepth >= 8) return (byte)sample;
        int maxVal = (1 << bitDepth) - 1;
        return (byte)(sample * 255 / maxVal);
    }

    private static PngDecodeResult? ConvertPixels(byte[] unfiltered, int width, int height,
        int bitDepth, int colorType, int channels, int stride, byte[]? palette, byte[]? trns)
    {
        int pixelCount = width * height;

        switch (colorType)
        {
            case 0: // Grayscale
            {
                bool hasTransparency = trns != null && trns.Length >= 2;
                int trnsGray = hasTransparency ? ((trns![0] << 8) | trns[1]) : -1;

                if (hasTransparency)
                {
                    var rgba = new byte[pixelCount * 4];
                    for (int y = 0; y < height; y++)
                    {
                        int srcRow = y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            int gray;
                            if (bitDepth == 16)
                            {
                                int gray16 = (unfiltered[srcRow + x * 2] << 8) | unfiltered[srcRow + x * 2 + 1];
                                gray = gray16 >> 8; // truncate to 8-bit
                                int dstIdx = (y * width + x) * 4;
                                rgba[dstIdx] = (byte)gray;
                                rgba[dstIdx + 1] = (byte)gray;
                                rgba[dstIdx + 2] = (byte)gray;
                                rgba[dstIdx + 3] = (byte)(gray16 == trnsGray ? 0 : 255);
                            }
                            else
                            {
                                int sample = GetGraySample(unfiltered, srcRow, x, bitDepth);
                                gray = ScaleGraySample(sample, bitDepth);
                                int dstIdx = (y * width + x) * 4;
                                rgba[dstIdx] = (byte)gray;
                                rgba[dstIdx + 1] = (byte)gray;
                                rgba[dstIdx + 2] = (byte)gray;
                                rgba[dstIdx + 3] = (byte)(sample == trnsGray ? 0 : 255);
                            }
                        }
                    }
                    return new PngDecodeResult(width, height, rgba, true);
                }
                else
                {
                    var rgb = new byte[pixelCount * 3];
                    for (int y = 0; y < height; y++)
                    {
                        int srcRow = y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            byte gray;
                            if (bitDepth == 16)
                                gray = unfiltered[srcRow + x * 2]; // take high byte
                            else
                                gray = (byte)ScaleGraySample(GetGraySample(unfiltered, srcRow, x, bitDepth), bitDepth);

                            int dstIdx = (y * width + x) * 3;
                            rgb[dstIdx] = gray;
                            rgb[dstIdx + 1] = gray;
                            rgb[dstIdx + 2] = gray;
                        }
                    }
                    return new PngDecodeResult(width, height, rgb, false);
                }
            }

            case 2: // RGB
            {
                bool hasTransparency = trns != null && trns.Length >= 6;
                if (hasTransparency)
                {
                    int trnsR = (trns![0] << 8) | trns[1];
                    int trnsG = (trns[2] << 8) | trns[3];
                    int trnsB = (trns[4] << 8) | trns[5];

                    var rgba = new byte[pixelCount * 4];
                    for (int y = 0; y < height; y++)
                    {
                        int srcRow = y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            byte r, g, b;
                            bool transparent;
                            if (bitDepth == 16)
                            {
                                int srcIdx = srcRow + x * 6;
                                int r16 = (unfiltered[srcIdx] << 8) | unfiltered[srcIdx + 1];
                                int g16 = (unfiltered[srcIdx + 2] << 8) | unfiltered[srcIdx + 3];
                                int b16 = (unfiltered[srcIdx + 4] << 8) | unfiltered[srcIdx + 5];
                                r = (byte)(r16 >> 8);
                                g = (byte)(g16 >> 8);
                                b = (byte)(b16 >> 8);
                                transparent = r16 == trnsR && g16 == trnsG && b16 == trnsB;
                            }
                            else
                            {
                                int srcIdx = srcRow + x * 3;
                                r = unfiltered[srcIdx];
                                g = unfiltered[srcIdx + 1];
                                b = unfiltered[srcIdx + 2];
                                transparent = r == trnsR && g == trnsG && b == trnsB;
                            }
                            int dstIdx = (y * width + x) * 4;
                            rgba[dstIdx] = r;
                            rgba[dstIdx + 1] = g;
                            rgba[dstIdx + 2] = b;
                            rgba[dstIdx + 3] = (byte)(transparent ? 0 : 255);
                        }
                    }
                    return new PngDecodeResult(width, height, rgba, true);
                }
                else
                {
                    var rgb = new byte[pixelCount * 3];
                    for (int y = 0; y < height; y++)
                    {
                        int srcRow = y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            if (bitDepth == 16)
                            {
                                int srcIdx = srcRow + x * 6;
                                int dstIdx = (y * width + x) * 3;
                                rgb[dstIdx] = unfiltered[srcIdx];       // R high byte
                                rgb[dstIdx + 1] = unfiltered[srcIdx + 2]; // G high byte
                                rgb[dstIdx + 2] = unfiltered[srcIdx + 4]; // B high byte
                            }
                            else
                            {
                                int srcIdx = srcRow + x * 3;
                                int dstIdx = (y * width + x) * 3;
                                rgb[dstIdx] = unfiltered[srcIdx];
                                rgb[dstIdx + 1] = unfiltered[srcIdx + 1];
                                rgb[dstIdx + 2] = unfiltered[srcIdx + 2];
                            }
                        }
                    }
                    return new PngDecodeResult(width, height, rgb, false);
                }
            }

            case 3: // Indexed (palette)
            {
                if (palette == null || palette.Length < 3)
                    return null;

                int paletteEntries = palette.Length / 3;
                bool hasAlpha = trns != null && trns.Length > 0;

                if (hasAlpha)
                {
                    var rgba = new byte[pixelCount * 4];
                    for (int y = 0; y < height; y++)
                    {
                        int srcRow = y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            int index = GetPaletteIndex(unfiltered, srcRow, x, bitDepth);
                            if (index >= paletteEntries)
                                index = 0;

                            int dstIdx = (y * width + x) * 4;
                            rgba[dstIdx] = palette[index * 3];
                            rgba[dstIdx + 1] = palette[index * 3 + 1];
                            rgba[dstIdx + 2] = palette[index * 3 + 2];
                            rgba[dstIdx + 3] = (index < trns!.Length) ? trns[index] : (byte)255;
                        }
                    }
                    return new PngDecodeResult(width, height, rgba, true);
                }
                else
                {
                    var rgb = new byte[pixelCount * 3];
                    for (int y = 0; y < height; y++)
                    {
                        int srcRow = y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            int index = GetPaletteIndex(unfiltered, srcRow, x, bitDepth);
                            if (index >= paletteEntries)
                                index = 0;

                            int dstIdx = (y * width + x) * 3;
                            rgb[dstIdx] = palette[index * 3];
                            rgb[dstIdx + 1] = palette[index * 3 + 1];
                            rgb[dstIdx + 2] = palette[index * 3 + 2];
                        }
                    }
                    return new PngDecodeResult(width, height, rgb, false);
                }
            }

            case 4: // Grayscale + Alpha
            {
                var rgba = new byte[pixelCount * 4];
                for (int y = 0; y < height; y++)
                {
                    int srcRow = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        byte gray, alpha;
                        if (bitDepth == 16)
                        {
                            int srcIdx = srcRow + x * 4;
                            gray = unfiltered[srcIdx];     // high byte
                            alpha = unfiltered[srcIdx + 2]; // high byte
                        }
                        else
                        {
                            int srcIdx = srcRow + x * 2;
                            gray = unfiltered[srcIdx];
                            alpha = unfiltered[srcIdx + 1];
                        }

                        int dstIdx = (y * width + x) * 4;
                        rgba[dstIdx] = gray;
                        rgba[dstIdx + 1] = gray;
                        rgba[dstIdx + 2] = gray;
                        rgba[dstIdx + 3] = alpha;
                    }
                }
                return new PngDecodeResult(width, height, rgba, true);
            }

            case 6: // RGBA
            {
                var rgba = new byte[pixelCount * 4];
                for (int y = 0; y < height; y++)
                {
                    int srcRow = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        if (bitDepth == 16)
                        {
                            int srcIdx = srcRow + x * 8;
                            int dstIdx = (y * width + x) * 4;
                            rgba[dstIdx] = unfiltered[srcIdx];       // R high byte
                            rgba[dstIdx + 1] = unfiltered[srcIdx + 2]; // G high byte
                            rgba[dstIdx + 2] = unfiltered[srcIdx + 4]; // B high byte
                            rgba[dstIdx + 3] = unfiltered[srcIdx + 6]; // A high byte
                        }
                        else
                        {
                            int srcIdx = srcRow + x * 4;
                            int dstIdx = (y * width + x) * 4;
                            rgba[dstIdx] = unfiltered[srcIdx];
                            rgba[dstIdx + 1] = unfiltered[srcIdx + 1];
                            rgba[dstIdx + 2] = unfiltered[srcIdx + 2];
                            rgba[dstIdx + 3] = unfiltered[srcIdx + 3];
                        }
                    }
                }
                return new PngDecodeResult(width, height, rgba, true);
            }

            default:
                return null;
        }
    }

    private static int GetPaletteIndex(byte[] data, int rowOffset, int x, int bitDepth)
    {
        if (bitDepth == 8)
            return data[rowOffset + x];

        if (bitDepth == 4)
        {
            int byteIndex = rowOffset + x / 2;
            if (x % 2 == 0)
                return (data[byteIndex] >> 4) & 0x0F;
            else
                return data[byteIndex] & 0x0F;
        }

        if (bitDepth == 2)
        {
            int byteIndex = rowOffset + x / 4;
            int shift = (3 - (x % 4)) * 2;
            return (data[byteIndex] >> shift) & 0x03;
        }

        if (bitDepth == 1)
        {
            int byteIndex = rowOffset + x / 8;
            int shift = 7 - (x % 8);
            return (data[byteIndex] >> shift) & 0x01;
        }

        return 0;
    }

    private static int GetChannelCount(int colorType)
    {
        switch (colorType)
        {
            case 0: return 1; // Grayscale
            case 2: return 3; // RGB
            case 3: return 1; // Indexed (1 byte index)
            case 4: return 2; // Grayscale + Alpha
            case 6: return 4; // RGBA
            default: return 0;
        }
    }

    private static bool UnfilterScanlines(byte[] raw, byte[] output, int height, int stride, int bytesPerPixel)
    {
        int srcPos = 0;
        for (int y = 0; y < height; y++)
        {
            if (srcPos >= raw.Length)
                return false;

            byte filterType = raw[srcPos++];
            int dstRow = y * stride;
            int prevRow = (y - 1) * stride;

            if (srcPos + stride > raw.Length)
                return false;

            switch (filterType)
            {
                case 0: // None
                    Buffer.BlockCopy(raw, srcPos, output, dstRow, stride);
                    break;

                case 1: // Sub
                    for (int i = 0; i < stride; i++)
                    {
                        byte left = (i >= bytesPerPixel) ? output[dstRow + i - bytesPerPixel] : (byte)0;
                        output[dstRow + i] = (byte)(raw[srcPos + i] + left);
                    }
                    break;

                case 2: // Up
                    for (int i = 0; i < stride; i++)
                    {
                        byte up = (y > 0) ? output[prevRow + i] : (byte)0;
                        output[dstRow + i] = (byte)(raw[srcPos + i] + up);
                    }
                    break;

                case 3: // Average
                    for (int i = 0; i < stride; i++)
                    {
                        byte left = (i >= bytesPerPixel) ? output[dstRow + i - bytesPerPixel] : (byte)0;
                        byte up = (y > 0) ? output[prevRow + i] : (byte)0;
                        output[dstRow + i] = (byte)(raw[srcPos + i] + ((left + up) / 2));
                    }
                    break;

                case 4: // Paeth
                    for (int i = 0; i < stride; i++)
                    {
                        byte left = (i >= bytesPerPixel) ? output[dstRow + i - bytesPerPixel] : (byte)0;
                        byte up = (y > 0) ? output[prevRow + i] : (byte)0;
                        byte upLeft = (y > 0 && i >= bytesPerPixel) ? output[prevRow + i - bytesPerPixel] : (byte)0;
                        output[dstRow + i] = (byte)(raw[srcPos + i] + PaethPredictor(left, up, upLeft));
                    }
                    break;

                default:
                    return false; // unknown filter type
            }

            srcPos += stride;
        }

        return true;
    }

    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc) return a;
        if (pb <= pc) return b;
        return c;
    }

    private static byte[]? DecompressZlib(byte[] zlibData)
    {
        if (zlibData.Length < 2)
            return null;

        try
        {
            // Skip 2-byte zlib header (CMF + FLG)
            using var input = new MemoryStream(zlibData, 2, zlibData.Length - 2);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            var buffer = new byte[8192];
            int read;
            while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadChunk(byte[] data, ref int pos, out string chunkType, out byte[] chunkData)
    {
        chunkType = "";
        chunkData = Array.Empty<byte>();

        if (pos + 8 > data.Length)
            return false;

        int length = ReadInt32BE(data, pos);
        pos += 4;

        if (length < 0 || pos + 4 + length + 4 > data.Length)
            return false;

        chunkType = "" + (char)data[pos] + (char)data[pos + 1] + (char)data[pos + 2] + (char)data[pos + 3];
        pos += 4;

        chunkData = new byte[length];
        if (length > 0)
            Buffer.BlockCopy(data, pos, chunkData, 0, length);
        pos += length;

        // Skip CRC (4 bytes)
        pos += 4;

        return true;
    }

    private static int ReadInt32BE(byte[] data, int offset)
    {
        return (data[offset] << 24) | (data[offset + 1] << 16) |
               (data[offset + 2] << 8) | data[offset + 3];
    }
}

/// <summary>Result of PNG decoding.</summary>
internal sealed class PngDecodeResult
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>Pixel data: RGB (3 bytes/pixel) if HasAlpha is false, RGBA (4 bytes/pixel) if true.</summary>
    public byte[] PixelData { get; }
    /// <summary>True if PixelData contains RGBA data, false if RGB.</summary>
    public bool HasAlpha { get; }

    public PngDecodeResult(int width, int height, byte[] pixelData, bool hasAlpha)
    {
        Width = width;
        Height = height;
        PixelData = pixelData;
        HasAlpha = hasAlpha;
    }
}
