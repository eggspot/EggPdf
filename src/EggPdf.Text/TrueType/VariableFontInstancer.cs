using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace EggPdf.Text.TrueType;

/// <summary>
/// Turns an OpenType variable font (fvar + gvar outlines, avar axis remapping, HVAR advance
/// variations) into an ordinary static TrueType font at chosen axis coordinates, so the rest of the
/// pipeline -- measuring, shaping, subsetting, PDF embedding -- keeps working on plain glyf fonts.
/// Composite glyphs are re-derived from their instanced components. Hinting instructions, the
/// cvt/MVAR metric variations and GDEF/GPOS variation stores are dropped: kerning values stay at
/// the default instance. Supports the <c>glyf</c>-flavoured variable fonts (not CFF2).
/// </summary>
public static partial class VariableFontInstancer
{
    /// <summary>One variation axis from the font's fvar table.</summary>
    public readonly struct Axis
    {
        public readonly string Tag;
        public readonly float Min, Default, Max;
        public Axis(string tag, float min, float def, float max) { Tag = tag; Min = min; Default = def; Max = max; }
    }

    private static readonly ConditionalWeakTable<FontData, Dictionary<string, FontData?>> InstanceCache =
        new ConditionalWeakTable<FontData, Dictionary<string, FontData?>>();

    /// <summary>The variation axes of <paramref name="font"/>, or an empty list for a static font.</summary>
    public static IReadOnlyList<Axis> GetAxes(FontData font)
    {
        var sfnt = Sfnt.TryRead(font.RawData);
        return sfnt == null ? Array.Empty<Axis>() : ReadAxes(sfnt);
    }

    /// <summary>
    /// The font at a given CSS <c>font-weight</c>: a variable font with a <c>wght</c> axis is instanced at that
    /// weight (clamped to the axis range) and re-parsed; any other font is returned unchanged. Results are
    /// cached per font and weight.
    /// </summary>
    public static FontData InstanceForWeight(FontData font, int weight) => InstanceFor(font, weight, null);

    /// <summary>
    /// The font at a CSS weight plus explicit design-axis values (wdth, slnt, opsz, custom tags; an explicit
    /// <c>wght</c> in <paramref name="axes"/> overrides <paramref name="weight"/>). Only axes the font actually has
    /// take part; a font with none of them is returned unchanged. Results are cached per font and settings.
    /// </summary>
    public static FontData InstanceFor(FontData font, int weight, IReadOnlyDictionary<string, float>? axes)
    {
        if (font.RawData == null || font.RawData.Length == 0) return font;

        var key = new System.Text.StringBuilder().Append(weight);
        if (axes != null)
            foreach (var kv in new SortedDictionary<string, float>(new Dictionary<string, float>(axes), StringComparer.Ordinal))
                key.Append('|').Append(kv.Key).Append('=').Append(kv.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        var cacheKey = key.ToString();

        var perFont = InstanceCache.GetOrCreateValue(font);
        lock (perFont)
        {
            if (perFont.TryGetValue(cacheKey, out var cached)) return cached ?? font;

            FontData? instance = null;
            var sfnt = Sfnt.TryRead(font.RawData);
            if (sfnt != null && sfnt.Has("fvar") && sfnt.Has("gvar"))
            {
                var requested = new Dictionary<string, float> { ["wght"] = weight };
                if (axes != null) foreach (var kv in axes) requested[kv.Key] = kv.Value;

                bool usable = false;
                foreach (var axis in ReadAxes(sfnt)) usable |= requested.ContainsKey(axis.Tag);
                if (usable)
                {
                    var bytes = Instantiate(font.RawData, requested);
                    if (bytes != null)
                    {
                        try { instance = TtfParser.Parse(bytes); } catch (Exception) { instance = null; }
                    }
                }
            }
            perFont[cacheKey] = instance;
            return instance ?? font;
        }
    }

    /// <summary>
    /// Build a static TrueType font from a variable one. <paramref name="userCoords"/> maps axis tags to values
    /// in the axis' own units (e.g. wght 650); missing axes use their default. Null when the font is not a
    /// supported variable font.
    /// </summary>
    public static byte[]? Instantiate(byte[] fontData, IReadOnlyDictionary<string, float> userCoords)
    {
        try
        {
            var sfnt = Sfnt.TryRead(fontData);
            if (sfnt == null || !sfnt.Has("fvar") || !sfnt.Has("gvar") || !sfnt.Has("glyf") || !sfnt.Has("loca")) return null;

            var axes = ReadAxes(sfnt);
            float[] coords = Normalize(sfnt, axes, userCoords);

            var context = new InstanceContext(sfnt, coords);
            return context.Build();
        }
        catch (Exception)
        {
            return null; // infallible by convention: an unreadable variable font stays at its default instance
        }
    }

    // ── fvar / avar ──────────────────────────────────────────────────────────

    private static List<Axis> ReadAxes(Sfnt sfnt)
    {
        var axes = new List<Axis>();
        if (!sfnt.Has("fvar")) return axes;

        int t = sfnt.Offset("fvar");
        var d = sfnt.Data;
        int axesOffset = U16(d, t + 4), axisCount = U16(d, t + 8), axisSize = U16(d, t + 10);
        for (int i = 0; i < axisCount; i++)
        {
            int a = t + axesOffset + i * axisSize;
            string tag = "" + (char)d[a] + (char)d[a + 1] + (char)d[a + 2] + (char)d[a + 3];
            axes.Add(new Axis(tag, Fixed(d, a + 4), Fixed(d, a + 8), Fixed(d, a + 12)));
        }
        return axes;
    }

    /// <summary>User-space coordinates to normalized [-1, 1] (then avar-remapped), one per axis.</summary>
    private static float[] Normalize(Sfnt sfnt, List<Axis> axes, IReadOnlyDictionary<string, float> user)
    {
        var result = new float[axes.Count];
        for (int i = 0; i < axes.Count; i++)
        {
            var axis = axes[i];
            float v = user.TryGetValue(axis.Tag, out var given) ? given : axis.Default;
            v = Math.Max(axis.Min, Math.Min(axis.Max, v));

            float n;
            if (v == axis.Default) n = 0f;
            else if (v < axis.Default) n = axis.Default == axis.Min ? 0f : -(axis.Default - v) / (axis.Default - axis.Min);
            else n = axis.Max == axis.Default ? 0f : (v - axis.Default) / (axis.Max - axis.Default);
            result[i] = n;
        }

        if (sfnt.Has("avar")) ApplyAvar(sfnt, result);
        return result;
    }

    private static void ApplyAvar(Sfnt sfnt, float[] coords)
    {
        var d = sfnt.Data;
        int p = sfnt.Offset("avar");
        int axisCount = U16(d, p + 6);
        p += 8;
        for (int axis = 0; axis < axisCount; axis++)
        {
            int mapCount = U16(d, p);
            p += 2;
            if (axis < coords.Length && mapCount > 0)
            {
                float c = coords[axis];
                float result = c;
                for (int k = 0; k < mapCount; k++)
                {
                    float from = F2Dot14(d, p + k * 4), to = F2Dot14(d, p + k * 4 + 2);
                    if (c == from) { result = to; break; }
                    if (k + 1 < mapCount)
                    {
                        float nextFrom = F2Dot14(d, p + (k + 1) * 4), nextTo = F2Dot14(d, p + (k + 1) * 4 + 2);
                        if (c > from && c < nextFrom)
                        {
                            result = to + (nextTo - to) * (c - from) / (nextFrom - from);
                            break;
                        }
                    }
                }
                coords[axis] = result;
            }
            p += mapCount * 4;
        }
    }

    // ── Big-endian primitives ────────────────────────────────────────────────

    internal static int U16(byte[] d, int o) => (d[o] << 8) | d[o + 1];
    internal static int I16(byte[] d, int o) => (short)((d[o] << 8) | d[o + 1]);
    internal static uint U32(byte[] d, int o) => (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);
    internal static float F2Dot14(byte[] d, int o) => I16(d, o) / 16384f;
    internal static float Fixed(byte[] d, int o) => (int)U32(d, o) / 65536f;

    /// <summary>The sfnt table directory of a font file.</summary>
    internal sealed class Sfnt
    {
        public readonly byte[] Data;
        private readonly Dictionary<string, (int offset, int length)> _tables = new Dictionary<string, (int, int)>();
        public readonly uint Version;

        private Sfnt(byte[] data) { Data = data; Version = U32(data, 0); }

        public static Sfnt? TryRead(byte[]? data)
        {
            if (data == null || data.Length < 12) return null;
            uint version = U32(data, 0);
            if (version != 0x00010000 && version != 0x74727565 /* 'true' */) return null; // TTC and CFF ('OTTO') are out of scope

            var sfnt = new Sfnt(data);
            int count = U16(data, 4);
            for (int i = 0; i < count; i++)
            {
                int r = 12 + i * 16;
                if (r + 16 > data.Length) return null;
                string tag = "" + (char)data[r] + (char)data[r + 1] + (char)data[r + 2] + (char)data[r + 3];
                int offset = (int)U32(data, r + 8), length = (int)U32(data, r + 12);
                if (offset < 0 || length < 0 || offset + length > data.Length) return null;
                sfnt._tables[tag] = (offset, length);
            }
            return sfnt;
        }

        public bool Has(string tag) => _tables.ContainsKey(tag);
        public int Offset(string tag) => _tables[tag].offset;
        public int Length(string tag) => _tables[tag].length;
        public IEnumerable<string> Tags => _tables.Keys;
    }
}
