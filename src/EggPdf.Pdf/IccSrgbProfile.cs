using System;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Generates a minimal but structurally valid ICC v2 RGB Display (matrix/TRC) profile
/// approximating sRGB (IEC 61966-2-1), for PDF/A OutputIntent's required DestOutputProfile.
/// Primaries and white point are the standard D50-adapted sRGB values; the tone curve is
/// approximated as a single gamma-2.2 'curv' tag rather than sRGB's exact piecewise curve --
/// sufficient for the structural conformance a PDF/A OutputIntent requires, not a colorimetric
/// reference profile. The profile is identical on every call, so it is built once.
/// </summary>
public static class IccSrgbProfile
{
    private const int HeaderSize = 128;
    private const int TagEntrySize = 12;
    private const uint ProfileVersion2_1 = 0x02100000;
    private const uint PerceptualIntent = 0;
    private const double D50X = 0.9642, D50Y = 1.0000, D50Z = 0.8249;

    private static readonly byte[] Cached = Build();

    /// <summary>A fresh copy of the generated profile bytes, safe for the caller to treat as owned.</summary>
    public static byte[] Generate() => (byte[])Cached.Clone();

    /// <summary>
    /// Builds the tag data first so offsets and sizes are known before the header is laid out; every tag starts on a
    /// 4-byte boundary (ICC.1:2001-04 §6.5). The preferred CMM, platform, flags, device attributes, profile ID and the
    /// reserved header bytes are zero.
    /// </summary>
    private static byte[] Build()
    {
        var tags = new (string sig, byte[] data)[]
        {
            ("desc", BuildDescTag("sRGB IEC61966-2.1")),
            ("cprt", BuildTextTag("Public Domain")),
            ("wtpt", BuildXyzTag(D50X, D50Y, D50Z)),
            ("rXYZ", BuildXyzTag(0.4360, 0.2225, 0.0139)),
            ("gXYZ", BuildXyzTag(0.3851, 0.7169, 0.0971)),
            ("bXYZ", BuildXyzTag(0.1431, 0.0606, 0.7139)),
            ("rTRC", BuildGammaCurveTag(2.2)),
            ("gTRC", BuildGammaCurveTag(2.2)),
            ("bTRC", BuildGammaCurveTag(2.2)),
        };

        int tagTableSize = 4 + tags.Length * TagEntrySize;
        int dataStart = HeaderSize + tagTableSize;

        var offsets = new int[tags.Length];
        var sizes = new int[tags.Length];
        int pos = dataStart;
        for (int i = 0; i < tags.Length; i++)
        {
            offsets[i] = pos;
            sizes[i] = tags[i].data.Length;
            pos += Pad4(sizes[i]);
        }
        int totalSize = pos;

        var buf = new byte[totalSize];

        WriteU32(buf, 0, (uint)totalSize);
        WriteU32(buf, 8, ProfileVersion2_1);
        WriteAscii(buf, 12, "mntr");
        WriteAscii(buf, 16, "RGB ");
        WriteAscii(buf, 20, "XYZ ");
        var now = DateTime.UtcNow;
        WriteU16(buf, 24, (ushort)now.Year);
        WriteU16(buf, 26, (ushort)now.Month);
        WriteU16(buf, 28, (ushort)now.Day);
        WriteU16(buf, 30, (ushort)now.Hour);
        WriteU16(buf, 32, (ushort)now.Minute);
        WriteU16(buf, 34, (ushort)now.Second);
        WriteAscii(buf, 36, "acsp");
        WriteU32(buf, 64, PerceptualIntent);
        WriteS15Fixed16(buf, 68, D50X);
        WriteS15Fixed16(buf, 72, D50Y);
        WriteS15Fixed16(buf, 76, D50Z);
        WriteAscii(buf, 80, "EGGP");

        WriteU32(buf, HeaderSize, (uint)tags.Length);
        for (int i = 0; i < tags.Length; i++)
        {
            int entryOffset = HeaderSize + 4 + i * TagEntrySize;
            WriteAscii(buf, entryOffset, tags[i].sig);
            WriteU32(buf, entryOffset + 4, (uint)offsets[i]);
            WriteU32(buf, entryOffset + 8, (uint)sizes[i]);
        }

        for (int i = 0; i < tags.Length; i++)
            Array.Copy(tags[i].data, 0, buf, offsets[i], tags[i].data.Length);

        return buf;
    }

    private static int Pad4(int size) => (size + 3) & ~3;

    /// <summary>
    /// textDescriptionType ('desc') -- ICC.1:2001-04 §6.5.17: signature, reserved, ASCII count and NUL-terminated text,
    /// then an empty Unicode description (language, count), an empty ScriptCode description (code, count) and a 67-byte
    /// zeroed Macintosh description.
    /// </summary>
    private static byte[] BuildDescTag(string ascii)
    {
        var asciiBytes = Encoding.ASCII.GetBytes(ascii);
        int countWithTerminator = asciiBytes.Length + 1;
        var buf = new byte[4 + 4 + 4 + countWithTerminator + 4 + 4 + 2 + 1 + 67];
        WriteAscii(buf, 0, "desc");
        WriteU32(buf, 8, (uint)countWithTerminator);
        Array.Copy(asciiBytes, 0, buf, 12, asciiBytes.Length);
        return buf;
    }

    /// <summary>textType ('text') -- ICC.1:2001-04 §6.5.18: signature, reserved, NUL-terminated text.</summary>
    private static byte[] BuildTextTag(string ascii)
    {
        var asciiBytes = Encoding.ASCII.GetBytes(ascii);
        var buf = new byte[4 + 4 + asciiBytes.Length + 1];
        WriteAscii(buf, 0, "text");
        Array.Copy(asciiBytes, 0, buf, 8, asciiBytes.Length);
        return buf;
    }

    /// <summary>XYZType ('XYZ ') -- a single CIE XYZ triplet as s15Fixed16Number.</summary>
    private static byte[] BuildXyzTag(double x, double y, double z)
    {
        var buf = new byte[4 + 4 + 12];
        WriteAscii(buf, 0, "XYZ ");
        WriteS15Fixed16(buf, 8, x);
        WriteS15Fixed16(buf, 12, y);
        WriteS15Fixed16(buf, 16, z);
        return buf;
    }

    /// <summary>curveType ('curv') with a single entry -- a pure power-law gamma curve as a u8Fixed8Number.</summary>
    private static byte[] BuildGammaCurveTag(double gamma)
    {
        var buf = new byte[4 + 4 + 4 + 2];
        WriteAscii(buf, 0, "curv");
        WriteU32(buf, 8, 1);
        WriteU16(buf, 12, (ushort)Math.Round(gamma * 256.0));
        return buf;
    }

    private static void WriteAscii(byte[] buf, int offset, string ascii)
    {
        var bytes = Encoding.ASCII.GetBytes(ascii);
        Array.Copy(bytes, 0, buf, offset, bytes.Length);
    }

    private static void WriteU16(byte[] buf, int offset, ushort value)
    {
        buf[offset] = (byte)(value >> 8);
        buf[offset + 1] = (byte)value;
    }

    private static void WriteU32(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static void WriteS15Fixed16(byte[] buf, int offset, double value)
        => WriteU32(buf, offset, unchecked((uint)(int)Math.Round(value * 65536.0)));
}
