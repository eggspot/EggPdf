using System;

namespace EggPdf.Text.OpenType;

/// <summary>
/// Bounds-safe big-endian reader over a font byte array. Out-of-range reads return 0 rather than
/// throwing -- the OpenType parsers are infallible on malformed/hostile fonts (worst case a
/// lookup silently matches nothing).
/// </summary>
internal readonly struct OtReader
{
    public readonly byte[] Data;

    public OtReader(byte[] data) { Data = data; }

    public int Length => Data.Length;

    public byte U8(int o) => o >= 0 && o < Data.Length ? Data[o] : (byte)0;

    public ushort U16(int o)
    {
        if (o < 0 || o + 2 > Data.Length) return 0;
        return (ushort)((Data[o] << 8) | Data[o + 1]);
    }

    public short I16(int o) => unchecked((short)U16(o));

    public uint U32(int o)
    {
        if (o < 0 || o + 4 > Data.Length) return 0;
        return ((uint)Data[o] << 24) | ((uint)Data[o + 1] << 16) | ((uint)Data[o + 2] << 8) | Data[o + 3];
    }

    public string Tag(int o)
    {
        if (o < 0 || o + 4 > Data.Length) return "";
        return new string(new[] { (char)Data[o], (char)Data[o + 1], (char)Data[o + 2], (char)Data[o + 3] });
    }
}
