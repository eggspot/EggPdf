using System;
using System.Collections.Generic;
using System.Globalization;

namespace EggPdf.Svg;

internal enum PathSegmentKind : byte
{
    MoveTo,
    LineTo,
    CubicTo,
    Close,
}

internal readonly struct PathSegment
{
    public PathSegment(PathSegmentKind kind, float x1, float y1, float x2, float y2, float x, float y)
    {
        Kind = kind;
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
        X = x;
        Y = y;
    }

    public PathSegmentKind Kind { get; }
    public float X1 { get; }
    public float Y1 { get; }
    public float X2 { get; }
    public float Y2 { get; }
    public float X { get; }
    public float Y { get; }

    public static PathSegment MoveTo(float x, float y) => new(PathSegmentKind.MoveTo, 0, 0, 0, 0, x, y);
    public static PathSegment LineTo(float x, float y) => new(PathSegmentKind.LineTo, 0, 0, 0, 0, x, y);
    public static PathSegment CubicTo(float x1, float y1, float x2, float y2, float x, float y) =>
        new(PathSegmentKind.CubicTo, x1, y1, x2, y2, x, y);
    public static PathSegment Close(float startX, float startY) => new(PathSegmentKind.Close, 0, 0, 0, 0, startX, startY);
}

internal static class SvgPathData
{
    public static List<PathSegment> Parse(string d)
    {
        var segments = new List<PathSegment>();
        if (string.IsNullOrEmpty(d)) return segments;

        int i = 0;
        char command = ' ';
        char previousCommand = ' ';
        float currentX = 0, currentY = 0, startX = 0, startY = 0;
        float lastControlX = 0, lastControlY = 0;

        while (true)
        {
            SkipSeparators(d, ref i);
            if (i >= d.Length) break;

            char c = d[i];
            if (IsCommandLetter(c))
            {
                command = c;
                i++;
            }
            else if (command == ' ' || command == 'Z' || command == 'z' || !StartsNumber(c))
            {
                break;
            }

            char upper = char.ToUpperInvariant(command);
            bool relative = command != upper;
            if (segments.Count == 0 && upper != 'M') break;
            float baseX = relative ? currentX : 0f, baseY = relative ? currentY : 0f;

            switch (upper)
            {
                case 'M':
                {
                    if (!TryReadNumber(d, ref i, out float x) || !TryReadNumber(d, ref i, out float y)) return segments;
                    currentX = startX = baseX + x;
                    currentY = startY = baseY + y;
                    segments.Add(PathSegment.MoveTo(currentX, currentY));
                    command = relative ? 'l' : 'L';
                    break;
                }
                case 'L':
                {
                    if (!TryReadNumber(d, ref i, out float x) || !TryReadNumber(d, ref i, out float y)) return segments;
                    currentX = baseX + x;
                    currentY = baseY + y;
                    segments.Add(PathSegment.LineTo(currentX, currentY));
                    break;
                }
                case 'H':
                {
                    if (!TryReadNumber(d, ref i, out float x)) return segments;
                    currentX = baseX + x;
                    segments.Add(PathSegment.LineTo(currentX, currentY));
                    break;
                }
                case 'V':
                {
                    if (!TryReadNumber(d, ref i, out float y)) return segments;
                    currentY = baseY + y;
                    segments.Add(PathSegment.LineTo(currentX, currentY));
                    break;
                }
                case 'C':
                {
                    if (!TryReadNumber(d, ref i, out float x1) || !TryReadNumber(d, ref i, out float y1) ||
                        !TryReadNumber(d, ref i, out float x2) || !TryReadNumber(d, ref i, out float y2) ||
                        !TryReadNumber(d, ref i, out float x) || !TryReadNumber(d, ref i, out float y))
                        return segments;
                    lastControlX = baseX + x2;
                    lastControlY = baseY + y2;
                    currentX = baseX + x;
                    currentY = baseY + y;
                    segments.Add(PathSegment.CubicTo(baseX + x1, baseY + y1, lastControlX, lastControlY, currentX, currentY));
                    break;
                }
                case 'S':
                {
                    if (!TryReadNumber(d, ref i, out float x2) || !TryReadNumber(d, ref i, out float y2) ||
                        !TryReadNumber(d, ref i, out float x) || !TryReadNumber(d, ref i, out float y))
                        return segments;
                    bool reflects = previousCommand == 'C' || previousCommand == 'S';
                    float x1 = reflects ? 2 * currentX - lastControlX : currentX;
                    float y1 = reflects ? 2 * currentY - lastControlY : currentY;
                    lastControlX = baseX + x2;
                    lastControlY = baseY + y2;
                    currentX = baseX + x;
                    currentY = baseY + y;
                    segments.Add(PathSegment.CubicTo(x1, y1, lastControlX, lastControlY, currentX, currentY));
                    break;
                }
                case 'Q':
                {
                    if (!TryReadNumber(d, ref i, out float qx) || !TryReadNumber(d, ref i, out float qy) ||
                        !TryReadNumber(d, ref i, out float x) || !TryReadNumber(d, ref i, out float y))
                        return segments;
                    lastControlX = baseX + qx;
                    lastControlY = baseY + qy;
                    AddQuadratic(segments, ref currentX, ref currentY, lastControlX, lastControlY, baseX + x, baseY + y);
                    break;
                }
                case 'T':
                {
                    if (!TryReadNumber(d, ref i, out float x) || !TryReadNumber(d, ref i, out float y)) return segments;
                    bool reflects = previousCommand == 'Q' || previousCommand == 'T';
                    lastControlX = reflects ? 2 * currentX - lastControlX : currentX;
                    lastControlY = reflects ? 2 * currentY - lastControlY : currentY;
                    AddQuadratic(segments, ref currentX, ref currentY, lastControlX, lastControlY, baseX + x, baseY + y);
                    break;
                }
                case 'A':
                {
                    if (!TryReadNumber(d, ref i, out float rx) || !TryReadNumber(d, ref i, out float ry) ||
                        !TryReadNumber(d, ref i, out float rotation) || !TryReadFlag(d, ref i, out bool largeArc) ||
                        !TryReadFlag(d, ref i, out bool sweep) || !TryReadNumber(d, ref i, out float x) ||
                        !TryReadNumber(d, ref i, out float y))
                        return segments;
                    float endX = baseX + x, endY = baseY + y;
                    AddArc(segments, currentX, currentY, rx, ry, rotation, largeArc, sweep, endX, endY);
                    currentX = endX;
                    currentY = endY;
                    break;
                }
                case 'Z':
                    segments.Add(PathSegment.Close(startX, startY));
                    currentX = startX;
                    currentY = startY;
                    break;
                default:
                    return segments;
            }
            previousCommand = upper;
        }
        return segments;
    }

    public static bool TryReadNumber(string s, ref int i, out float value)
    {
        value = 0f;
        SkipSeparators(s, ref i);
        int start = i;
        int pos = i;
        if (pos < s.Length && (s[pos] == '+' || s[pos] == '-')) pos++;
        int digits = 0;
        while (pos < s.Length && IsDigit(s[pos])) { pos++; digits++; }
        if (pos < s.Length && s[pos] == '.')
        {
            pos++;
            while (pos < s.Length && IsDigit(s[pos])) { pos++; digits++; }
        }
        if (digits == 0) return false;
        if (pos < s.Length && (s[pos] == 'e' || s[pos] == 'E'))
        {
            int exponent = pos + 1;
            if (exponent < s.Length && (s[exponent] == '+' || s[exponent] == '-')) exponent++;
            if (exponent < s.Length && IsDigit(s[exponent]))
            {
                pos = exponent;
                while (pos < s.Length && IsDigit(s[pos])) pos++;
            }
        }
        if (!double.TryParse(s.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
            double.IsNaN(parsed) || double.IsInfinity(parsed))
            return false;
        value = (float)Math.Max(float.MinValue, Math.Min(float.MaxValue, parsed));
        i = pos;
        return true;
    }

    private static bool TryReadFlag(string s, ref int i, out bool flag)
    {
        flag = false;
        SkipSeparators(s, ref i);
        if (i >= s.Length || (s[i] != '0' && s[i] != '1')) return false;
        flag = s[i] == '1';
        i++;
        return true;
    }

    public static void SkipSeparators(string s, ref int i)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n' || s[i] == '\f' || s[i] == ','))
            i++;
    }

    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    private static bool StartsNumber(char c) => IsDigit(c) || c == '.' || c == '-' || c == '+';

    private static bool IsCommandLetter(char c)
    {
        switch (c)
        {
            case 'M': case 'm': case 'L': case 'l': case 'H': case 'h': case 'V': case 'v':
            case 'C': case 'c': case 'S': case 's': case 'Q': case 'q': case 'T': case 't':
            case 'A': case 'a': case 'Z': case 'z':
                return true;
            default:
                return false;
        }
    }

    private static void AddQuadratic(List<PathSegment> segments, ref float currentX, ref float currentY,
        float controlX, float controlY, float x, float y)
    {
        const float twoThirds = 2f / 3f;
        segments.Add(PathSegment.CubicTo(
            currentX + twoThirds * (controlX - currentX), currentY + twoThirds * (controlY - currentY),
            x + twoThirds * (controlX - x), y + twoThirds * (controlY - y),
            x, y));
        currentX = x;
        currentY = y;
    }

    private static void AddArc(List<PathSegment> segments, float x0, float y0, float rx, float ry,
        float rotationDegrees, bool largeArc, bool sweep, float x1, float y1)
    {
        if (x0 == x1 && y0 == y1) return;
        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        if (rx == 0f || ry == 0f)
        {
            segments.Add(PathSegment.LineTo(x1, y1));
            return;
        }

        double phi = rotationDegrees * Math.PI / 180.0;
        double cosPhi = Math.Cos(phi), sinPhi = Math.Sin(phi);
        double dx2 = (x0 - x1) / 2.0, dy2 = (y0 - y1) / 2.0;
        double x1p = cosPhi * dx2 + sinPhi * dy2, y1p = -sinPhi * dx2 + cosPhi * dy2;

        double rxd = rx, ryd = ry;
        double lambda = x1p * x1p / (rxd * rxd) + y1p * y1p / (ryd * ryd);
        if (lambda > 1.0)
        {
            double scale = Math.Sqrt(lambda);
            rxd *= scale;
            ryd *= scale;
        }

        double numerator = rxd * rxd * ryd * ryd - rxd * rxd * y1p * y1p - ryd * ryd * x1p * x1p;
        double denominator = rxd * rxd * y1p * y1p + ryd * ryd * x1p * x1p;
        double coefficient = denominator == 0.0 ? 0.0 : Math.Sqrt(Math.Max(0.0, numerator / denominator));
        if (largeArc == sweep) coefficient = -coefficient;
        double cxp = coefficient * rxd * y1p / ryd, cyp = -coefficient * ryd * x1p / rxd;
        double cx = cosPhi * cxp - sinPhi * cyp + (x0 + x1) / 2.0;
        double cy = sinPhi * cxp + cosPhi * cyp + (y0 + y1) / 2.0;

        double theta1 = Math.Atan2((y1p - cyp) / ryd, (x1p - cxp) / rxd);
        double theta2 = Math.Atan2((-y1p - cyp) / ryd, (-x1p - cxp) / rxd);
        double delta = theta2 - theta1;
        if (sweep && delta < 0) delta += 2 * Math.PI;
        else if (!sweep && delta > 0) delta -= 2 * Math.PI;

        int pieces = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9));
        double step = delta / pieces;
        double handle = 4.0 / 3.0 * Math.Tan(step / 4.0);
        double angle = theta1;
        for (int piece = 0; piece < pieces; piece++)
        {
            double cos1 = Math.Cos(angle), sin1 = Math.Sin(angle);
            double cos2 = Math.Cos(angle + step), sin2 = Math.Sin(angle + step);
            var (c1x, c1y) = ArcPoint(cx, cy, rxd, ryd, cosPhi, sinPhi, cos1 - handle * sin1, sin1 + handle * cos1);
            var (c2x, c2y) = ArcPoint(cx, cy, rxd, ryd, cosPhi, sinPhi, cos2 + handle * sin2, sin2 - handle * cos2);
            var (ex, ey) = piece == pieces - 1 ? (x1, y1) : ArcPoint(cx, cy, rxd, ryd, cosPhi, sinPhi, cos2, sin2);
            segments.Add(PathSegment.CubicTo(c1x, c1y, c2x, c2y, ex, ey));
            angle += step;
        }
    }

    private static (float X, float Y) ArcPoint(double cx, double cy, double rx, double ry,
        double cosPhi, double sinPhi, double unitX, double unitY)
    {
        double ex = rx * unitX, ey = ry * unitY;
        return ((float)(cosPhi * ex - sinPhi * ey + cx), (float)(sinPhi * ex + cosPhi * ey + cy));
    }
}
