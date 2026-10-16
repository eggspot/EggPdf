using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EggPdf.Text.TrueType;

namespace EggPdf.Tests.Unit.Text;

/// <summary>
/// Builds tiny, valid-enough TrueType fonts (and collections) that map a chosen set of code points,
/// so font-fallback logic can be tested deterministically without any installed fonts.
/// Code point i (in ascending order) maps to glyph i + 1.
/// </summary>
internal static class SyntheticFonts
{
    private static byte[] Be16(int v) => new[] { (byte)(v >> 8), (byte)v };
    private static byte[] Be32(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

    /// <param name="codepoints">Code points the font maps.</param>
    /// <param name="advance">Advance width of every glyph, in a 1000 units-per-em font.</param>
    /// <param name="outlines">Include a glyf table (false emulates a font with nothing drawable).</param>
    /// <param name="bitmapColorOnly">Include an sbix table and no COLR (a bitmap emoji font the engine cannot paint).</param>
    /// <param name="format4">Write the cmap as format 4 (BMP only) instead of format 12.</param>
    public static byte[] Build(IEnumerable<int> codepoints, int advance = 1000, bool outlines = true,
        bool bitmapColorOnly = false, bool format4 = false)
    {
        var sorted = codepoints.Distinct().OrderBy(c => c).ToList();
        int numGlyphs = sorted.Count + 1;

        var head = new byte[54];
        head[18] = 0x03; head[19] = 0xE8;                                  // unitsPerEm 1000
        var hhea = new byte[36];
        hhea[4] = 0x03; hhea[5] = 0x20;                                    // ascent 800
        hhea[34] = (byte)(numGlyphs >> 8); hhea[35] = (byte)numGlyphs;     // numberOfHMetrics
        var maxp = Be32(0x00005000).Concat(Be16(numGlyphs)).ToArray();

        var hmtx = new List<byte>();
        for (int i = 0; i < numGlyphs; i++) { hmtx.AddRange(Be16(advance)); hmtx.AddRange(Be16(0)); }

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["cmap"] = format4 ? Format4Cmap(sorted) : Format12Cmap(sorted),
            ["head"] = head, ["hhea"] = hhea, ["hmtx"] = hmtx.ToArray(), ["maxp"] = maxp,
        };
        if (outlines)
        {
            tables["glyf"] = new byte[4];
            tables["loca"] = new byte[(numGlyphs + 1) * 2]; // short offsets, every glyph empty
        }
        if (bitmapColorOnly) tables["sbix"] = new byte[8];
        return SfntWriter.WriteFont(tables);
    }

    /// <summary>A TrueType Collection ('ttcf') whose faces are the given standalone fonts.</summary>
    public static byte[] BuildCollection(params byte[][] faces)
    {
        var output = new List<byte>();
        output.AddRange(new byte[] { (byte)'t', (byte)'t', (byte)'c', (byte)'f' });
        output.AddRange(Be32(0x00010000));
        output.AddRange(Be32((uint)faces.Length));

        // Directories first (after the header + offset array), table data after all directories
        var directoryOffsets = new int[faces.Length];
        int cursor = 12 + faces.Length * 4;
        var parsed = new List<(string tag, byte[] data)[]>();
        foreach (var face in faces)
        {
            int numTables = (face[4] << 8) | face[5];
            var tables = new (string tag, byte[] data)[numTables];
            for (int t = 0; t < numTables; t++)
            {
                int rec = 12 + t * 16;
                string tag = System.Text.Encoding.ASCII.GetString(face, rec, 4);
                int offset = (face[rec + 8] << 24) | (face[rec + 9] << 16) | (face[rec + 10] << 8) | face[rec + 11];
                int length = (face[rec + 12] << 24) | (face[rec + 13] << 16) | (face[rec + 14] << 8) | face[rec + 15];
                tables[t] = (tag, face.Skip(offset).Take(length).ToArray());
            }
            parsed.Add(tables);
        }
        for (int f = 0; f < faces.Length; f++)
        {
            directoryOffsets[f] = cursor;
            output.AddRange(Be32((uint)directoryOffsets[f]));
            cursor += 12 + parsed[f].Length * 16;
        }

        var body = new List<byte>();
        int dataStart = cursor;
        var directories = new List<byte>();
        foreach (var tables in parsed)
        {
            directories.AddRange(Be32(0x00010000));
            directories.AddRange(Be16(tables.Length));
            directories.AddRange(new byte[6]);
            foreach (var (tag, data) in tables)
            {
                directories.AddRange(System.Text.Encoding.ASCII.GetBytes(tag));
                directories.AddRange(Be32(0));
                directories.AddRange(Be32((uint)(dataStart + body.Count)));
                directories.AddRange(Be32((uint)data.Length));
                body.AddRange(data);
                while (body.Count % 4 != 0) body.Add(0);
            }
        }
        output.AddRange(directories);
        output.AddRange(body);
        return output.ToArray();
    }

    /// <summary>Writes a font file into <paramref name="directory"/> and returns its path.</summary>
    public static string Write(string directory, string fileName, byte[] data)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static List<(int start, int end, int firstGlyph)> Runs(List<int> sorted)
    {
        var runs = new List<(int start, int end, int firstGlyph)>();
        for (int i = 0; i < sorted.Count; i++)
        {
            if (runs.Count > 0 && runs[runs.Count - 1].end + 1 == sorted[i])
                runs[runs.Count - 1] = (runs[runs.Count - 1].start, sorted[i], runs[runs.Count - 1].firstGlyph);
            else
                runs.Add((sorted[i], sorted[i], i + 1));
        }
        return runs;
    }

    private static byte[] Format12Cmap(List<int> sorted)
    {
        var runs = Runs(sorted);
        var cmap = new List<byte>();
        cmap.AddRange(Be16(0)); cmap.AddRange(Be16(1));
        cmap.AddRange(Be16(3)); cmap.AddRange(Be16(10)); cmap.AddRange(Be32(12));
        cmap.AddRange(Be16(12)); cmap.AddRange(Be16(0)); cmap.AddRange(Be32((uint)(16 + runs.Count * 12)));
        cmap.AddRange(Be32(0)); cmap.AddRange(Be32((uint)runs.Count));
        foreach (var (start, end, glyph) in runs)
        {
            cmap.AddRange(Be32((uint)start)); cmap.AddRange(Be32((uint)end)); cmap.AddRange(Be32((uint)glyph));
        }
        return cmap.ToArray();
    }

    private static byte[] Format4Cmap(List<int> sorted)
    {
        if (sorted.Any(c => c > 0xFFFE)) throw new ArgumentException("format 4 maps the BMP only");
        var runs = Runs(sorted);
        int segCount = runs.Count + 1; // + the 0xFFFF terminator segment
        var sub = new List<byte>();
        sub.AddRange(Be16(4)); sub.AddRange(Be16(16 + segCount * 8)); sub.AddRange(Be16(0));
        sub.AddRange(Be16(segCount * 2)); sub.AddRange(Be16(0)); sub.AddRange(Be16(0)); sub.AddRange(Be16(0));
        foreach (var r in runs) sub.AddRange(Be16(r.end));
        sub.AddRange(Be16(0xFFFF));
        sub.AddRange(Be16(0));
        foreach (var r in runs) sub.AddRange(Be16(r.start));
        sub.AddRange(Be16(0xFFFF));
        foreach (var r in runs) sub.AddRange(Be16((r.firstGlyph - r.start) & 0xFFFF)); // idDelta
        sub.AddRange(Be16(1));
        foreach (var _ in runs) sub.AddRange(Be16(0));                                  // idRangeOffset
        sub.AddRange(Be16(0));

        var cmap = new List<byte>();
        cmap.AddRange(Be16(0)); cmap.AddRange(Be16(1));
        cmap.AddRange(Be16(3)); cmap.AddRange(Be16(1)); cmap.AddRange(Be32(12));
        cmap.AddRange(sub);
        return cmap.ToArray();
    }
}
