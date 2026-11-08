namespace EggPdf.Pdf;

/// <summary>
/// Reads the natural pixel size of a PNG, JPEG, GIF, BMP or WebP file from its header, without
/// decoding the pixels, plus the EXIF orientation of a JPEG.
/// </summary>
public static class ImageIntrinsicSize
{
    private const int NormalOrientation = 1;
    private const int FirstTransposedOrientation = 5;

    /// <summary>
    /// Read the stored width and height in pixels and the EXIF orientation (1-8; 1 when absent or not
    /// a JPEG). Returns false when the format is not recognised or the header is damaged.
    /// </summary>
    public static bool TryRead(byte[] data, out int width, out int height, out int exifOrientation)
    {
        width = height = 0;
        exifOrientation = NormalOrientation;
        if (data == null || data.Length < 10) return false;

        if (IsPng(data)) return TryReadPng(data, out width, out height);
        if (data[0] == 0xFF && data[1] == 0xD8)
        {
            if (!TryReadJpeg(data, out width, out height)) return false;
            exifOrientation = ExifReader.GetOrientation(data);
            return true;
        }
        if (data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8')
        {
            width = data[6] | (data[7] << 8);
            height = data[8] | (data[9] << 8);
            return width > 0 && height > 0;
        }
        if (data[0] == 'B' && data[1] == 'M') return TryReadBmp(data, out width, out height);
        if (WebPDecoder.IsWebP(data)) return TryReadWebP(data, out width, out height);
        return false;
    }

    /// <summary>True when an EXIF orientation swaps width and height (orientations 5-8).</summary>
    public static bool SwapsWidthAndHeight(int exifOrientation) => exifOrientation >= FirstTransposedOrientation && exifOrientation <= 8;

    private static bool IsPng(byte[] data) =>
        data.Length >= 24 && data[0] == 137 && data[1] == 80 && data[2] == 78 && data[3] == 71;

    private static bool TryReadPng(byte[] data, out int width, out int height)
    {
        width = ReadInt32BigEndian(data, 16);
        height = ReadInt32BigEndian(data, 20);
        return width > 0 && height > 0;
    }

    private static bool TryReadJpeg(byte[] data, out int width, out int height)
    {
        width = height = 0;
        int pos = 2;
        while (pos + 4 <= data.Length)
        {
            if (data[pos] != 0xFF) { pos++; continue; }
            byte marker = data[pos + 1];
            if (marker == 0xFF) { pos++; continue; }
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { pos += 2; continue; }
            if (marker == 0xD9 || marker == 0xDA) return false;

            int segmentLength = (data[pos + 2] << 8) | data[pos + 3];
            if (segmentLength < 2) return false;
            if (IsStartOfFrame(marker))
            {
                if (pos + 9 > data.Length) return false;
                height = (data[pos + 5] << 8) | data[pos + 6];
                width = (data[pos + 7] << 8) | data[pos + 8];
                return width > 0 && height > 0;
            }
            pos += 2 + segmentLength;
        }
        return false;
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;

    private static bool TryReadBmp(byte[] data, out int width, out int height)
    {
        width = height = 0;
        if (data.Length < 26) return false;
        width = ReadInt32LittleEndian(data, 18);
        int signedHeight = ReadInt32LittleEndian(data, 22);
        height = signedHeight == int.MinValue ? 0 : System.Math.Abs(signedHeight);
        return width > 0 && height > 0;
    }

    private static bool TryReadWebP(byte[] data, out int width, out int height)
    {
        width = height = 0;
        if (data.Length < 30) return false;
        if (data[12] == 'V' && data[13] == 'P' && data[14] == '8' && data[15] == ' ')
        {
            width = (data[26] | (data[27] << 8)) & 0x3FFF;
            height = (data[28] | (data[29] << 8)) & 0x3FFF;
        }
        else if (data[12] == 'V' && data[13] == 'P' && data[14] == '8' && data[15] == 'L')
        {
            int bits = data[21] | (data[22] << 8) | (data[23] << 16) | (data[24] << 24);
            width = (bits & 0x3FFF) + 1;
            height = ((bits >> 14) & 0x3FFF) + 1;
        }
        else if (data[12] == 'V' && data[13] == 'P' && data[14] == '8' && data[15] == 'X')
        {
            width = (data[24] | (data[25] << 8) | (data[26] << 16)) + 1;
            height = (data[27] | (data[28] << 8) | (data[29] << 16)) + 1;
        }
        return width > 0 && height > 0;
    }

    private static int ReadInt32BigEndian(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static int ReadInt32LittleEndian(byte[] data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
}
