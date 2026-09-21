namespace EggPdf.Pdf;

/// <summary>
/// YUV 4:2:0 to RGB conversion for decoded VP8 frames: the chroma planes are upsampled with the
/// bilinear ("fancy") filter browsers use, and colour uses the BT.601 limited-range matrix in the
/// fixed-point form of the reference decoder, so results match Chrome's within rounding.
/// </summary>
internal static class Vp8Yuv
{
    /// <summary>Convert a frame to straight-alpha RGBA (alpha 255).</summary>
    public static byte[] ToRgba(Vp8Frame f)
    {
        int w = f.Width, h = f.Height;
        var rgba = new byte[w * h * 4];
        for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;

        // Row 0 stands alone, rows (1,2), (3,4), ... pair up around chroma row boundaries, and for an
        // even height the last row is alone again -- the same grouping the reference decoder emits.
        UpsampleLinePair(f, 0, -1, 0, 0, rgba, w);
        int y = 1;
        for (; y + 1 < h; y += 2)
            UpsampleLinePair(f, y, y + 1, (y >> 1), (y >> 1) + 1, rgba, w);
        if (h > 1 && (h & 1) == 0)
        {
            int lastChroma = (h - 1) >> 1;
            UpsampleLinePair(f, h - 1, -1, lastChroma, lastChroma, rgba, w);
        }
        return rgba;
    }

    private static void UpsampleLinePair(Vp8Frame f, int topRow, int bottomRow, int topUvRow, int curUvRow, byte[] rgba, int len)
    {
        int yTop = topRow * f.YStride, yBottom = bottomRow < 0 ? -1 : bottomRow * f.YStride;
        int uvTop = topUvRow * f.UvStride, uvCur = curUvRow * f.UvStride;
        int dstTop = topRow * len * 4, dstBottom = bottomRow < 0 ? -1 : bottomRow * len * 4;
        var U = f.U; var V = f.V;

        int lastPair = (len - 1) >> 1;
        int tlU = U[uvTop], tlV = V[uvTop];
        int lU = U[uvCur], lV = V[uvCur];

        Put(rgba, dstTop, f.Y[yTop], (3 * tlU + lU + 2) >> 2, (3 * tlV + lV + 2) >> 2);
        if (yBottom >= 0) Put(rgba, dstBottom, f.Y[yBottom], (3 * lU + tlU + 2) >> 2, (3 * lV + tlV + 2) >> 2);

        for (int x = 1; x <= lastPair; x++)
        {
            int tU = U[uvTop + x], tV = V[uvTop + x];
            int cU = U[uvCur + x], cV = V[uvCur + x];

            // The two diagonals of the 2x2 chroma neighbourhood, shared by the four output pixels
            int avgU = tlU + tU + lU + cU + 8, avgV = tlV + tV + lV + cV + 8;
            int diag12U = (avgU + 2 * (tU + lU)) >> 3, diag12V = (avgV + 2 * (tV + lV)) >> 3;
            int diag03U = (avgU + 2 * (tlU + cU)) >> 3, diag03V = (avgV + 2 * (tlV + cV)) >> 3;

            Put(rgba, dstTop + (2 * x - 1) * 4, f.Y[yTop + 2 * x - 1], (diag12U + tlU) >> 1, (diag12V + tlV) >> 1);
            Put(rgba, dstTop + 2 * x * 4, f.Y[yTop + 2 * x], (diag03U + tU) >> 1, (diag03V + tV) >> 1);
            if (yBottom >= 0)
            {
                Put(rgba, dstBottom + (2 * x - 1) * 4, f.Y[yBottom + 2 * x - 1], (diag03U + lU) >> 1, (diag03V + lV) >> 1);
                Put(rgba, dstBottom + 2 * x * 4, f.Y[yBottom + 2 * x], (diag12U + cU) >> 1, (diag12V + cV) >> 1);
            }

            tlU = tU; tlV = tV; lU = cU; lV = cV;
        }

        if ((len & 1) == 0)
        {
            Put(rgba, dstTop + (len - 1) * 4, f.Y[yTop + len - 1], (3 * tlU + lU + 2) >> 2, (3 * tlV + lV + 2) >> 2);
            if (yBottom >= 0)
                Put(rgba, dstBottom + (len - 1) * 4, f.Y[yBottom + len - 1], (3 * lU + tlU + 2) >> 2, (3 * lV + tlV + 2) >> 2);
        }
    }

    private static int MultHi(int v, int coeff) => (v * coeff) >> 8;

    private static byte Clip8(int v) => (v & ~16383) == 0 ? (byte)(v >> 6) : (byte)(v < 0 ? 0 : 255);

    private static void Put(byte[] rgba, int at, int y, int u, int v)
    {
        rgba[at] = Clip8(MultHi(y, 19077) + MultHi(v, 26149) - 14234);
        rgba[at + 1] = Clip8(MultHi(y, 19077) - MultHi(u, 6419) - MultHi(v, 13320) + 8708);
        rgba[at + 2] = Clip8(MultHi(y, 19077) + MultHi(u, 33050) - 17685);
    }
}
