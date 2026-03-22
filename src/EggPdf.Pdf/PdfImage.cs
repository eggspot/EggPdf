using System;
using System.Collections.Generic;

namespace EggPdf.Pdf;

/// <summary>
/// Represents an image to be embedded in a PDF document.
/// Supports JPEG (DCTDecode pass-through) and raw RGB/RGBA pixel data (FlateDecode).
/// </summary>
public class PdfImage
{
    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }
    public PdfImageFormat Format { get; }
    public int BitsPerComponent { get; }
    public byte[]? SMaskData { get; }

    private PdfImage(string name, int width, int height, byte[] data,
        PdfImageFormat format, int bitsPerComponent, byte[]? smaskData)
    {
        Name = name;
        Width = width;
        Height = height;
        Data = data;
        Format = format;
        BitsPerComponent = bitsPerComponent;
        SMaskData = smaskData;
    }

    /// <summary>Create image from JPEG data (pass-through, no decoding needed).</summary>
    public static PdfImage? FromJpeg(string name, byte[] jpegData)
    {
        if (jpegData == null || jpegData.Length < 2)
            return null;

        // Verify JPEG SOI marker
        if (jpegData[0] != 0xFF || jpegData[1] != 0xD8)
            return null;

        // Parse JPEG headers to get dimensions
        int width = 0, height = 0;
        int pos = 2;
        while (pos < jpegData.Length - 1)
        {
            if (jpegData[pos] != 0xFF) break;
            byte marker = jpegData[pos + 1];
            pos += 2;

            // SOF markers (Start of Frame) contain dimensions
            if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
            {
                if (pos + 7 <= jpegData.Length)
                {
                    // Skip length(2) + precision(1)
                    height = (jpegData[pos + 3] << 8) | jpegData[pos + 4];
                    width = (jpegData[pos + 5] << 8) | jpegData[pos + 6];
                }
                break;
            }

            // Skip segment
            if (pos + 2 <= jpegData.Length)
            {
                int segLen = (jpegData[pos] << 8) | jpegData[pos + 1];
                pos += segLen;
            }
            else break;
        }

        if (width <= 0 || height <= 0)
            return null;

        return new PdfImage(name, width, height, jpegData, PdfImageFormat.Jpeg, 8, null);
    }

    /// <summary>Create image from PNG data (decoded to RGB or RGBA).</summary>
    public static PdfImage? FromPng(string name, byte[] pngData)
    {
        if (pngData == null || pngData.Length < 8)
            return null;

        var result = PngDecoder.Decode(pngData);
        if (result == null)
            return null;

        if (result.HasAlpha)
            return FromRgba(name, result.Width, result.Height, result.PixelData);
        else
            return FromRgb(name, result.Width, result.Height, result.PixelData);
    }

    /// <summary>Create image from GIF data (decoded to RGB or RGBA).</summary>
    public static PdfImage? FromGif(string name, byte[] gifData)
    {
        if (gifData == null || gifData.Length < 6)
            return null;

        var result = GifDecoder.Decode(gifData);
        if (result == null)
            return null;

        if (result.HasAlpha)
            return FromRgba(name, result.Width, result.Height, result.PixelData);
        else
            return FromRgb(name, result.Width, result.Height, result.PixelData);
    }

    /// <summary>Create image from BMP data (decoded to RGB or RGBA).</summary>
    public static PdfImage? FromBmp(string name, byte[] bmpData)
    {
        if (bmpData == null || bmpData.Length < 14)
            return null;

        var result = BmpDecoder.Decode(bmpData);
        if (result == null)
            return null;

        if (result.HasAlpha)
            return FromRgba(name, result.Width, result.Height, result.PixelData);
        else
            return FromRgb(name, result.Width, result.Height, result.PixelData);
    }

    /// <summary>Create image from raw RGB pixel data.</summary>
    public static PdfImage FromRgb(string name, int width, int height, byte[] rgbData)
    {
        return new PdfImage(name, width, height, rgbData, PdfImageFormat.Raw, 8, null);
    }

    /// <summary>Create image from raw RGBA pixel data (alpha channel becomes SMask).</summary>
    public static PdfImage FromRgba(string name, int width, int height, byte[] rgbaData)
    {
        // Separate RGB and alpha channels
        int pixelCount = width * height;
        var rgb = new byte[pixelCount * 3];
        var alpha = new byte[pixelCount];

        for (int i = 0; i < pixelCount; i++)
        {
            rgb[i * 3] = rgbaData[i * 4];
            rgb[i * 3 + 1] = rgbaData[i * 4 + 1];
            rgb[i * 3 + 2] = rgbaData[i * 4 + 2];
            alpha[i] = rgbaData[i * 4 + 3];
        }

        return new PdfImage(name, width, height, rgb, PdfImageFormat.Raw, 8, alpha);
    }
}

public enum PdfImageFormat
{
    Jpeg,
    Raw
}
