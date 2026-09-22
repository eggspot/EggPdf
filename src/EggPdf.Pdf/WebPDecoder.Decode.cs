using System;

namespace EggPdf.Pdf;

// Full WebP decoding: the RIFF container (simple, extended VP8X with ALPH/ICCP/EXIF chunks, and
// the first frame of an animation), the lossy VP8 codec with its separately-coded alpha plane,
// and the lossless VP8L codec. Detection and dimension probing live in WebPDecoder.cs.
public static partial class WebPDecoder
{
    private const int FlagAnimation = 0x02;

    private readonly struct Chunk
    {
        public readonly string Id;
        public readonly int Offset, Size;
        public Chunk(string id, int offset, int size) { Id = id; Offset = offset; Size = size; }
    }

    /// <summary>
    /// Decode a WebP file to straight-alpha RGBA (row-major, top to bottom). Animated files decode
    /// their first frame. Returns null for malformed or unsupported data.
    /// </summary>
    public static (int width, int height, byte[] rgba)? Decode(byte[] data)
    {
        if (!IsWebP(data) || data.Length < 20) return null;
        try
        {
            int end = (int)Math.Min((long)data.Length, 8L + ReadLe32(data, 4));
            var chunks = ReadChunks(data, 12, end);

            int canvasW = 0, canvasH = 0;
            bool animated = false;
            foreach (var c in chunks)
            {
                if (c.Id != "VP8X" || c.Size < 10) continue;
                animated = (data[c.Offset] & FlagAnimation) != 0;
                canvasW = ReadLe24(data, c.Offset + 4) + 1;
                canvasH = ReadLe24(data, c.Offset + 7) + 1;
            }

            if (animated)
            {
                foreach (var c in chunks)
                    if (c.Id == "ANMF" && c.Size > 16) return DecodeFirstFrame(data, c, canvasW, canvasH);
                return null;
            }
            return DecodeImage(data, chunks);
        }
        catch (Exception)
        {
            return null; // infallible by convention
        }
    }

    private static (int, int, byte[])? DecodeImage(byte[] data, System.Collections.Generic.List<Chunk> chunks)
    {
        Chunk? alph = null, vp8 = null, vp8l = null;
        foreach (var c in chunks)
        {
            if (c.Id == "ALPH" && alph == null) alph = c;
            else if (c.Id == "VP8 " && vp8 == null) vp8 = c;
            else if (c.Id == "VP8L" && vp8l == null) vp8l = c;
        }

        if (vp8l.HasValue)
        {
            var payload = new byte[vp8l.Value.Size];
            Array.Copy(data, vp8l.Value.Offset, payload, 0, payload.Length);
            var lossless = Vp8LDecoder.Decode(payload);
            return lossless == null ? null : (lossless.Value.width, lossless.Value.height, lossless.Value.argb);
        }
        if (!vp8.HasValue) return null;

        var frame = Vp8Decoder.Decode(data, vp8.Value.Offset, vp8.Value.Size);
        if (frame == null) return null;

        var rgba = Vp8Yuv.ToRgba(frame);
        if (alph.HasValue && !ApplyAlpha(data, alph.Value, frame.Width, frame.Height, rgba)) return null;
        return (frame.Width, frame.Height, rgba);
    }

    /// <summary>The first frame of an animation, placed on a transparent canvas of the animation's size.</summary>
    private static (int, int, byte[])? DecodeFirstFrame(byte[] data, Chunk anmf, int canvasW, int canvasH)
    {
        int frameX = ReadLe24(data, anmf.Offset) * 2, frameY = ReadLe24(data, anmf.Offset + 3) * 2;
        var frameChunks = ReadChunks(data, anmf.Offset + 16, anmf.Offset + anmf.Size);
        var frame = DecodeImage(data, frameChunks);
        if (frame == null) return null;

        var (fw, fh, pixels) = frame.Value;
        if (canvasW <= 0 || canvasH <= 0) return (fw, fh, pixels);

        var canvas = new byte[canvasW * canvasH * 4];
        for (int y = 0; y < fh && frameY + y < canvasH; y++)
        {
            int copy = Math.Min(fw, canvasW - frameX);
            if (copy > 0) Array.Copy(pixels, y * fw * 4, canvas, ((frameY + y) * canvasW + frameX) * 4, copy * 4);
        }
        return (canvasW, canvasH, canvas);
    }

    private static System.Collections.Generic.List<Chunk> ReadChunks(byte[] data, int start, int end)
    {
        var chunks = new System.Collections.Generic.List<Chunk>();
        int pos = start;
        while (pos + 8 <= end)
        {
            string id = System.Text.Encoding.ASCII.GetString(data, pos, 4);
            int size = (int)Math.Min(ReadLe32(data, pos + 4), (uint)int.MaxValue);
            int payload = pos + 8;
            if (size < 0 || payload + size > end) size = Math.Max(0, end - payload); // tolerate a truncated last chunk
            chunks.Add(new Chunk(id, payload, size));
            pos = payload + size + (size & 1); // chunks are padded to even sizes
        }
        return chunks;
    }

    private static uint ReadLe32(byte[] d, int at) => (uint)(d[at] | (d[at + 1] << 8) | (d[at + 2] << 16) | (d[at + 3] << 24));
    private static int ReadLe24(byte[] d, int at) => d[at] | (d[at + 1] << 8) | (d[at + 2] << 16);

    // ── Alpha plane (ALPH chunk) ─────────────────────────────────────────────

    /// <summary>Decode the ALPH chunk and store it in the alpha channel of <paramref name="rgba"/>.</summary>
    private static bool ApplyAlpha(byte[] data, Chunk alph, int width, int height, byte[] rgba)
    {
        if (alph.Size <= 1) return false;

        int header = data[alph.Offset];
        int method = header & 3;
        int filter = (header >> 2) & 3;
        int payloadOffset = alph.Offset + 1, payloadSize = alph.Size - 1;

        var plane = new byte[width * height];
        if (method == 0)
        {
            if (payloadSize < plane.Length) return false;
            Array.Copy(data, payloadOffset, plane, 0, plane.Length);
        }
        else if (method == 1)
        {
            // The alpha plane is a headerless VP8L image stream whose green channel carries the values;
            // give it the 5-byte VP8L header the decoder expects (signature + dimensions).
            var stream = new byte[5 + payloadSize];
            stream[0] = 0x2F;
            uint bits = (uint)(width - 1) | ((uint)(height - 1) << 14);
            stream[1] = (byte)bits; stream[2] = (byte)(bits >> 8); stream[3] = (byte)(bits >> 16); stream[4] = (byte)(bits >> 24);
            Array.Copy(data, payloadOffset, stream, 5, payloadSize);

            var decoded = Vp8LDecoder.Decode(stream);
            if (decoded == null) return false;
            var argb = decoded.Value.argb;
            for (int i = 0; i < plane.Length; i++) plane[i] = argb[i * 4 + 1]; // green
        }
        else
        {
            return false;
        }

        UnfilterAlpha(plane, width, height, filter);
        for (int i = 0; i < plane.Length; i++) rgba[i * 4 + 3] = plane[i];
        return true;
    }

    /// <summary>Undo the predictive filter the encoder applied to the alpha plane (none, horizontal, vertical, gradient).</summary>
    private static void UnfilterAlpha(byte[] plane, int width, int height, int filter)
    {
        if (filter == 0) return;
        for (int y = 0; y < height; y++)
        {
            int row = y * width, prev = row - width;
            if (y == 0 || filter == 1)
            {
                // Horizontal: left prediction (the first pixel of a row predicts from the pixel above)
                byte pred = y == 0 ? (byte)0 : plane[prev];
                for (int x = 0; x < width; x++)
                {
                    plane[row + x] = (byte)(plane[row + x] + pred);
                    pred = plane[row + x];
                }
            }
            else if (filter == 2)
            {
                for (int x = 0; x < width; x++) plane[row + x] = (byte)(plane[row + x] + plane[prev + x]);
            }
            else // gradient
            {
                int top = plane[prev], topLeft = top, left = top;
                for (int x = 0; x < width; x++)
                {
                    top = plane[prev + x];
                    int g = left + top - topLeft;
                    int predicted = g < 0 ? 0 : g > 255 ? 255 : g;
                    left = (byte)(plane[row + x] + predicted);
                    topLeft = top;
                    plane[row + x] = (byte)left;
                }
            }
        }
    }
}
