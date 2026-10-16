using System;
using System.Collections.Generic;

namespace EggPdf.Text.TrueType;

/// <summary>
/// TrueType Collection ('ttcf') support. The parser, subsetter and layout reader all work on one
/// standalone font, and for a collection they read its first face; <see cref="ExtractFace"/> turns
/// any other face into a standalone sfnt so those components need no face-index plumbing.
/// </summary>
internal static class FontCollection
{
    /// <summary>
    /// A standalone font file holding face <paramref name="faceIndex"/> of the collection, or null when the
    /// data is not a collection or the index is out of range. Table data is copied (tables shared between
    /// faces are duplicated), keeping the face's original sfnt version ('OTTO' stays 'OTTO').
    /// </summary>
    public static byte[]? ExtractFace(byte[] data, int faceIndex)
    {
        try
        {
            if (data.Length < 16 || U32(data, 0) != 0x74746366) return null; // 'ttcf'
            uint numFaces = U32(data, 8);
            if (faceIndex < 0 || faceIndex >= numFaces) return null;

            int dir = (int)U32(data, 12 + faceIndex * 4);
            int numTables = (data[dir + 4] << 8) | data[dir + 5];
            var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            for (int i = 0; i < numTables; i++)
            {
                int rec = dir + 12 + i * 16;
                string tag = new string(new[] { (char)data[rec], (char)data[rec + 1], (char)data[rec + 2], (char)data[rec + 3] });
                int offset = (int)U32(data, rec + 8);
                int length = (int)U32(data, rec + 12);
                if (offset < 0 || length < 0 || offset + length > data.Length) return null;
                var table = new byte[length];
                Buffer.BlockCopy(data, offset, table, 0, length);
                tables[tag] = table;
            }

            var face = SfntWriter.WriteFont(tables);
            Buffer.BlockCopy(data, dir, face, 0, 4); // restore the face's sfnt version (WriteFont assumes TrueType outlines)
            return face;
        }
        catch
        {
            return null;
        }
    }

    private static uint U32(byte[] d, int at)
        => ((uint)d[at] << 24) | ((uint)d[at + 1] << 16) | ((uint)d[at + 2] << 8) | d[at + 3];
}
