namespace EggPdf.Pdf;

internal static partial class BmpDecoder
{
    private const int MaskFieldsOffset = FileHeaderSize + InfoHeaderSize;
    private const int HeaderSizeWithAlphaMask = 56;

    /// <summary>One channel of a 16- or 32-bit pixel: its bits in the pixel, scaled to 0-255 on extraction.</summary>
    private readonly struct ChannelMask
    {
        private readonly uint _mask;
        private readonly int _shift;
        private readonly ulong _max;

        public ChannelMask(uint mask)
        {
            _mask = mask;
            _shift = 0;
            if (mask == 0)
            {
                _max = 0;
                return;
            }
            while (((mask >> _shift) & 1) == 0) _shift++;
            _max = mask >> _shift;
        }

        public bool IsPresent => _mask != 0;

        public byte Extract(uint pixel)
        {
            ulong value = (pixel & _mask) >> _shift;
            if (_max == byte.MaxValue) return (byte)value;
            if (_max == 0) return 0;
            return (byte)((value * 255 + _max / 2) / _max);
        }
    }

    private readonly struct ChannelMasks
    {
        public readonly ChannelMask Red, Green, Blue, Alpha;

        public ChannelMasks(uint red, uint green, uint blue, uint alpha)
        {
            Red = new ChannelMask(red);
            Green = new ChannelMask(green);
            Blue = new ChannelMask(blue);
            Alpha = new ChannelMask(alpha);
        }
    }

    /// <summary>
    /// BI_RGB uses the default layouts (16-bit 5-5-5, 32-bit BGRA with the top byte as alpha). BI_BITFIELDS stores the
    /// red, green and blue masks at file offset 54, after a 40-byte header or inside a larger one; the alpha mask follows
    /// for BI_ALPHABITFIELDS and for headers of 56 bytes or more.
    /// </summary>
    private static ChannelMasks? ReadChannelMasks(byte[] data, int headerSize, int compression, int bitDepth)
    {
        if (compression == BI_RGB)
        {
            return bitDepth == 16
                ? new ChannelMasks(0x7C00, 0x03E0, 0x001F, 0)
                : new ChannelMasks(0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000);
        }

        bool hasAlphaMask = compression == BI_ALPHABITFIELDS || headerSize >= HeaderSizeWithAlphaMask;
        int maskCount = hasAlphaMask ? 4 : 3;
        if (MaskFieldsOffset + maskCount * 4 > data.Length)
            return null;

        return new ChannelMasks(
            ReadUInt32LE(data, MaskFieldsOffset),
            ReadUInt32LE(data, MaskFieldsOffset + 4),
            ReadUInt32LE(data, MaskFieldsOffset + 8),
            hasAlphaMask ? ReadUInt32LE(data, MaskFieldsOffset + 12) : 0);
    }

    /// <summary>
    /// Decodes 16- and 32-bit pixels through their channel masks. As in Chrome, an image whose alpha channel is zero
    /// everywhere is opaque: many writers leave the reserved byte of 32-bit BI_RGB pixels at zero.
    /// </summary>
    private static BmpDecodeResult DecodeMasked(byte[] data, int dataOffset, int width, int height, int rowStride,
        bool topDown, int bytesPerPixel, ChannelMasks masks)
    {
        int channels = masks.Alpha.IsPresent ? 4 : 3;
        var pixels = new byte[width * height * channels];
        bool anyVisibleAlpha = false;

        for (int y = 0; y < height; y++)
        {
            int srcRow = topDown ? y : (height - 1 - y);
            int srcOffset = dataOffset + srcRow * rowStride;
            int dstRow = y * width;

            for (int x = 0; x < width; x++)
            {
                int src = srcOffset + x * bytesPerPixel;
                if (src + bytesPerPixel > data.Length) break;

                uint pixel = bytesPerPixel == 2 ? (uint)(data[src] | (data[src + 1] << 8)) : ReadUInt32LE(data, src);
                int dst = (dstRow + x) * channels;
                pixels[dst] = masks.Red.Extract(pixel);
                pixels[dst + 1] = masks.Green.Extract(pixel);
                pixels[dst + 2] = masks.Blue.Extract(pixel);
                if (channels == 4)
                {
                    byte alpha = masks.Alpha.Extract(pixel);
                    pixels[dst + 3] = alpha;
                    anyVisibleAlpha |= alpha != 0;
                }
            }
        }

        if (channels == 4 && !anyVisibleAlpha)
            return new BmpDecodeResult(width, height, DropAlpha(pixels), false);
        return new BmpDecodeResult(width, height, pixels, channels == 4);
    }

    private static byte[] DropAlpha(byte[] rgba)
    {
        var rgb = new byte[rgba.Length / 4 * 3];
        for (int i = 0, j = 0; i < rgba.Length; i += 4, j += 3)
        {
            rgb[j] = rgba[i];
            rgb[j + 1] = rgba[i + 1];
            rgb[j + 2] = rgba[i + 2];
        }
        return rgb;
    }

    private static uint ReadUInt32LE(byte[] data, int offset)
        => (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
}
