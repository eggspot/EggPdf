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

    private const int PngSignatureLength = 8;
    private const int ChunkLengthTypeAndCrcBytes = 12;
    private const int ZlibHeaderBytes = 2;
    private const int ColorTypeRgb = 2, ColorTypeRgba = 6;
    private const int FilterNone = 0, FilterSub = 1, FilterUp = 2, FilterAverage = 3, FilterPaeth = 4;

    /// <summary>
    /// Decode a PNG file to (width, height, RGBA pixel array).
    /// Supports color types 2 (RGB) and 6 (RGBA), 8-bit depth.
    /// Handles PNG row filters (None, Sub, Up, Average, Paeth). The IDAT data is zlib: a 2-byte header, deflate
    /// data and a checksum; each decoded row is a filter byte followed by the pixel bytes.
    /// </summary>
    internal static (int width, int height, byte[] pixels) DecodePng(byte[] data)
    {
        if (data.Length < PngSignatureLength || data[0] != 137 || data[1] != 80 || data[2] != 78 || data[3] != 71)
            throw new InvalidDataException("Not a valid PNG file");

        int pos = PngSignatureLength;
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
                if (colorType != ColorTypeRgb && colorType != ColorTypeRgba)
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

            pos += ChunkLengthTypeAndCrcBytes + chunkLen;
        }

        if (width == 0 || height == 0)
            throw new InvalidDataException("Missing IHDR chunk");

        var compressed = idatStream.ToArray();
        byte[] raw;
        using (var output = new MemoryStream())
        {
            using (var deflate = new DeflateStream(
                new MemoryStream(compressed, ZlibHeaderBytes, compressed.Length - ZlibHeaderBytes),
                CompressionMode.Decompress))
            {
                deflate.CopyTo(output);
            }
            raw = output.ToArray();
        }

        int channels = colorType == ColorTypeRgba ? 4 : 3;
        int bytesPerPixel = channels;
        int stride = width * channels;
        int rowSizeWithFilterByte = stride + 1;

        var rgbaPixels = new byte[width * height * 4];

        var prevRow = new byte[stride];
        var curRow = new byte[stride];

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * rowSizeWithFilterByte;
            if (rowStart >= raw.Length) break;

            int filter = raw[rowStart];

            int pixelDataStart = rowStart + 1;
            int copyLen = Math.Min(stride, raw.Length - pixelDataStart);
            Array.Clear(curRow, 0, stride);
            if (copyLen > 0)
                Array.Copy(raw, pixelDataStart, curRow, 0, copyLen);

            for (int x = 0; x < stride; x++)
            {
                byte rawByte = curRow[x];
                byte left = x >= bytesPerPixel ? curRow[x - bytesPerPixel] : (byte)0;
                byte above = prevRow[x];
                byte upperLeft = x >= bytesPerPixel ? prevRow[x - bytesPerPixel] : (byte)0;

                switch (filter)
                {
                    case FilterNone: break;
                    case FilterSub: curRow[x] = (byte)(rawByte + left); break;
                    case FilterUp: curRow[x] = (byte)(rawByte + above); break;
                    case FilterAverage: curRow[x] = (byte)(rawByte + (left + above) / 2); break;
                    case FilterPaeth: curRow[x] = (byte)(rawByte + PaethPredictor(left, above, upperLeft)); break;
                }
            }

            for (int x = 0; x < width; x++)
            {
                int source = x * channels;
                int target = (y * width + x) * 4;
                rgbaPixels[target] = curRow[source];
                rgbaPixels[target + 1] = curRow[source + 1];
                rgbaPixels[target + 2] = curRow[source + 2];
                rgbaPixels[target + 3] = channels == 4 ? curRow[source + 3] : (byte)255;
            }

            (prevRow, curRow) = (curRow, prevRow);
        }

        return (width, height, rgbaPixels);
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
