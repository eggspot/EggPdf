using System;

namespace EggPdf.Pdf;

/// <summary>A decoded VP8 key frame: planar YUV 4:2:0, padded to whole 16x16 macroblocks.</summary>
internal sealed class Vp8Frame
{
    public int Width, Height, MbWidth, MbHeight;
    public byte[] Y = Array.Empty<byte>(), U = Array.Empty<byte>(), V = Array.Empty<byte>();
    public int YStride, UvStride;
}

/// <summary>
/// VP8 key-frame decoder (RFC 6386), the intra-only subset that WebP lossy images use: frame and
/// segment headers, per-macroblock intra modes, token partitions with dequantization, the inverse
/// DCT / Walsh-Hadamard transforms, intra prediction and the in-loop deblocking filter. Intra
/// prediction reads unfiltered neighbours, so the whole frame is reconstructed first and the
/// filter runs over it afterwards, which is equivalent to the reference decoder's delayed row filter.
/// Split across Vp8Decoder.Prediction.cs, Vp8Decoder.LoopFilter.cs and <see cref="Vp8Yuv"/>.
/// </summary>
internal sealed partial class Vp8Decoder
{
    private const int NumSegments = 4;
    private const int Bps = 32;                          // stride of the per-macroblock scratch buffer
    private const int YOffset = Bps + 8;                 // scratch layout: 1 border row above luma, 8 border columns to the left
    private const int UOffset = YOffset + Bps * 16 + Bps;
    private const int VOffset = UOffset + 16;
    private const int ScratchSize = Bps * 26;

    // Sub-block / 16x16 prediction modes (B_* numbering; DC/TM/V/H map to the first four)
    private const int ModeDc = 0, ModeTm = 1, ModeVe = 2, ModeHe = 3;

    private readonly byte[] _data;
    private readonly int _offset, _length;

    private int _width, _height, _mbW, _mbH;

    // Segment header
    private bool _useSegment, _updateSegmentMap, _absoluteDelta = true;
    private readonly int[] _segQuant = new int[NumSegments], _segFilter = new int[NumSegments];
    private readonly int[] _segProba = { 255, 255, 255 };

    // Filter header
    private bool _simpleFilter, _useLfDelta;
    private int _filterLevel, _sharpness, _filterType;
    private readonly int[] _refLfDelta = new int[4], _modeLfDelta = new int[4];

    // Quantizers per segment: [DC, AC] for luma, Y2 and chroma
    private readonly int[][] _y1Mat = new int[NumSegments][], _y2Mat = new int[NumSegments][], _uvMat = new int[NumSegments][];

    private byte[] _probas = Array.Empty<byte>();        // [type][band][ctx][11], possibly updated by the frame header
    private bool _useSkipProba;
    private int _skipProba;

    private Vp8BoolDecoder _modeReader = null!;
    private Vp8BoolDecoder[] _tokenReaders = Array.Empty<Vp8BoolDecoder>();

    // Per-frame contexts
    private byte[] _intraTop = Array.Empty<byte>();
    private readonly byte[] _intraLeft = new byte[4];
    private byte[] _nzTopY = Array.Empty<byte>(), _nzTopU = Array.Empty<byte>(), _nzTopV = Array.Empty<byte>(), _nzTopDc = Array.Empty<byte>();
    private readonly byte[] _nzLeftY = new byte[4], _nzLeftU = new byte[2], _nzLeftV = new byte[2];
    private byte _nzLeftDc;

    // Current macroblock
    private int _segment;
    private bool _skip, _isI4x4;
    private readonly byte[] _modes = new byte[16];
    private int _uvMode;
    private readonly short[] _coeffs = new short[384];
    private readonly bool[] _blockHasCoeffs = new bool[24];

    // Per-macroblock filter parameters (frame-wide, applied after reconstruction)
    private int[] _filterLimit = Array.Empty<int>(), _filterInterior = Array.Empty<int>(), _filterHev = Array.Empty<int>();
    private bool[] _filterInner = Array.Empty<bool>();

    private Vp8Frame _frame = null!;

    private Vp8Decoder(byte[] data, int offset, int length)
    {
        _data = data; _offset = offset; _length = length;
    }

    /// <summary>Decode the key frame in <paramref name="data"/>[offset..offset+length); null when it is malformed or unsupported.</summary>
    public static Vp8Frame? Decode(byte[] data, int offset, int length)
    {
        try { return new Vp8Decoder(data, offset, length).Run(); }
        catch (Exception) { return null; } // infallible by convention: a corrupt stream is "no image"
    }

    private Vp8Frame? Run()
    {
        if (!ParseHeaders()) return null;

        _frame = new Vp8Frame
        {
            Width = _width, Height = _height, MbWidth = _mbW, MbHeight = _mbH,
            YStride = _mbW * 16, UvStride = _mbW * 8,
        };
        _frame.Y = new byte[_frame.YStride * _mbH * 16];
        _frame.U = new byte[_frame.UvStride * _mbH * 8];
        _frame.V = new byte[_frame.UvStride * _mbH * 8];

        _intraTop = new byte[4 * _mbW];
        _nzTopY = new byte[4 * _mbW]; _nzTopU = new byte[2 * _mbW]; _nzTopV = new byte[2 * _mbW]; _nzTopDc = new byte[_mbW];
        _filterLimit = new int[_mbW * _mbH]; _filterInterior = new int[_mbW * _mbH]; _filterHev = new int[_mbW * _mbH];
        _filterInner = new bool[_mbW * _mbH];

        var scratch = new byte[ScratchSize];
        for (int mbY = 0; mbY < _mbH; mbY++)
        {
            var tokens = _tokenReaders[mbY & (_tokenReaders.Length - 1)];
            Array.Clear(_nzLeftY, 0, 4); Array.Clear(_nzLeftU, 0, 2); Array.Clear(_nzLeftV, 0, 2);
            _nzLeftDc = 0;
            Array.Clear(_intraLeft, 0, 4);
            InitScratchBorders(scratch, mbY);

            for (int mbX = 0; mbX < _mbW; mbX++)
            {
                ParseIntraMode(mbX);
                bool hasCoeffs = _useSkipProba && _skip ? SkipResiduals(mbX) : ParseResiduals(tokens, mbX);
                RecordFilterInfo(mbX, mbY, hasCoeffs);
                ReconstructMacroblock(scratch, mbX, mbY);
            }
        }

        if (_filterType > 0) ApplyLoopFilter();
        return _frame;
    }

    // ── Headers (RFC 6386 sections 9 and 19.2) ───────────────────────────────

    private bool ParseHeaders()
    {
        if (_length < 10) return false;
        int p = _offset;
        uint tag = (uint)(_data[p] | (_data[p + 1] << 8) | (_data[p + 2] << 16));
        bool keyFrame = (tag & 1) == 0;
        int profile = (int)((tag >> 1) & 7);
        bool show = ((tag >> 4) & 1) != 0;
        int firstPartSize = (int)(tag >> 5);
        if (!keyFrame || profile > 3 || !show) return false;
        if (_data[p + 3] != 0x9D || _data[p + 4] != 0x01 || _data[p + 5] != 0x2A) return false;

        _width = ((_data[p + 7] << 8) | _data[p + 6]) & 0x3FFF;
        _height = ((_data[p + 9] << 8) | _data[p + 8]) & 0x3FFF;
        if (_width == 0 || _height == 0) return false;
        _mbW = (_width + 15) >> 4;
        _mbH = (_height + 15) >> 4;

        int partStart = p + 10;
        int available = _offset + _length - partStart;
        if (firstPartSize > available) return false;

        var br = _modeReader = new Vp8BoolDecoder(_data, partStart, firstPartSize);
        br.ReadFlag(); // color space
        br.ReadFlag(); // clamping type: output is always clamped

        ParseSegmentHeader(br);
        ParseFilterHeader(br);

        int partitions = 1 << br.ReadLiteral(2);
        if (!SetupTokenPartitions(partStart + firstPartSize, available - firstPartSize, partitions)) return false;

        ParseQuantizers(br);
        br.ReadFlag(); // refresh_entropy_probs: irrelevant for a single frame
        ParseCoefficientProbabilities(br);

        _useSkipProba = br.ReadFlag() != 0;
        if (_useSkipProba) _skipProba = br.ReadLiteral(8);
        return true;
    }

    private void ParseSegmentHeader(Vp8BoolDecoder br)
    {
        _useSegment = br.ReadFlag() != 0;
        if (!_useSegment) return;

        _updateSegmentMap = br.ReadFlag() != 0;
        if (br.ReadFlag() != 0) // update segment feature data
        {
            _absoluteDelta = br.ReadFlag() != 0;
            for (int s = 0; s < NumSegments; s++) _segQuant[s] = br.ReadOptionalSigned(7);
            for (int s = 0; s < NumSegments; s++) _segFilter[s] = br.ReadOptionalSigned(6);
        }
        if (_updateSegmentMap)
            for (int s = 0; s < 3; s++) _segProba[s] = br.ReadFlag() != 0 ? br.ReadLiteral(8) : 255;
    }

    private void ParseFilterHeader(Vp8BoolDecoder br)
    {
        _simpleFilter = br.ReadFlag() != 0;
        _filterLevel = br.ReadLiteral(6);
        _sharpness = br.ReadLiteral(3);
        _useLfDelta = br.ReadFlag() != 0;
        if (_useLfDelta && br.ReadFlag() != 0) // update deltas
        {
            for (int i = 0; i < 4; i++) if (br.ReadFlag() != 0) _refLfDelta[i] = br.ReadSigned(6);
            for (int i = 0; i < 4; i++) if (br.ReadFlag() != 0) _modeLfDelta[i] = br.ReadSigned(6);
        }
        _filterType = _filterLevel == 0 ? 0 : _simpleFilter ? 1 : 2;
    }

    private bool SetupTokenPartitions(int start, int size, int count)
    {
        if (size < 3 * (count - 1)) return false;
        _tokenReaders = new Vp8BoolDecoder[count];
        int sizesAt = start;
        int partStart = start + 3 * (count - 1);
        int left = size - 3 * (count - 1);
        for (int i = 0; i < count - 1; i++)
        {
            int psize = _data[sizesAt] | (_data[sizesAt + 1] << 8) | (_data[sizesAt + 2] << 16);
            if (psize > left) psize = left;
            _tokenReaders[i] = new Vp8BoolDecoder(_data, partStart, psize);
            partStart += psize;
            left -= psize;
            sizesAt += 3;
        }
        _tokenReaders[count - 1] = new Vp8BoolDecoder(_data, partStart, left);
        return partStart < _offset + _length;
    }

    private void ParseQuantizers(Vp8BoolDecoder br)
    {
        int baseQ = br.ReadLiteral(7);
        int y1Dc = br.ReadOptionalSigned(4);
        int y2Dc = br.ReadOptionalSigned(4);
        int y2Ac = br.ReadOptionalSigned(4);
        int uvDc = br.ReadOptionalSigned(4);
        int uvAc = br.ReadOptionalSigned(4);

        for (int s = 0; s < NumSegments; s++)
        {
            int q = baseQ;
            if (_useSegment)
            {
                q = _segQuant[s];
                if (!_absoluteDelta) q += baseQ;
            }
            _y1Mat[s] = new[] { Vp8Tables.DcTable[Clip(q + y1Dc, 127)], Vp8Tables.AcTable[Clip(q, 127)] };
            _y2Mat[s] = new[]
            {
                Vp8Tables.DcTable[Clip(q + y2Dc, 127)] * 2,
                Math.Max(8, (Vp8Tables.AcTable[Clip(q + y2Ac, 127)] * 101581) >> 16), // = x * 155 / 100
            };
            _uvMat[s] = new[] { Vp8Tables.DcTable[Clip(q + uvDc, 117)], Vp8Tables.AcTable[Clip(q + uvAc, 127)] };
        }
    }

    private static int Clip(int v, int max) => v < 0 ? 0 : v > max ? max : v;

    private void ParseCoefficientProbabilities(Vp8BoolDecoder br)
    {
        _probas = new byte[Vp8Tables.CoeffsProba0.Length];
        for (int i = 0; i < _probas.Length; i++)
            _probas[i] = br.ReadBool(Vp8Tables.CoeffsUpdateProba[i]) != 0 ? (byte)br.ReadLiteral(8) : Vp8Tables.CoeffsProba0[i];
    }

    // ── Per-macroblock modes (RFC 6386 section 11) ───────────────────────────

    private void ParseIntraMode(int mbX)
    {
        var br = _modeReader;
        if (_updateSegmentMap)
        {
            _segment = br.ReadBool(_segProba[0]) == 0
                ? br.ReadBool(_segProba[1])
                : br.ReadBool(_segProba[2]) + 2;
        }
        else
        {
            _segment = 0;
        }
        _skip = _useSkipProba && br.ReadBool(_skipProba) != 0;

        _isI4x4 = br.ReadBool(145) == 0;
        int topBase = 4 * mbX;
        if (!_isI4x4)
        {
            int ymode = br.ReadBool(156) != 0
                ? (br.ReadBool(128) != 0 ? ModeTm : ModeHe)
                : (br.ReadBool(163) != 0 ? ModeVe : ModeDc);
            _modes[0] = (byte)ymode;
            for (int i = 0; i < 4; i++) { _intraTop[topBase + i] = (byte)ymode; _intraLeft[i] = (byte)ymode; }
        }
        else
        {
            int m = 0;
            for (int y = 0; y < 4; y++)
            {
                int ymode = _intraLeft[y];
                for (int x = 0; x < 4; x++)
                {
                    int probBase = (_intraTop[topBase + x] * 10 + ymode) * 9;
                    ymode = ReadSubblockMode(br, probBase);
                    _intraTop[topBase + x] = (byte)ymode;
                    _modes[m++] = (byte)ymode;
                }
                _intraLeft[y] = (byte)ymode;
            }
        }

        _uvMode = br.ReadBool(142) == 0 ? ModeDc
            : br.ReadBool(114) == 0 ? ModeVe
            : br.ReadBool(183) != 0 ? ModeTm : ModeHe;
    }

    /// <summary>The sub-block mode tree of RFC 6386 section 11.2 with key-frame contextual probabilities.</summary>
    private static int ReadSubblockMode(Vp8BoolDecoder br, int p)
    {
        var prob = Vp8Tables.BModesProba;
        if (br.ReadBool(prob[p]) == 0) return 0;      // B_DC_PRED
        if (br.ReadBool(prob[p + 1]) == 0) return 1;  // B_TM_PRED
        if (br.ReadBool(prob[p + 2]) == 0) return 2;  // B_VE_PRED
        if (br.ReadBool(prob[p + 3]) == 0)
        {
            if (br.ReadBool(prob[p + 4]) == 0) return 3; // B_HE_PRED
            return br.ReadBool(prob[p + 5]) == 0 ? 4 : 5; // B_RD_PRED : B_VR_PRED
        }
        if (br.ReadBool(prob[p + 6]) == 0) return 6;      // B_LD_PRED
        if (br.ReadBool(prob[p + 7]) == 0) return 7;      // B_VL_PRED
        return br.ReadBool(prob[p + 8]) == 0 ? 8 : 9;     // B_HD_PRED : B_HU_PRED
    }

    // ── Residual tokens (RFC 6386 section 13) ────────────────────────────────

    private bool SkipResiduals(int mbX)
    {
        Array.Clear(_coeffs, 0, _coeffs.Length);
        Array.Clear(_blockHasCoeffs, 0, _blockHasCoeffs.Length);
        Array.Clear(_nzLeftY, 0, 4); Array.Clear(_nzLeftU, 0, 2); Array.Clear(_nzLeftV, 0, 2);
        Array.Clear(_nzTopY, 4 * mbX, 4); Array.Clear(_nzTopU, 2 * mbX, 2); Array.Clear(_nzTopV, 2 * mbX, 2);
        if (!_isI4x4)
        {
            _nzLeftDc = 0;
            _nzTopDc[mbX] = 0;
        }
        return false;
    }

    /// <summary>Parse all coefficient blocks of the macroblock into <see cref="_coeffs"/>; returns whether any is non-zero.</summary>
    private bool ParseResiduals(Vp8BoolDecoder br, int mbX)
    {
        Array.Clear(_coeffs, 0, _coeffs.Length);
        Array.Clear(_blockHasCoeffs, 0, _blockHasCoeffs.Length);
        var y1 = _y1Mat[_segment]; var y2 = _y2Mat[_segment]; var uv = _uvMat[_segment];
        int first, lumaType;
        bool any = false;

        if (!_isI4x4)
        {
            // The Y2 block carries the 16 luma DC coefficients through a Walsh-Hadamard transform
            var dc = new short[16];
            int ctx = _nzTopDc[mbX] + _nzLeftDc;
            int nz = ReadCoefficients(br, 1, ctx, y2, 0, dc, 0);
            _nzTopDc[mbX] = _nzLeftDc = (byte)(nz > 0 ? 1 : 0);
            if (nz > 1) InverseWalshHadamard(dc, _coeffs);
            else
            {
                int dc0 = (dc[0] + 3) >> 3;
                for (int i = 0; i < 256; i += 16) _coeffs[i] = (short)dc0;
            }
            first = 1;
            lumaType = 0;
        }
        else
        {
            first = 0;
            lumaType = 3;
        }

        for (int y = 0; y < 4; y++)
        {
            int left = _nzLeftY[y];
            for (int x = 0; x < 4; x++)
            {
                int block = y * 4 + x;
                int ctx = left + _nzTopY[4 * mbX + x];
                int nz = ReadCoefficients(br, lumaType, ctx, y1, first, _coeffs, block * 16);
                left = nz > first ? 1 : 0;
                _nzTopY[4 * mbX + x] = (byte)left;
                bool has = nz > 1 || _coeffs[block * 16] != 0;
                _blockHasCoeffs[block] = has;
                any |= has;
            }
            _nzLeftY[y] = (byte)left;
        }

        for (int plane = 0; plane < 2; plane++)
        {
            var nzTop = plane == 0 ? _nzTopU : _nzTopV;
            var nzLeft = plane == 0 ? _nzLeftU : _nzLeftV;
            for (int y = 0; y < 2; y++)
            {
                int left = nzLeft[y];
                for (int x = 0; x < 2; x++)
                {
                    int block = 16 + plane * 4 + y * 2 + x;
                    int ctx = left + nzTop[2 * mbX + x];
                    int nz = ReadCoefficients(br, 2, ctx, uv, 0, _coeffs, block * 16);
                    left = nz > 0 ? 1 : 0;
                    nzTop[2 * mbX + x] = (byte)left;
                    bool has = nz > 1 || _coeffs[block * 16] != 0;
                    _blockHasCoeffs[block] = has;
                    any |= has;
                }
                nzLeft[y] = (byte)left;
            }
        }
        return any;
    }

    private int ProbaIndex(int type, int band, int ctx) => ((type * 8 + band) * 3 + ctx) * 11;

    /// <summary>
    /// Decode one block's tokens starting at coefficient <paramref name="n"/>, dequantize into
    /// <paramref name="outp"/> (raster order), and return the position after the last non-zero coefficient.
    /// </summary>
    private int ReadCoefficients(Vp8BoolDecoder br, int type, int ctx, int[] dq, int n, short[] outp, int outOffset)
    {
        var probas = _probas;
        int p = ProbaIndex(type, Vp8Tables.Bands[n], ctx);
        for (; n < 16; n++)
        {
            if (br.ReadBool(probas[p]) == 0) return n; // end of block
            while (br.ReadBool(probas[p + 1]) == 0)    // run of zero coefficients
            {
                n++;
                if (n == 16) return 16;
                p = ProbaIndex(type, Vp8Tables.Bands[n], 0);
            }

            int nextBand = Vp8Tables.Bands[n + 1];
            int v;
            if (br.ReadBool(probas[p + 2]) == 0)
            {
                v = 1;
                p = ProbaIndex(type, nextBand, 1);
            }
            else
            {
                v = ReadLargeValue(br, p);
                p = ProbaIndex(type, nextBand, 2);
            }
            int signed = br.ReadBool(128) != 0 ? -v : v;
            outp[outOffset + Vp8Tables.Zigzag[n]] = (short)(signed * dq[n > 0 ? 1 : 0]);
        }
        return 16;
    }

    private static readonly byte[][] LargeValueCategories =
    {
        new byte[] { 173, 148, 140 },
        new byte[] { 176, 155, 140, 135 },
        new byte[] { 180, 157, 141, 134, 130 },
        new byte[] { 254, 254, 243, 230, 196, 177, 153, 140, 133, 130, 129 },
    };

    /// <summary>Magnitudes of 2 and above: small literals, then the DCT_CAT1-6 extra-bit categories.</summary>
    private int ReadLargeValue(Vp8BoolDecoder br, int p)
    {
        var probas = _probas;
        if (br.ReadBool(probas[p + 3]) == 0)
        {
            if (br.ReadBool(probas[p + 4]) == 0) return 2;
            return 3 + br.ReadBool(probas[p + 5]);
        }

        if (br.ReadBool(probas[p + 6]) == 0)
        {
            if (br.ReadBool(probas[p + 7]) == 0) return 5 + br.ReadBool(159);
            int v = 7 + 2 * br.ReadBool(165);
            return v + br.ReadBool(145);
        }

        int bit1 = br.ReadBool(probas[p + 8]);
        int bit0 = br.ReadBool(probas[p + 9 + bit1]);
        int cat = 2 * bit1 + bit0;
        int value = 0;
        foreach (byte prob in LargeValueCategories[cat]) value += value + br.ReadBool(prob);
        return value + 3 + (8 << cat);
    }

    private void RecordFilterInfo(int mbX, int mbY, bool hasCoeffs)
    {
        if (_filterType == 0) return;

        int baseLevel = _filterLevel;
        if (_useSegment)
        {
            baseLevel = _segFilter[_segment];
            if (!_absoluteDelta) baseLevel += _filterLevel;
        }
        int level = baseLevel;
        if (_useLfDelta)
        {
            level += _refLfDelta[0];
            if (_isI4x4) level += _modeLfDelta[0];
        }
        level = level < 0 ? 0 : level > 63 ? 63 : level;

        int idx = mbY * _mbW + mbX;
        if (level > 0)
        {
            int interior = level;
            if (_sharpness > 0)
            {
                interior >>= _sharpness > 4 ? 2 : 1;
                if (interior > 9 - _sharpness) interior = 9 - _sharpness;
            }
            if (interior < 1) interior = 1;
            _filterInterior[idx] = interior;
            _filterLimit[idx] = 2 * level + interior;
            _filterHev[idx] = level >= 40 ? 2 : level >= 15 ? 1 : 0;
        }
        else
        {
            _filterLimit[idx] = 0;
        }
        _filterInner[idx] = _isI4x4 || hasCoeffs;
    }
}
