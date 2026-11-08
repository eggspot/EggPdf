using System;

namespace EggPdf.Pdf;

internal static partial class PngDecoder
{
    private const int Adam7InterlaceMethod = 1;

    private static readonly int[] Adam7XStart = { 0, 4, 0, 2, 0, 1, 0 };
    private static readonly int[] Adam7YStart = { 0, 0, 4, 0, 2, 0, 1 };
    private static readonly int[] Adam7XStep = { 8, 8, 4, 4, 2, 2, 1 };
    private static readonly int[] Adam7YStep = { 8, 8, 8, 4, 4, 2, 2 };

    private static byte[]? DeinterlaceAdam7(byte[] rawScanlines, int width, int height,
        int bitsPerPixel, int bytesPerPixel, int stride)
    {
        if (Adam7ScanlineBytes(width, height, bitsPerPixel) > rawScanlines.Length)
            return null;

        var image = new byte[(long)height * stride];
        int offset = 0;
        for (int pass = 0; pass < Adam7XStart.Length; pass++)
        {
            int passWidth = PassWidth(pass, width);
            int passHeight = PassHeight(pass, height);
            if (passWidth <= 0 || passHeight <= 0) continue;

            int passStride = (int)PassStride(passWidth, bitsPerPixel);
            var passPixels = new byte[(long)passHeight * passStride];
            if (!UnfilterScanlines(rawScanlines, passPixels, passHeight, passStride, bytesPerPixel, offset))
                return null;
            offset += passHeight * (passStride + 1);

            for (int y = 0; y < passHeight; y++)
            {
                int targetRow = (Adam7YStart[pass] + y * Adam7YStep[pass]) * stride;
                int sourceRow = y * passStride;
                for (int x = 0; x < passWidth; x++)
                    CopyPixel(passPixels, sourceRow, x, image, targetRow, Adam7XStart[pass] + x * Adam7XStep[pass], bitsPerPixel);
            }
        }
        return image;
    }

    private static long Adam7ScanlineBytes(int width, int height, int bitsPerPixel)
    {
        long total = 0;
        for (int pass = 0; pass < Adam7XStart.Length; pass++)
        {
            int passWidth = PassWidth(pass, width);
            int passHeight = PassHeight(pass, height);
            if (passWidth > 0 && passHeight > 0)
                total += (long)passHeight * (PassStride(passWidth, bitsPerPixel) + 1);
        }
        return total;
    }

    private static int PassWidth(int pass, int width) => (width - Adam7XStart[pass] + Adam7XStep[pass] - 1) / Adam7XStep[pass];

    private static int PassHeight(int pass, int height) => (height - Adam7YStart[pass] + Adam7YStep[pass] - 1) / Adam7YStep[pass];

    private static long PassStride(int passWidth, int bitsPerPixel) => ((long)passWidth * bitsPerPixel + 7) / 8;

    private static void CopyPixel(byte[] source, int sourceRow, int sourceX, byte[] target, int targetRow, int targetX, int bitsPerPixel)
    {
        if (bitsPerPixel >= 8)
        {
            int bytes = bitsPerPixel / 8;
            Buffer.BlockCopy(source, sourceRow + sourceX * bytes, target, targetRow + targetX * bytes, bytes);
            return;
        }

        int pixelsPerByte = 8 / bitsPerPixel;
        int mask = (1 << bitsPerPixel) - 1;
        int sourceShift = 8 - bitsPerPixel * (sourceX % pixelsPerByte + 1);
        int value = (source[sourceRow + sourceX / pixelsPerByte] >> sourceShift) & mask;
        int targetShift = 8 - bitsPerPixel * (targetX % pixelsPerByte + 1);
        int targetIndex = targetRow + targetX / pixelsPerByte;
        target[targetIndex] = (byte)((target[targetIndex] & ~(mask << targetShift)) | (value << targetShift));
    }
}
