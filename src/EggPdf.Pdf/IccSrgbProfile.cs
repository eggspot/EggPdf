using System;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Generates a minimal but structurally valid ICC v2 RGB Display (matrix/TRC) profile
/// approximating sRGB (IEC 61966-2-1), for PDF/A OutputIntent's required DestOutputProfile.
/// Primaries and white point are the standard D50-adapted sRGB values; the tone curve is
/// approximated as a single gamma-2.2 'curv' tag rather than sRGB's exact piecewise curve --
/// sufficient for the structural conformance a PDF/A OutputIntent requires, not a colorimetric
/// reference profile.
/// </summary>
public static class IccSrgbProfile
{
    public static byte[] Generate()
    {
        // Tag data, built first so offsets/sizes are known before the header is laid out.
        var tags = new (string sig, byte[] data)[]
        {
            ("desc", BuildDescTag("sRGB IEC61966-2.1")),
            ("cprt", BuildTextTag("Public Domain")),
            ("wtpt", BuildXyzTag(0.9642, 1.0000, 0.8249)), // D50 PCS white point
            ("rXYZ", BuildXyzTag(0.4360, 0.2225, 0.0139)), // D50-adapted sRGB primaries
            ("gXYZ", BuildXyzTag(0.3851, 0.7169, 0.0971)),
            ("bXYZ", BuildXyzTag(0.1431, 0.0606, 0.7139)),
            ("rTRC", BuildGammaCurveTag(2.2)),
            ("gTRC", BuildGammaCurveTag(2.2)),
            ("bTRC", BuildGammaCurveTag(2.2)),
        };

        int tagTableSize = 4 + tags.Length * 12;
        int dataStart = 128 + tagTableSize;

        // Every tag data element must start on a 4-byte boundary (ICC.1:2001-04 §6.5).
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

        // Header (128 bytes)
        WriteU32(buf, 0, (uint)totalSize);
        WriteU32(buf, 8, 0x02100000); // profile version 2.1.0
        WriteAscii(buf, 12, "mntr");  // device class: display
        WriteAscii(buf, 16, "RGB ");  // data colour space
        WriteAscii(buf, 20, "XYZ ");  // profile connection space
        var now = DateTime.UtcNow;
        WriteU16(buf, 24, (ushort)now.Year);
        WriteU16(buf, 26, (ushort)now.Month);
        WriteU16(buf, 28, (ushort)now.Day);
        WriteU16(buf, 30, (ushort)now.Hour);
        WriteU16(buf, 32, (ushort)now.Minute);
        WriteU16(buf, 34, (ushort)now.Second);
        WriteAscii(buf, 36, "acsp"); // profile file signature (required)
        WriteU32(buf, 64, 0);        // rendering intent: perceptual
        WriteS15Fixed16(buf, 68, 0.9642); // PCS illuminant (always D50, per spec)
        WriteS15Fixed16(buf, 72, 1.0000);
        WriteS15Fixed16(buf, 76, 0.8249);
        WriteAscii(buf, 80, "EGGP"); // profile creator (informational)
        // Preferred CMM, primary platform, flags, manufacturer/model/attributes,
        // profile ID, and the trailing reserved bytes are all legitimately zero.

        // Tag table
        WriteU32(buf, 128, (uint)tags.Length);
        for (int i = 0; i < tags.Length; i++)
        {
            int entryOffset = 132 + i * 12;
            WriteAscii(buf, entryOffset, tags[i].sig);
            WriteU32(buf, entryOffset + 4, (uint)offsets[i]);
            WriteU32(buf, entryOffset + 8, (uint)sizes[i]);
        }

        // Tag data
        for (int i = 0; i < tags.Length; i++)
            Array.Copy(tags[i].data, 0, buf, offsets[i], tags[i].data.Length);

        return buf;
    }

    private static int Pad4(int size) => (size + 3) & ~3;

    /// <summary>textDescriptionType ('desc') -- ICC.1:2001-04 §6.5.17.</summary>
    private static byte[] BuildDescTag(string ascii)
    {
        var asciiBytes = Encoding.ASCII.GetBytes(ascii);
        int n = asciiBytes.Length + 1; // includes the NUL terminator
        // sig(4) + reserved(4) + count(4) + ascii(n) + unicodeLang(4) + unicodeCount(4) + scriptCode(2) + macCount(1) + macDesc(67)
        var buf = new byte[4 + 4 + 4 + n + 4 + 4 + 2 + 1 + 67];
        WriteAscii(buf, 0, "desc");
        WriteU32(buf, 8, (uint)n);
        Array.Copy(asciiBytes, 0, buf, 12, asciiBytes.Length);
        // NUL terminator, Unicode lang/count, ScriptCode, and the Mac description are all
        // legitimately zero (no Unicode/Mac variant supplied).
        return buf;
    }

    /// <summary>textType ('text') -- ICC.1:2001-04 §6.5.18.</summary>
    private static byte[] BuildTextTag(string ascii)
    {
        var asciiBytes = Encoding.ASCII.GetBytes(ascii);
        int n = asciiBytes.Length + 1; // includes the NUL terminator
        var buf = new byte[4 + 4 + n];
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

    /// <summary>curveType ('curv') with a single entry -- a pure power-law gamma curve.</summary>
    private static byte[] BuildGammaCurveTag(double gamma)
    {
        var buf = new byte[4 + 4 + 4 + 2];
        WriteAscii(buf, 0, "curv");
        WriteU32(buf, 8, 1); // count = 1 -> single u8Fixed8Number gamma value
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
