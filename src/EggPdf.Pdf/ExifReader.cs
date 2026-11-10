using System;

namespace EggPdf.Pdf;

/// <summary>
/// Reads EXIF orientation tag from JPEG images.
/// Smartphone photos are often stored rotated with EXIF tag indicating correct orientation.
/// Without this, images appear sideways in the PDF.
/// </summary>
public static class ExifReader
{
    private const byte StartOfImage = 0xD8;
    private const byte App1Marker = 0xE1;
    private const byte StartOfScan = 0xDA;
    private const int OrientationTag = 0x0112;

    /// <summary>
    /// Read the EXIF orientation value from a JPEG file.
    /// Returns 1-8 (1 = normal, 3 = 180°, 6 = 90° CW, 8 = 90° CCW).
    /// Returns 1 if no EXIF or no orientation tag found.
    /// </summary>
    public static int GetOrientation(byte[] jpegData)
    {
        if (jpegData == null || jpegData.Length < 12) return 1;
        if (jpegData[0] != 0xFF || jpegData[1] != StartOfImage) return 1;

        try
        {
            int pos = 2;
            while (pos + 4 < jpegData.Length)
            {
                if (jpegData[pos] != 0xFF) break;
                byte marker = jpegData[pos + 1];

                if (marker == App1Marker)
                {
                    int segLen = (jpegData[pos + 2] << 8) | jpegData[pos + 3];
                    return ParseExifOrientation(jpegData, pos + 4, segLen - 2);
                }

                if (marker == StartOfScan) break;

                int len = (jpegData[pos + 2] << 8) | jpegData[pos + 3];
                pos += 2 + len;
            }
        }
        catch { }

        return 1;
    }

    private static int ParseExifOrientation(byte[] data, int offset, int length)
    {
        if (offset + 14 > data.Length) return 1;

        if (data[offset] != 'E' || data[offset + 1] != 'x' ||
            data[offset + 2] != 'i' || data[offset + 3] != 'f') return 1;

        int tiffStart = offset + 6;
        if (tiffStart + 8 > data.Length) return 1;

        bool littleEndian = data[tiffStart] == 'I' && data[tiffStart + 1] == 'I';

        int ifdOffset = ReadU32(data, tiffStart + 4, littleEndian);
        int ifdPos = tiffStart + ifdOffset;
        if (ifdPos + 2 > data.Length) return 1;

        int entryCount = ReadU16(data, ifdPos, littleEndian);
        ifdPos += 2;

        for (int i = 0; i < entryCount; i++)
        {
            if (ifdPos + 12 > data.Length) break;

            int tag = ReadU16(data, ifdPos, littleEndian);
            if (tag == OrientationTag)
            {
                int value = ReadU16(data, ifdPos + 8, littleEndian);
                return value >= 1 && value <= 8 ? value : 1;
            }
            ifdPos += 12;
        }

        return 1;
    }

    /// <summary>
    /// Get the PDF transformation matrix for a given EXIF orientation.
    /// Applied before drawing the image to correct its rotation: 1 normal, 2 mirrored, 3 rotated 180°, 4 mirrored and
    /// rotated 180°, 5 mirrored and rotated 90° counter-clockwise, 6 rotated 90° clockwise, 7 mirrored and rotated 90°
    /// clockwise, 8 rotated 90° counter-clockwise.
    /// </summary>
    public static (float a, float b, float c, float d, float e, float f) GetTransformMatrix(
        int orientation, float width, float height)
    {
        switch (orientation)
        {
            case 1: return (1, 0, 0, 1, 0, 0);
            case 2: return (-1, 0, 0, 1, width, 0);
            case 3: return (-1, 0, 0, -1, width, height);
            case 4: return (1, 0, 0, -1, 0, height);
            case 5: return (0, 1, 1, 0, 0, 0);
            case 6: return (0, 1, -1, 0, height, 0);
            case 7: return (0, -1, -1, 0, height, width);
            case 8: return (0, -1, 1, 0, 0, width);
            default: return (1, 0, 0, 1, 0, 0);
        }
    }

    private static int ReadU16(byte[] data, int offset, bool littleEndian)
    {
        if (littleEndian) return data[offset] | (data[offset + 1] << 8);
        return (data[offset] << 8) | data[offset + 1];
    }

    private static int ReadU32(byte[] data, int offset, bool littleEndian)
    {
        if (littleEndian) return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
        return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    }
}
