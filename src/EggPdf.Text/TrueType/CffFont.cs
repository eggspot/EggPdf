using System;
using System.Collections.Generic;

namespace EggPdf.Text.TrueType;

/// <summary>One point of a converted outline in font units; off-curve points are quadratic control points.</summary>
internal readonly struct OutlinePoint
{
    public readonly float X, Y;
    public readonly bool OnCurve;
    public OutlinePoint(float x, float y, bool onCurve) { X = x; Y = y; OnCurve = onCurve; }
}

/// <summary>
/// A CFF or CFF2 table (the outlines of OpenType fonts with PostScript flavour): INDEX/DICT parsing, subroutines,
/// and a Type 2 charstring interpreter that yields flattened-to-quadratic contours. CFF2 variation (the
/// <c>blend</c> and <c>vsindex</c> operators over an ItemVariationStore) is evaluated at a normalized location.
/// Hinting operators are parsed and ignored.
/// </summary>
internal sealed class CffFont
{
    private readonly byte[] _d;
    private readonly int _cff;
    private readonly bool _isCff2;
    private List<(int offset, int length)> _charStrings = new List<(int, int)>();
    private List<(int offset, int length)> _globalSubrs = new List<(int, int)>();
    private readonly List<FontDict> _fontDicts = new List<FontDict>();
    private byte[]? _fdSelect;                       // glyph -> font dict (null: everything uses dict 0)
    private ItemVariationStore? _store;

    private sealed class FontDict
    {
        public List<(int offset, int length)> LocalSubrs = new List<(int, int)>();
        public int VsIndex;
    }

    public int GlyphCount => _charStrings.Count;

    private CffFont(byte[] data, int cffOffset, bool isCff2)
    {
        _d = data; _cff = cffOffset; _isCff2 = isCff2;
    }

    /// <summary>Parse the CFF (or CFF2) table at <paramref name="offset"/>; null when it is malformed.</summary>
    public static CffFont? TryParse(byte[] data, int offset, bool isCff2, float[]? coords)
    {
        try
        {
            var font = new CffFont(data, offset, isCff2);
            font.Parse(coords ?? Array.Empty<float>());
            return font;
        }
        catch (Exception)
        {
            return null; // infallible by convention
        }
    }

    // ── Structure ────────────────────────────────────────────────────────────

    private int U16(int o) => (_d[o] << 8) | _d[o + 1];
    private uint U32(int o) => (uint)((_d[o] << 24) | (_d[o + 1] << 16) | (_d[o + 2] << 8) | _d[o + 3]);

    private void Parse(float[] coords)
    {
        int headerSize = _d[_cff + 2];
        Dictionary<int, List<double>> top;
        int pos;
        if (_isCff2)
        {
            int topLength = U16(_cff + 3);
            top = ParseDict(_cff + headerSize, topLength);
            pos = _cff + headerSize + topLength;
        }
        else
        {
            pos = _cff + headerSize;
            pos = SkipIndex(pos);                              // Name INDEX
            var topDicts = ReadIndex(pos, out pos);
            top = ParseDict(topDicts[0].offset, topDicts[0].length);
            pos = SkipIndex(pos);                              // String INDEX
        }
        _globalSubrs = ReadIndex(pos, out _);

        _charStrings = ReadIndex(_cff + (int)Operand(top, 17), out _);

        if (top.ContainsKey(1236)) // FDArray (CID-keyed CFF, and always in CFF2)
        {
            var fds = ReadIndex(_cff + (int)Operand(top, 1236), out _);
            foreach (var fd in fds) _fontDicts.Add(ReadFontDict(ParseDict(fd.offset, fd.length)));
            if (top.ContainsKey(1237)) _fdSelect = ReadFdSelect(_cff + (int)Operand(top, 1237));
        }
        else
        {
            _fontDicts.Add(ReadFontDict(top));
        }

        if (_isCff2 && top.TryGetValue(24, out var vs) && vs.Count > 0)
            _store = new ItemVariationStore(_d, _cff + (int)vs[0] + 2, coords); // the store is preceded by a 2-byte length
    }

    private FontDict ReadFontDict(Dictionary<int, List<double>> dict)
    {
        var result = new FontDict();
        if (!dict.TryGetValue(18, out var priv) || priv.Count < 2) return result;

        int size = (int)priv[0], at = _cff + (int)priv[1];
        var privateDict = ParseDict(at, size);
        if (privateDict.TryGetValue(19, out var subrs) && subrs.Count > 0) result.LocalSubrs = ReadIndex(at + (int)subrs[0], out _);
        if (privateDict.TryGetValue(22, out var vsindex) && vsindex.Count > 0) result.VsIndex = (int)vsindex[0];
        return result;
    }

    private byte[] ReadFdSelect(int at)
    {
        var map = new byte[_charStrings.Count];
        int format = _d[at];
        if (format == 0)
        {
            for (int g = 0; g < map.Length; g++) map[g] = _d[at + 1 + g];
        }
        else if (format == 3 || format == 4)
        {
            bool wide = format == 4;
            int ranges = wide ? (int)U32(at + 1) : U16(at + 1);
            int p = at + (wide ? 5 : 3);
            for (int r = 0; r < ranges; r++)
            {
                int first = wide ? (int)U32(p) : U16(p);
                int fd = wide ? U16(p + 4) : _d[p + 2];
                int next = wide ? (int)U32(p + 6) : U16(p + 3);
                for (int g = first; g < next && g < map.Length; g++) map[g] = (byte)fd;
                p += wide ? 6 : 3;
            }
        }
        return map;
    }

    private static double Operand(Dictionary<int, List<double>> dict, int op)
        => dict.TryGetValue(op, out var v) && v.Count > 0 ? v[v.Count - 1] : 0;

    private int SkipIndex(int pos)
    {
        ReadIndex(pos, out int next);
        return next;
    }

    /// <summary>An INDEX: count, offSize, offsets, data. Returns absolute (offset, length) of each item.</summary>
    private List<(int offset, int length)> ReadIndex(int pos, out int next)
    {
        int count = _isCff2 ? (int)U32(pos) : U16(pos);
        pos += _isCff2 ? 4 : 2;
        var items = new List<(int, int)>(count);
        if (count == 0) { next = pos; return items; }

        int offSize = _d[pos++];
        int offsetsAt = pos;
        int dataStart = offsetsAt + (count + 1) * offSize - 1;
        int Off(int i)
        {
            int v = 0;
            for (int k = 0; k < offSize; k++) v = (v << 8) | _d[offsetsAt + i * offSize + k];
            return v;
        }
        for (int i = 0; i < count; i++) items.Add((dataStart + Off(i), Off(i + 1) - Off(i)));
        next = dataStart + Off(count);
        return items;
    }

    /// <summary>DICT data to operator -> operands (two-byte operators 12 xx are keyed 1200 + xx).</summary>
    private Dictionary<int, List<double>> ParseDict(int offset, int length)
    {
        var dict = new Dictionary<int, List<double>>();
        var operands = new List<double>();
        int p = offset, end = offset + length;
        while (p < end)
        {
            int b = _d[p++];
            if (b <= 27) // 22 (vsindex) and 23 (blend) are CFF2 operators
            {
                int op = b == 12 ? 1200 + _d[p++] : b;
                dict[op] = operands;
                operands = new List<double>();
            }
            else if (b == 28) { operands.Add((short)((_d[p] << 8) | _d[p + 1])); p += 2; }
            else if (b == 29) { operands.Add((int)U32(p)); p += 4; }
            else if (b == 30) operands.Add(ReadReal(ref p));
            else if (b >= 32 && b <= 246) operands.Add(b - 139);
            else if (b >= 247 && b <= 250) operands.Add((b - 247) * 256 + _d[p++] + 108);
            else if (b >= 251 && b <= 254) operands.Add(-(b - 251) * 256 - _d[p++] - 108);
        }
        return dict;
    }

    private double ReadReal(ref int p)
    {
        var text = new System.Text.StringBuilder();
        while (true)
        {
            int b = _d[p++];
            foreach (int nibble in new[] { b >> 4, b & 15 })
            {
                if (nibble <= 9) text.Append((char)('0' + nibble));
                else if (nibble == 10) text.Append('.');
                else if (nibble == 11) text.Append('E');
                else if (nibble == 12) text.Append("E-");
                else if (nibble == 14) text.Append('-');
                else if (nibble == 15) return double.TryParse(text.ToString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
            }
        }
    }

    // ── Type 2 charstrings ───────────────────────────────────────────────────

    private struct Segment
    {
        public bool IsCurve;
        public float X1, Y1, X2, Y2, X3, Y3;   // line: (X3, Y3) is the end point
    }

    /// <summary>All contours of a glyph, converted to TrueType-style quadratic outlines.</summary>
    public List<List<OutlinePoint>> GetContours(int gid, float tolerance)
    {
        var contours = new List<List<OutlinePoint>>();
        if (gid < 0 || gid >= _charStrings.Count) return contours;

        var interpreter = new Interpreter(this, gid);
        interpreter.Run();
        foreach (var path in interpreter.Paths)
        {
            var points = Quadratic.Convert(path.start, path.segments, tolerance);
            if (points.Count >= 3) contours.Add(points);
        }
        return contours;
    }

    private static int Bias(int count) => count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

    private sealed class Interpreter
    {
        private readonly CffFont _f;
        private readonly int _gid;
        private readonly FontDict _fd;
        private readonly float[] _stack = new float[520];
        private int _sp;
        private float _x, _y;
        private int _stems;
        private bool _haveWidth;
        private bool _ended;
        private float[] _scalars = Array.Empty<float>();
        private (float x, float y) _start;
        private List<Segment>? _current;

        public readonly List<((float x, float y) start, List<Segment> segments)> Paths = new List<((float, float), List<Segment>)>();

        public Interpreter(CffFont font, int gid)
        {
            _f = font; _gid = gid;
            int fdIndex = font._fdSelect != null ? font._fdSelect[gid] : 0;
            _fd = font._fontDicts[Math.Min(fdIndex, font._fontDicts.Count - 1)];
            if (font._store != null) _scalars = font._store.Scalars(_fd.VsIndex);
        }

        public void Run()
        {
            var cs = _f._charStrings[_gid];
            Execute(cs.offset, cs.length, 0);
            ClosePath();
        }

        private void ClosePath()
        {
            if (_current != null && _current.Count > 0) Paths.Add((_start, _current));
            _current = null;
        }

        private void MoveTo(float dx, float dy)
        {
            ClosePath();
            _x += dx; _y += dy;
            _start = (_x, _y);
            _current = new List<Segment>();
        }

        private void LineTo(float dx, float dy)
        {
            _current ??= StartImplicit();
            _x += dx; _y += dy;
            _current.Add(new Segment { IsCurve = false, X3 = _x, Y3 = _y });
        }

        private void CurveTo(float dx1, float dy1, float dx2, float dy2, float dx3, float dy3)
        {
            _current ??= StartImplicit();
            float x1 = _x + dx1, y1 = _y + dy1, x2 = x1 + dx2, y2 = y1 + dy2;
            _x = x2 + dx3; _y = y2 + dy3;
            _current.Add(new Segment { IsCurve = true, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, X3 = _x, Y3 = _y });
        }

        private List<Segment> StartImplicit()
        {
            _start = (_x, _y);
            return new List<Segment>();
        }

        private void Push(float v) { if (_sp < _stack.Length) _stack[_sp++] = v; }

        /// <summary>CFF1: the first stack-clearing operator may carry the glyph width as an extra leading argument.</summary>
        private void TakeWidth(bool present)
        {
            if (_haveWidth || _f._isCff2) return;
            _haveWidth = true;
            if (present && _sp > 0)
            {
                for (int i = 1; i < _sp; i++) _stack[i - 1] = _stack[i];
                _sp--;
            }
        }

        private void Execute(int offset, int length, int depth)
        {
            if (depth > 10) return;
            var d = _f._d;
            int p = offset, end = offset + length;
            while (p < end && !_ended)
            {
                int b = d[p++];
                if (b >= 32 || b == 28)
                {
                    if (b == 28) { Push((short)((d[p] << 8) | d[p + 1])); p += 2; }
                    else if (b <= 246) Push(b - 139);
                    else if (b <= 250) Push((b - 247) * 256 + d[p++] + 108);
                    else if (b <= 254) Push(-(b - 251) * 256 - d[p++] - 108);
                    else { Push(((d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]) / 65536f); p += 4; }
                    continue;
                }

                switch (b)
                {
                    case 1: case 3: case 18: case 23: // hstem vstem hstemhm vstemhm
                        TakeWidth((_sp & 1) == 1);
                        _stems += _sp / 2; _sp = 0;
                        break;
                    case 19: case 20: // hintmask cntrmask: pending stem args, then the mask bytes
                        TakeWidth((_sp & 1) == 1);
                        _stems += _sp / 2; _sp = 0;
                        p += (_stems + 7) / 8;
                        break;
                    case 21: TakeWidth(_sp > 2); MoveTo(_stack[_sp - 2], _stack[_sp - 1]); _sp = 0; break;
                    case 22: TakeWidth(_sp > 1); MoveTo(_stack[_sp - 1], 0); _sp = 0; break;
                    case 4: TakeWidth(_sp > 1); MoveTo(0, _stack[_sp - 1]); _sp = 0; break;
                    case 5: for (int i = 0; i + 1 < _sp; i += 2) LineTo(_stack[i], _stack[i + 1]); _sp = 0; break;
                    case 6: case 7: // hlineto vlineto: alternate directions
                    {
                        bool horizontal = b == 6;
                        for (int i = 0; i < _sp; i++, horizontal = !horizontal)
                            if (horizontal) LineTo(_stack[i], 0); else LineTo(0, _stack[i]);
                        _sp = 0;
                        break;
                    }
                    case 8: for (int i = 0; i + 5 < _sp; i += 6) CurveTo(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]); _sp = 0; break;
                    case 24: // rcurveline
                    {
                        int i = 0;
                        for (; i + 7 < _sp; i += 6) CurveTo(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                        if (i + 1 < _sp) LineTo(_stack[i], _stack[i + 1]);
                        _sp = 0;
                        break;
                    }
                    case 25: // rlinecurve
                    {
                        int i = 0;
                        for (; i + 7 < _sp; i += 2) LineTo(_stack[i], _stack[i + 1]);
                        if (i + 5 < _sp) CurveTo(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                        _sp = 0;
                        break;
                    }
                    case 26: // vvcurveto
                    {
                        int i = 0; float dx1 = 0;
                        if ((_sp & 1) == 1) dx1 = _stack[i++];
                        for (; i + 3 < _sp; i += 4) { CurveTo(dx1, _stack[i], _stack[i + 1], _stack[i + 2], 0, _stack[i + 3]); dx1 = 0; }
                        _sp = 0;
                        break;
                    }
                    case 27: // hhcurveto
                    {
                        int i = 0; float dy1 = 0;
                        if ((_sp & 1) == 1) dy1 = _stack[i++];
                        for (; i + 3 < _sp; i += 4) { CurveTo(_stack[i], dy1, _stack[i + 1], _stack[i + 2], _stack[i + 3], 0); dy1 = 0; }
                        _sp = 0;
                        break;
                    }
                    case 30: case 31: HvCurves(b == 31); _sp = 0; break;
                    case 10: case 29: // callsubr callgsubr
                    {
                        var subrs = b == 10 ? _fd.LocalSubrs : _f._globalSubrs;
                        int index = (int)_stack[--_sp] + Bias(subrs.Count);
                        if (index >= 0 && index < subrs.Count) Execute(subrs[index].offset, subrs[index].length, depth + 1);
                        break;
                    }
                    case 11: return;
                    case 14: // endchar (CFF1); an optional width precedes it
                        TakeWidth(_sp == 1 || _sp == 5);
                        _ended = true;
                        break;
                    case 15: _scalars = _f._store != null ? _f._store.Scalars((int)_stack[--_sp]) : _scalars; _sp = 0; break; // vsindex
                    case 16: Blend(); break;
                    case 12: Escape(d[p++]); break;
                    default: _sp = 0; break;
                }
            }
        }

        /// <summary>hvcurveto / vhcurveto: curves alternate between starting horizontally and vertically.</summary>
        private void HvCurves(bool startHorizontal)
        {
            bool horizontal = startHorizontal;
            for (int i = 0; _sp - i >= 4; i += 4, horizontal = !horizontal)
            {
                float last = _sp - i == 5 ? _stack[i + 4] : 0f;
                if (horizontal) CurveTo(_stack[i], 0, _stack[i + 1], _stack[i + 2], last, _stack[i + 3]);
                else CurveTo(0, _stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], last);
            }
        }

        /// <summary>CFF2 blend: n default values followed by n * regions deltas collapse to n blended values.</summary>
        private void Blend()
        {
            int n = (int)_stack[--_sp];
            int regions = _scalars.Length;
            int first = _sp - n * (1 + regions);
            if (first < 0) { _sp = 0; return; }

            for (int i = 0; i < n; i++)
            {
                float value = _stack[first + i];
                for (int r = 0; r < regions; r++) value += _stack[first + n + i * regions + r] * _scalars[r];
                _stack[first + i] = value;
            }
            _sp = first + n;
        }

        private void Escape(int op)
        {
            switch (op)
            {
                case 34: // hflex
                    CurveTo(_stack[0], 0, _stack[1], _stack[2], _stack[3], 0);
                    CurveTo(_stack[4], 0, _stack[5], -_stack[2], _stack[6], 0);
                    _sp = 0; break;
                case 35: // flex
                    CurveTo(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
                    CurveTo(_stack[6], _stack[7], _stack[8], _stack[9], _stack[10], _stack[11]);
                    _sp = 0; break;
                case 36: // hflex1
                    CurveTo(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], 0);
                    CurveTo(_stack[5], 0, _stack[6], _stack[7], _stack[8], -(_stack[1] + _stack[3] + _stack[7]));
                    _sp = 0; break;
                case 37: // flex1
                {
                    float dx = _stack[0] + _stack[2] + _stack[4] + _stack[6] + _stack[8];
                    float dy = _stack[1] + _stack[3] + _stack[5] + _stack[7] + _stack[9];
                    CurveTo(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
                    if (Math.Abs(dx) > Math.Abs(dy)) CurveTo(_stack[6], _stack[7], _stack[8], _stack[9], _stack[10], -dy);
                    else CurveTo(_stack[6], _stack[7], _stack[8], _stack[9], -dx, _stack[10]);
                    _sp = 0; break;
                }
                case 9: if (_sp > 0) _stack[_sp - 1] = Math.Abs(_stack[_sp - 1]); break;                        // abs
                case 10: if (_sp > 1) { _stack[_sp - 2] += _stack[_sp - 1]; _sp--; } break;                     // add
                case 11: if (_sp > 1) { _stack[_sp - 2] -= _stack[_sp - 1]; _sp--; } break;                     // sub
                case 12: if (_sp > 1) { _stack[_sp - 2] /= _stack[_sp - 1]; _sp--; } break;                     // div
                case 14: if (_sp > 0) _stack[_sp - 1] = -_stack[_sp - 1]; break;                                // neg
                case 18: if (_sp > 0) _sp--; break;                                                             // drop
                case 24: if (_sp > 1) { _stack[_sp - 2] *= _stack[_sp - 1]; _sp--; } break;                     // mul
                case 26: if (_sp > 0) _stack[_sp - 1] = (float)Math.Sqrt(_stack[_sp - 1]); break;               // sqrt
                case 27: if (_sp > 0) { _stack[_sp] = _stack[_sp - 1]; _sp++; } break;                          // dup
                case 28: if (_sp > 1) { (_stack[_sp - 1], _stack[_sp - 2]) = (_stack[_sp - 2], _stack[_sp - 1]); } break; // exch
                default: _sp = 0; break;
            }
        }
    }

    // ── Cubic to quadratic ───────────────────────────────────────────────────

    /// <summary>Approximates cubic Bezier segments by quadratic ones, the curve type glyf outlines use.</summary>
    private static class Quadratic
    {
        public static List<OutlinePoint> Convert(
            (float x, float y) start, List<Segment> segments, float tolerance)
        {
            var points = new List<OutlinePoint> { new OutlinePoint(start.x, start.y, true) };
            float cx = start.x, cy = start.y;
            foreach (var s in segments)
            {
                if (!s.IsCurve) points.Add(new OutlinePoint(s.X3, s.Y3, true));
                else AddCubic(points, cx, cy, s.X1, s.Y1, s.X2, s.Y2, s.X3, s.Y3, tolerance);
                cx = s.X3; cy = s.Y3;
            }

            // An outline that returns to its start doesn't need the duplicate closing point
            if (points.Count > 1)
            {
                var first = points[0]; var last = points[points.Count - 1];
                if (last.OnCurve && Math.Abs(last.X - first.X) < 0.01f && Math.Abs(last.Y - first.Y) < 0.01f) points.RemoveAt(points.Count - 1);
            }
            return points;
        }

        private static void AddCubic(List<OutlinePoint> pts, float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, float tol)
        {
            // Distance of the cubic from the best single quadratic scales with |p3 - 3c2 + 3c1 - p0| / n^3
            float dx = x3 - 3 * x2 + 3 * x1 - x0, dy = y3 - 3 * y2 + 3 * y1 - y0;
            double deviation = Math.Sqrt(dx * dx + dy * dy) * Math.Sqrt(3.0) / 36.0;
            int n = (int)Math.Ceiling(Math.Pow(deviation / tol, 1.0 / 3.0));
            n = Math.Max(1, Math.Min(n, 16));

            for (int i = 0; i < n; i++)
            {
                float t0 = i / (float)n, t1 = (i + 1) / (float)n, span = t1 - t0;
                Eval(x0, y0, x1, y1, x2, y2, x3, y3, t0, out float sx0, out float sy0, out float dx0, out float dy0);
                Eval(x0, y0, x1, y1, x2, y2, x3, y3, t1, out float sx3, out float sy3, out float dx1, out float dy1);
                // Sub-cubic control points, then the quadratic control point (3(c1 + c2) - p0 - p3) / 4
                float c1x = sx0 + span / 3f * dx0, c1y = sy0 + span / 3f * dy0;
                float c2x = sx3 - span / 3f * dx1, c2y = sy3 - span / 3f * dy1;
                float qx = (3 * (c1x + c2x) - sx0 - sx3) / 4f, qy = (3 * (c1y + c2y) - sy0 - sy3) / 4f;
                pts.Add(new OutlinePoint(qx, qy, false));
                pts.Add(new OutlinePoint(sx3, sy3, true));
            }
        }

        private static void Eval(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, float t,
            out float x, out float y, out float dx, out float dy)
        {
            float u = 1 - t;
            x = u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3;
            y = u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3;
            dx = 3 * (u * u * (x1 - x0) + 2 * u * t * (x2 - x1) + t * t * (x3 - x2));
            dy = 3 * (u * u * (y1 - y0) + 2 * u * t * (y2 - y1) + t * t * (y3 - y2));
        }
    }
}
