using System;

namespace EggPdf.Pdf;

// Intra prediction, inverse transforms and macroblock reconstruction. Each macroblock is built in
// a small scratch buffer whose row -1 and column -1 hold the neighbouring samples the predictors
// read (127 above the picture, 129 left of it, per the specification).
internal sealed partial class Vp8Decoder
{
    private static byte Clip8(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

    private void InitScratchBorders(byte[] s, int mbY)
    {
        for (int j = 0; j < 16; j++) s[YOffset + j * Bps - 1] = 129;
        for (int j = 0; j < 8; j++)
        {
            s[UOffset + j * Bps - 1] = 129;
            s[VOffset + j * Bps - 1] = 129;
        }

        if (mbY > 0)
        {
            s[YOffset - 1 - Bps] = s[UOffset - 1 - Bps] = s[VOffset - 1 - Bps] = 129;
        }
        else
        {
            // The whole top border (including the four top-right samples) is 127 for the first row
            for (int i = 0; i < 16 + 4 + 1; i++) s[YOffset - Bps - 1 + i] = 127;
            for (int i = 0; i < 8 + 1; i++) { s[UOffset - Bps - 1 + i] = 127; s[VOffset - Bps - 1 + i] = 127; }
        }
    }

    private void ReconstructMacroblock(byte[] s, int mbX, int mbY)
    {
        var f = _frame;

        // Rotate the samples of the macroblock to the left into the scratch buffer's left border
        if (mbX > 0)
        {
            for (int j = -1; j < 16; j++) Array.Copy(s, YOffset + j * Bps + 12, s, YOffset + j * Bps - 4, 4);
            for (int j = -1; j < 8; j++)
            {
                Array.Copy(s, UOffset + j * Bps + 4, s, UOffset + j * Bps - 4, 4);
                Array.Copy(s, VOffset + j * Bps + 4, s, VOffset + j * Bps - 4, 4);
            }
        }

        // Samples above come from the (still unfiltered) picture
        if (mbY > 0)
        {
            int yAbove = (mbY * 16 - 1) * f.YStride + mbX * 16;
            Array.Copy(f.Y, yAbove, s, YOffset - Bps, 16);
            int uvAbove = (mbY * 8 - 1) * f.UvStride + mbX * 8;
            Array.Copy(f.U, uvAbove, s, UOffset - Bps, 8);
            Array.Copy(f.V, uvAbove, s, VOffset - Bps, 8);
        }

        if (_isI4x4)
        {
            // The four samples above and to the right of the macroblock feed the rightmost sub-blocks
            int topRight = YOffset - Bps + 16;
            if (mbY > 0)
            {
                int yAbove = (mbY * 16 - 1) * f.YStride + mbX * 16;
                if (mbX >= _mbW - 1)
                    for (int i = 0; i < 4; i++) s[topRight + i] = f.Y[yAbove + 15];
                else
                    Array.Copy(f.Y, yAbove + 16, s, topRight, 4);
            }
            // Sub-blocks in the lower rows reuse those same samples as their above-right neighbours
            for (int row = 1; row < 4; row++) Array.Copy(s, topRight, s, topRight + row * 4 * Bps, 4);

            for (int n = 0; n < 16; n++)
            {
                int dst = YOffset + (n & 3) * 4 + (n >> 2) * 4 * Bps;
                PredictSubblock(s, dst, _modes[n]);
                if (_blockHasCoeffs[n]) InverseDct(_coeffs, n * 16, s, dst);
            }
        }
        else
        {
            PredictLuma16(s, YOffset, _modes[0], mbX, mbY);
            for (int n = 0; n < 16; n++)
                if (_blockHasCoeffs[n]) InverseDct(_coeffs, n * 16, s, YOffset + (n & 3) * 4 + (n >> 2) * 4 * Bps);
        }

        PredictChroma8(s, UOffset, _uvMode, mbX, mbY);
        PredictChroma8(s, VOffset, _uvMode, mbX, mbY);
        for (int n = 0; n < 4; n++)
        {
            int at = (n & 1) * 4 + (n >> 1) * 4 * Bps;
            if (_blockHasCoeffs[16 + n]) InverseDct(_coeffs, (16 + n) * 16, s, UOffset + at);
            if (_blockHasCoeffs[20 + n]) InverseDct(_coeffs, (20 + n) * 16, s, VOffset + at);
        }

        // Copy the finished macroblock into the picture
        for (int j = 0; j < 16; j++)
            Array.Copy(s, YOffset + j * Bps, f.Y, (mbY * 16 + j) * f.YStride + mbX * 16, 16);
        for (int j = 0; j < 8; j++)
        {
            int at = (mbY * 8 + j) * f.UvStride + mbX * 8;
            Array.Copy(s, UOffset + j * Bps, f.U, at, 8);
            Array.Copy(s, VOffset + j * Bps, f.V, at, 8);
        }
    }

    // ── Transforms (RFC 6386 section 14) ─────────────────────────────────────

    private static int Mul1(int a) => ((a * 20091) >> 16) + a;
    private static int Mul2(int a) => (a * 35468) >> 16;

    /// <summary>Inverse 4x4 DCT of one block, added onto the prediction in the scratch buffer.</summary>
    private static void InverseDct(short[] coeffs, int at, byte[] dst, int dstAt)
    {
        var tmp = new int[16];
        for (int i = 0; i < 4; i++) // vertical pass
        {
            int in0 = coeffs[at + i], in4 = coeffs[at + 4 + i], in8 = coeffs[at + 8 + i], in12 = coeffs[at + 12 + i];
            int a = in0 + in8, b = in0 - in8;
            int c = Mul2(in4) - Mul1(in12);
            int d = Mul1(in4) + Mul2(in12);
            tmp[4 * i + 0] = a + d;
            tmp[4 * i + 1] = b + c;
            tmp[4 * i + 2] = b - c;
            tmp[4 * i + 3] = a - d;
        }
        for (int i = 0; i < 4; i++) // horizontal pass
        {
            int dc = tmp[i] + 4;
            int a = dc + tmp[8 + i], b = dc - tmp[8 + i];
            int c = Mul2(tmp[4 + i]) - Mul1(tmp[12 + i]);
            int d = Mul1(tmp[4 + i]) + Mul2(tmp[12 + i]);
            int row = dstAt + i * Bps;
            dst[row + 0] = Clip8(dst[row + 0] + ((a + d) >> 3));
            dst[row + 1] = Clip8(dst[row + 1] + ((b + c) >> 3));
            dst[row + 2] = Clip8(dst[row + 2] + ((b - c) >> 3));
            dst[row + 3] = Clip8(dst[row + 3] + ((a - d) >> 3));
        }
    }

    /// <summary>Inverse Walsh-Hadamard transform of the Y2 block: the 16 results become the DC of each luma block.</summary>
    private static void InverseWalshHadamard(short[] input, short[] output)
    {
        var tmp = new int[16];
        for (int i = 0; i < 4; i++)
        {
            int a0 = input[i] + input[12 + i];
            int a1 = input[4 + i] + input[8 + i];
            int a2 = input[4 + i] - input[8 + i];
            int a3 = input[i] - input[12 + i];
            tmp[i] = a0 + a1;
            tmp[8 + i] = a0 - a1;
            tmp[4 + i] = a3 + a2;
            tmp[12 + i] = a3 - a2;
        }
        int outAt = 0;
        for (int i = 0; i < 4; i++)
        {
            int dc = tmp[i * 4] + 3;
            int a0 = dc + tmp[3 + i * 4];
            int a1 = tmp[1 + i * 4] + tmp[2 + i * 4];
            int a2 = tmp[1 + i * 4] - tmp[2 + i * 4];
            int a3 = dc - tmp[3 + i * 4];
            output[outAt] = (short)((a0 + a1) >> 3);
            output[outAt + 16] = (short)((a3 + a2) >> 3);
            output[outAt + 32] = (short)((a0 - a1) >> 3);
            output[outAt + 48] = (short)((a3 - a2) >> 3);
            outAt += 64;
        }
    }

    // ── 16x16 and chroma prediction ──────────────────────────────────────────

    // DC prediction adapts to the picture edge; the other modes read the 127/129 border samples directly.
    private static void PredictLuma16(byte[] s, int dst, int mode, int mbX, int mbY)
    {
        switch (mode)
        {
            case ModeDc: PredictDc(s, dst, 16, mbX, mbY); break;
            case ModeTm: PredictTrueMotion(s, dst, 16); break;
            case ModeVe:
                for (int j = 0; j < 16; j++) Array.Copy(s, dst - Bps, s, dst + j * Bps, 16);
                break;
            default: // horizontal
                for (int j = 0; j < 16; j++)
                    for (int i = 0; i < 16; i++) s[dst + j * Bps + i] = s[dst + j * Bps - 1];
                break;
        }
    }

    private static void PredictChroma8(byte[] s, int dst, int mode, int mbX, int mbY)
    {
        switch (mode)
        {
            case ModeDc: PredictDc(s, dst, 8, mbX, mbY); break;
            case ModeTm: PredictTrueMotion(s, dst, 8); break;
            case ModeVe:
                for (int j = 0; j < 8; j++) Array.Copy(s, dst - Bps, s, dst + j * Bps, 8);
                break;
            default:
                for (int j = 0; j < 8; j++)
                    for (int i = 0; i < 8; i++) s[dst + j * Bps + i] = s[dst + j * Bps - 1];
                break;
        }
    }

    private static void PredictDc(byte[] s, int dst, int size, int mbX, int mbY)
    {
        int shift = size == 16 ? 4 : 3; // log2(size)
        int dc;
        if (mbX > 0 && mbY > 0)
        {
            dc = size; // rounding term
            for (int j = 0; j < size; j++) dc += s[dst - 1 + j * Bps] + s[dst - Bps + j];
            dc >>= shift + 1;
        }
        else if (mbY > 0) // no left samples
        {
            dc = size / 2;
            for (int j = 0; j < size; j++) dc += s[dst - Bps + j];
            dc >>= shift;
        }
        else if (mbX > 0) // no top samples
        {
            dc = size / 2;
            for (int j = 0; j < size; j++) dc += s[dst - 1 + j * Bps];
            dc >>= shift;
        }
        else
        {
            dc = 0x80;
        }

        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++) s[dst + j * Bps + i] = (byte)dc;
    }

    private static void PredictTrueMotion(byte[] s, int dst, int size)
    {
        int topLeft = s[dst - Bps - 1];
        for (int j = 0; j < size; j++)
        {
            int left = s[dst + j * Bps - 1];
            for (int i = 0; i < size; i++) s[dst + j * Bps + i] = Clip8(left + s[dst - Bps + i] - topLeft);
        }
    }

    // ── 4x4 sub-block prediction (RFC 6386 section 12.3) ─────────────────────

    private static int Avg3(int a, int b, int c) => (a + 2 * b + c + 2) >> 2;
    private static int Avg2(int a, int b) => (a + b + 1) >> 1;

    private static void PredictSubblock(byte[] s, int d, int mode)
    {
        int top = d - Bps;
        // Row above (A..H, with X the corner) and the column to the left (I..L)
        int X = s[top - 1], A = s[top], B = s[top + 1], C = s[top + 2], D = s[top + 3];
        int E = s[top + 4], F = s[top + 5], G = s[top + 6], H = s[top + 7];
        int I = s[d - 1], J = s[d - 1 + Bps], K = s[d - 1 + 2 * Bps], L = s[d - 1 + 3 * Bps];

        void Put(int x, int y, int v) => s[d + x + y * Bps] = (byte)v;

        switch (mode)
        {
            case 0: // DC
            {
                int dc = 4 + A + B + C + D + I + J + K + L;
                dc >>= 3;
                for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) Put(x, y, dc);
                break;
            }
            case 1: // TM
                for (int y = 0; y < 4; y++)
                {
                    int left = s[d + y * Bps - 1];
                    for (int x = 0; x < 4; x++) Put(x, y, Clip8(left + s[top + x] - X));
                }
                break;
            case 2: // VE (smoothed vertical)
            {
                int v0 = Avg3(X, A, B), v1 = Avg3(A, B, C), v2 = Avg3(B, C, D), v3 = Avg3(C, D, E);
                for (int y = 0; y < 4; y++) { Put(0, y, v0); Put(1, y, v1); Put(2, y, v2); Put(3, y, v3); }
                break;
            }
            case 3: // HE (smoothed horizontal)
            {
                int r0 = Avg3(X, I, J), r1 = Avg3(I, J, K), r2 = Avg3(J, K, L), r3 = Avg3(K, L, L);
                for (int x = 0; x < 4; x++) { Put(x, 0, r0); Put(x, 1, r1); Put(x, 2, r2); Put(x, 3, r3); }
                break;
            }
            case 4: // RD (down-right)
                Put(0, 3, Avg3(J, K, L));
                Put(1, 3, Avg3(I, J, K)); Put(0, 2, Avg3(I, J, K));
                Put(2, 3, Avg3(X, I, J)); Put(1, 2, Avg3(X, I, J)); Put(0, 1, Avg3(X, I, J));
                Put(3, 3, Avg3(A, X, I)); Put(2, 2, Avg3(A, X, I)); Put(1, 1, Avg3(A, X, I)); Put(0, 0, Avg3(A, X, I));
                Put(3, 2, Avg3(B, A, X)); Put(2, 1, Avg3(B, A, X)); Put(1, 0, Avg3(B, A, X));
                Put(3, 1, Avg3(C, B, A)); Put(2, 0, Avg3(C, B, A));
                Put(3, 0, Avg3(D, C, B));
                break;
            case 5: // VR (vertical-right)
                Put(0, 0, Avg2(X, A)); Put(1, 2, Avg2(X, A));
                Put(1, 0, Avg2(A, B)); Put(2, 2, Avg2(A, B));
                Put(2, 0, Avg2(B, C)); Put(3, 2, Avg2(B, C));
                Put(3, 0, Avg2(C, D));
                Put(0, 3, Avg3(K, J, I));
                Put(0, 2, Avg3(J, I, X));
                Put(0, 1, Avg3(I, X, A)); Put(1, 3, Avg3(I, X, A));
                Put(1, 1, Avg3(X, A, B)); Put(2, 3, Avg3(X, A, B));
                Put(2, 1, Avg3(A, B, C)); Put(3, 3, Avg3(A, B, C));
                Put(3, 1, Avg3(B, C, D));
                break;
            case 6: // LD (down-left)
                Put(0, 0, Avg3(A, B, C));
                Put(1, 0, Avg3(B, C, D)); Put(0, 1, Avg3(B, C, D));
                Put(2, 0, Avg3(C, D, E)); Put(1, 1, Avg3(C, D, E)); Put(0, 2, Avg3(C, D, E));
                Put(3, 0, Avg3(D, E, F)); Put(2, 1, Avg3(D, E, F)); Put(1, 2, Avg3(D, E, F)); Put(0, 3, Avg3(D, E, F));
                Put(3, 1, Avg3(E, F, G)); Put(2, 2, Avg3(E, F, G)); Put(1, 3, Avg3(E, F, G));
                Put(3, 2, Avg3(F, G, H)); Put(2, 3, Avg3(F, G, H));
                Put(3, 3, Avg3(G, H, H));
                break;
            case 7: // VL (vertical-left)
                Put(0, 0, Avg2(A, B));
                Put(1, 0, Avg2(B, C)); Put(0, 2, Avg2(B, C));
                Put(2, 0, Avg2(C, D)); Put(1, 2, Avg2(C, D));
                Put(3, 0, Avg2(D, E)); Put(2, 2, Avg2(D, E));
                Put(0, 1, Avg3(A, B, C));
                Put(1, 1, Avg3(B, C, D)); Put(0, 3, Avg3(B, C, D));
                Put(2, 1, Avg3(C, D, E)); Put(1, 3, Avg3(C, D, E));
                Put(3, 1, Avg3(D, E, F)); Put(2, 3, Avg3(D, E, F));
                Put(3, 2, Avg3(E, F, G));
                Put(3, 3, Avg3(F, G, H));
                break;
            case 8: // HD (horizontal-down)
                Put(0, 0, Avg2(I, X)); Put(2, 1, Avg2(I, X));
                Put(0, 1, Avg2(J, I)); Put(2, 2, Avg2(J, I));
                Put(0, 2, Avg2(K, J)); Put(2, 3, Avg2(K, J));
                Put(0, 3, Avg2(L, K));
                Put(3, 0, Avg3(A, B, C));
                Put(2, 0, Avg3(X, A, B));
                Put(1, 0, Avg3(I, X, A)); Put(3, 1, Avg3(I, X, A));
                Put(1, 1, Avg3(J, I, X)); Put(3, 2, Avg3(J, I, X));
                Put(1, 2, Avg3(K, J, I)); Put(3, 3, Avg3(K, J, I));
                Put(1, 3, Avg3(L, K, J));
                break;
            default: // 9: HU (horizontal-up)
                Put(0, 0, Avg2(I, J));
                Put(2, 0, Avg2(J, K)); Put(0, 1, Avg2(J, K));
                Put(2, 1, Avg2(K, L)); Put(0, 2, Avg2(K, L));
                Put(1, 0, Avg3(I, J, K));
                Put(3, 0, Avg3(J, K, L)); Put(1, 1, Avg3(J, K, L));
                Put(3, 1, Avg3(K, L, L)); Put(1, 2, Avg3(K, L, L));
                Put(3, 2, L); Put(2, 2, L); Put(0, 3, L); Put(1, 3, L); Put(2, 3, L); Put(3, 3, L);
                break;
        }
    }
}
