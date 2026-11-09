using System;
using System.Collections.Generic;
using System.Globalization;
using EggPdf.Core;

namespace EggPdf.Pdf;

internal sealed class GradientStops
{
    private readonly List<(float R, float G, float B, float A, float Position)> _stops;

    private GradientStops(List<(float R, float G, float B, float A, float Position)> stops) => _stops = stops;

    public bool HasTranslucentStop
    {
        get
        {
            foreach (var stop in _stops)
                if (stop.A < 1f) return true;
            return false;
        }
    }

    public static GradientStops? Parse(List<string> parts, int firstStop, float lineLength)
    {
        var colors = new List<(float R, float G, float B, float A)>();
        var positions = new List<float?>();
        for (int i = firstStop; i < parts.Count; i++)
        {
            var part = parts[i].Trim();
            if (part.Length == 0) continue;
            var colorToken = ColorToken(part);
            var color = Color.TryParse(colorToken);
            if (!color.HasValue) continue;
            var rest = part.Substring(colorToken.Length).Trim();
            var positionTokens = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var rgba = (color.Value.R / 255f, color.Value.G / 255f, color.Value.B / 255f, color.Value.A / 255f);
            if (positionTokens.Length == 0)
            {
                colors.Add(rgba);
                positions.Add(null);
            }
            foreach (var token in positionTokens)
            {
                colors.Add(rgba);
                positions.Add(ParsePosition(token, lineLength));
            }
        }
        if (colors.Count == 0) return null;
        if (colors.Count == 1)
        {
            colors.Add(colors[0]);
            positions.Add(null);
        }

        if (!positions[0].HasValue) positions[0] = 0f;
        if (!positions[positions.Count - 1].HasValue) positions[positions.Count - 1] = 1f;
        float previous = positions[0]!.Value;
        for (int i = 1; i < positions.Count; i++)
        {
            if (positions[i].HasValue && positions[i]!.Value < previous) positions[i] = previous;
            if (positions[i].HasValue) previous = positions[i]!.Value;
        }
        for (int i = 1; i < positions.Count - 1; i++)
        {
            if (positions[i].HasValue) continue;
            int next = i + 1;
            while (!positions[next].HasValue) next++;
            float from = positions[i - 1]!.Value, to = positions[next]!.Value;
            for (int k = i; k < next; k++)
                positions[k] = from + (to - from) * (k - i + 1) / (next - i + 1);
        }

        var stops = new List<(float, float, float, float, float)>(colors.Count);
        for (int i = 0; i < colors.Count; i++)
            stops.Add((colors[i].R, colors[i].G, colors[i].B, colors[i].A, positions[i]!.Value));
        return new GradientStops(stops);
    }

    public (float R, float G, float B, float A) ColorAt(float t, bool repeating)
    {
        float first = _stops[0].Position, last = _stops[_stops.Count - 1].Position;
        if (repeating && last > first)
        {
            float period = last - first;
            t = first + (float)((t - first) - Math.Floor((t - first) / period) * period);
        }
        if (t <= first) return Unpack(_stops[0]);
        if (t >= last) return Unpack(_stops[_stops.Count - 1]);
        for (int i = 0; i < _stops.Count - 1; i++)
        {
            var s0 = _stops[i];
            var s1 = _stops[i + 1];
            if (t > s1.Position) continue;
            float span = s1.Position - s0.Position;
            float local = span > 0f ? (t - s0.Position) / span : 1f;
            float alpha = s0.A + (s1.A - s0.A) * local;
            if (alpha <= 0f) return (s0.R + (s1.R - s0.R) * local, s0.G + (s1.G - s0.G) * local, s0.B + (s1.B - s0.B) * local, 0f);
            return (
                (s0.R * s0.A * (1f - local) + s1.R * s1.A * local) / alpha,
                (s0.G * s0.A * (1f - local) + s1.G * s1.A * local) / alpha,
                (s0.B * s0.A * (1f - local) + s1.B * s1.A * local) / alpha,
                alpha);
        }
        return Unpack(_stops[_stops.Count - 1]);
    }

    public static string ColorToken(string stop)
    {
        stop = stop.Trim();
        int paren = stop.IndexOf('(');
        int space = stop.IndexOf(' ');
        if (paren >= 0 && (space < 0 || paren < space))
        {
            int depth = 0;
            for (int i = paren; i < stop.Length; i++)
            {
                if (stop[i] == '(') depth++;
                else if (stop[i] == ')' && --depth == 0) return stop.Substring(0, i + 1);
            }
            return stop;
        }
        return space > 0 ? stop.Substring(0, space) : stop;
    }

    private static float? ParsePosition(string token, float lineLength)
    {
        token = token.Trim().ToLowerInvariant();
        float factor;
        string number;
        if (token.EndsWith("%", StringComparison.Ordinal)) { factor = 0.01f; number = token.Substring(0, token.Length - 1); }
        else if (token.EndsWith("px", StringComparison.Ordinal)) { factor = 0.75f / lineLength; number = token.Substring(0, token.Length - 2); }
        else if (token.EndsWith("pt", StringComparison.Ordinal)) { factor = 1f / lineLength; number = token.Substring(0, token.Length - 2); }
        else if (token.EndsWith("em", StringComparison.Ordinal)) { factor = 12f / lineLength; number = token.Substring(0, token.Length - 2); }
        else if (token == "0") return 0f;
        else return null;
        return float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value * factor : null;
    }

    private static (float R, float G, float B, float A) Unpack((float R, float G, float B, float A, float Position) stop) =>
        (stop.R, stop.G, stop.B, stop.A);
}
