using System;

namespace EggPdf.Pdf;

/// <summary>
/// Basic WebP image format detection and metadata extraction.
/// WebP uses RIFF container with VP8 (lossy) or VP8L (lossless) codec.
/// Full VP8 decoding requires significant code (~5000 lines);
/// this provides format detection and dimension extraction.
/// For rendering, WebP images are passed through as-is with dimensions
/// noted, and the renderer falls back to a placeholder if needed.
/// </summary>
public static class WebPDecoder
{
    /// <summary>Check if data is a WebP image (RIFF/WEBP container).</summary>
    public static bool IsWebP(byte[] data)
    {
        if (data == null || data.Length < 12) return false;
        // RIFF header + WEBP signature
        return data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
               data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P';
    }

    /// <summary>
    /// Extract dimensions from a WebP file.
    /// Returns (width, height) or (0, 0) if unable to parse.
    /// </summary>
    public static (int width, int height) GetDimensions(byte[] data)
    {
        if (!IsWebP(data) || data.Length < 30)
            return (0, 0);

        try
        {
            // Find VP8 or VP8L chunk
            int pos = 12;
            while (pos + 8 < data.Length)
            {
                string chunkId = System.Text.Encoding.ASCII.GetString(data, pos, 4);
                uint chunkSize = (uint)(data[pos + 4] | (data[pos + 5] << 8) |
                                       (data[pos + 6] << 16) | (data[pos + 7] << 24));
                pos += 8;

                if (chunkId == "VP8 " && pos + 10 <= data.Length)
                {
                    // VP8 lossy: skip 3 bytes frame tag, then width/height
                    // Frame header: 3 bytes tag + 3 bytes start code (9D 01 2A) + width(2) + height(2)
                    int frameStart = pos;
                    // Find start code 0x9D012A
                    for (int i = frameStart; i < frameStart + 10 && i + 5 < data.Length; i++)
                    {
                        if (data[i] == 0x9D && data[i + 1] == 0x01 && data[i + 2] == 0x2A)
                        {
                            int width = (data[i + 3] | (data[i + 4] << 8)) & 0x3FFF;
                            int height = (data[i + 5] | (data[i + 6] << 8)) & 0x3FFF;
                            return (width, height);
                        }
                    }
                }
                else if (chunkId == "VP8L" && pos + 5 <= data.Length)
                {
                    // VP8L lossless: 1 byte signature (0x2F) + bitstream with width/height
                    if (data[pos] == 0x2F)
                    {
                        uint bits = (uint)(data[pos + 1] | (data[pos + 2] << 8) |
                                          (data[pos + 3] << 16) | (data[pos + 4] << 24));
                        int width = (int)(bits & 0x3FFF) + 1;
                        int height = (int)((bits >> 14) & 0x3FFF) + 1;
                        return (width, height);
                    }
                }
                else if (chunkId == "VP8X" && pos + 10 <= data.Length)
                {
                    // VP8X extended: canvas width at offset 4, height at offset 7 (24-bit LE each)
                    int width = (data[pos + 4] | (data[pos + 5] << 8) | (data[pos + 6] << 16)) + 1;
                    int height = (data[pos + 7] | (data[pos + 8] << 8) | (data[pos + 9] << 16)) + 1;
                    return (width, height);
                }

                pos += (int)chunkSize;
                if (pos % 2 != 0) pos++; // RIFF chunks are 2-byte aligned
            }
        }
        catch { }

        return (0, 0);
    }
}
