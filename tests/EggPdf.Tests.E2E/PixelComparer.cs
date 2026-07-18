using System;
using System.IO;
using System.IO.Compression;

namespace EggPdf.Tests.E2E;

/// <summary>
/// Decodes two Playwright PNG screenshots to raw RGBA pixels and computes
/// a pixel-similarity percentage. Used for visual regression testing:
/// comparing browser Print Preview against EggPdf PDF rendering.
/// </summary>
internal static class PixelComparer
{
    /// <summary>
    /// Compare two PNG screenshots and return the fraction of pixels that match
    /// (0.0 = completely different, 1.0 = identical).
    /// </summary>
    /// <param name="png1">First PNG screenshot bytes.</param>
    /// <param name="png2">Second PNG screenshot bytes.</param>
    /// <param name="channelTolerance">Per-channel tolerance (0-255). Pixels whose R/G/B each
    /// differ by at most this value are considered matching.</param>
    public static double Compare(byte[] png1, byte[] png2, int channelTolerance = 30)
    {
        var (w1, h1, p1) = DecodePng(png1);
        var (w2, h2, p2) = DecodePng(png2);

        int w = Math.Min(w1, w2);
        int h = Math.Min(h1, h2);
        if (w == 0 || h == 0) return 0.0;

        int totalPixels = w * h;
        int matching = 0;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i1 = (y * w1 + x) * 4;
                int i2 = (y * w2 + x) * 4;

                int dr = Math.Abs(p1[i1] - p2[i2]);
                int dg = Math.Abs(p1[i1 + 1] - p2[i2 + 1]);
                int db = Math.Abs(p1[i1 + 2] - p2[i2 + 2]);

                if (dr <= channelTolerance && dg <= channelTolerance && db <= channelTolerance)
                    matching++;
            }
        }

        return (double)matching / totalPixels;
    }

    /// <summary>
    /// Decode a PNG file to (width, height, RGBA pixel array).
    /// Supports color types 2 (RGB) and 6 (RGBA), 8-bit depth.
    /// Handles PNG row filters (None, Sub, Up, Average, Paeth).
    /// </summary>
    internal static (int width, int height, byte[] pixels) DecodePng(byte[] data)
    {
        // Validate PNG signature
        if (data.Length < 8 || data[0] != 137 || data[1] != 80 || data[2] != 78 || data[3] != 71)
            throw new InvalidDataException("Not a valid PNG file");

        int pos = 8;
        int width = 0, height = 0;
        int colorType = 0;
        using var idatStream = new MemoryStream();

        while (pos + 8 <= data.Length)
        {
            int chunkLen = ReadBE32(data, pos);
            var chunkType = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);

            if (chunkType == "IHDR" && chunkLen >= 13)
            {
                width = ReadBE32(data, pos + 8);
                height = ReadBE32(data, pos + 12);
                int bitDepth = data[pos + 16];
                colorType = data[pos + 17];

                if (bitDepth != 8)
                    throw new NotSupportedException($"Only 8-bit PNG supported, got {bitDepth}");
                if (colorType != 2 && colorType != 6)
                    throw new NotSupportedException($"Only RGB(2) and RGBA(6) PNG supported, got {colorType}");
            }
            else if (chunkType == "IDAT")
            {
                idatStream.Write(data, pos + 8, chunkLen);
            }
            else if (chunkType == "IEND")
            {
                break;
            }

            pos += 12 + chunkLen; // 4 length + 4 type + data + 4 CRC
        }

        if (width == 0 || height == 0)
            throw new InvalidDataException("Missing IHDR chunk");

        // Decompress IDAT (zlib = 2-byte header + deflate data + 4-byte checksum)
        var compressed = idatStream.ToArray();
        byte[] raw;
        using (var output = new MemoryStream())
        {
            // Skip zlib header (2 bytes)
            using (var deflate = new DeflateStream(
                new MemoryStream(compressed, 2, compressed.Length - 2),
                CompressionMode.Decompress))
            {
                deflate.CopyTo(output);
            }
            raw = output.ToArray();
        }

        // Unfilter rows
        int channels = colorType == 6 ? 4 : 3; // RGBA or RGB
        int bpp = channels; // bytes per pixel (8-bit)
        int stride = width * channels;
        int rowSize = stride + 1; // filter byte + pixel data

        var pixels = new byte[width * height * 4]; // always output RGBA

        var prevRow = new byte[stride];
        var curRow = new byte[stride];

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * rowSize;
            if (rowStart >= raw.Length) break;

            int filter = raw[rowStart];

            // Copy raw row data (skip filter byte)
            int srcStart = rowStart + 1;
            int copyLen = Math.Min(stride, raw.Length - srcStart);
            Array.Clear(curRow, 0, stride);
            if (copyLen > 0)
                Array.Copy(raw, srcStart, curRow, 0, copyLen);

            // Apply filter
            for (int x = 0; x < stride; x++)
            {
                byte rawByte = curRow[x];
                byte a = x >= bpp ? curRow[x - bpp] : (byte)0;   // left
                byte b = prevRow[x];                                // above
                byte c = (x >= bpp) ? prevRow[x - bpp] : (byte)0; // upper-left

                switch (filter)
                {
                    case 0: break; // None
                    case 1: curRow[x] = (byte)(rawByte + a); break; // Sub
                    case 2: curRow[x] = (byte)(rawByte + b); break; // Up
                    case 3: curRow[x] = (byte)(rawByte + (a + b) / 2); break; // Average
                    case 4: curRow[x] = (byte)(rawByte + PaethPredictor(a, b, c)); break; // Paeth
                }
            }

            // Write to output as RGBA
            for (int x = 0; x < width; x++)
            {
                int si = x * channels;
                int di = (y * width + x) * 4;
                pixels[di] = curRow[si];       // R
                pixels[di + 1] = curRow[si + 1]; // G
                pixels[di + 2] = curRow[si + 2]; // B
                pixels[di + 3] = channels == 4 ? curRow[si + 3] : (byte)255; // A
            }

            // Swap rows
            var tmp = prevRow;
            prevRow = curRow;
            curRow = tmp;
        }

        return (width, height, pixels);
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

    private static int ReadBE32(byte[] data, int offset)
    {
        return (data[offset] << 24) | (data[offset + 1] << 16) |
               (data[offset + 2] << 8) | data[offset + 3];
    }
}
